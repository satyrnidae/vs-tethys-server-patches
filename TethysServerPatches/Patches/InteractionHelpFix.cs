using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace TethysServerPatches.Patches;

/// <summary>
/// Shared state for the interaction help async patch. Created by TethysServerPatchesClient
/// before the category is applied; disposed when the mod unloads.
/// </summary>
sealed class InteractionHelpFixState : IDisposable
{
    public static InteractionHelpFixState Instance { get; private set; }

    public static InteractionHelpFixState Create(int gameThreadId)
    {
        Instance = new InteractionHelpFixState(gameThreadId);
        return Instance;
    }

    InteractionHelpFixState(int gameThreadId) => GameThreadId = gameThreadId;

    /// <summary>ManagedThreadId of the VS game/render thread, set at creation time.</summary>
    public int GameThreadId { get; }

    /// <summary>Client API reference, set in StartClientSide.</summary>
    public ICoreClientAPI Capi { get; set; }

    // ----- PendingCompose — thread-safe Action queue -----
    // Getter atomically takes and clears the pending action (take-and-clear semantics).
    // Setter atomically replaces the pending action (background thread queues here).

    private volatile Action _pendingCompose;

    public Action PendingCompose
    {
        get => Interlocked.Exchange(ref _pendingCompose, null);
        set => Interlocked.Exchange(ref _pendingCompose, value);
    }

    /// <summary>
    /// Re-entry guard: true while vanilla's compose body is running with pre-swapped delegates,
    /// so a recursive call to ComposeBlockWorldInteractionHelp passes through unmodified.
    /// </summary>
    public bool IsComposing { get; set; }

    // ----- Result cache -----
    // Keyed on (blockId, x, y, z, selectionBoxIndex) so the expensive resolve only runs once
    // per distinct block the player targets.

    public (int blockId, int x, int y, int z, int selIdx) CacheKey { get; set; }
    public WorldInteraction[] CachedInteractions { get; set; }
    public ItemStack[][] CachedStacks { get; set; }

    public void Dispose()
    {
        PendingCompose     = null;
        Capi               = null;
        IsComposing        = false;
        CachedInteractions = null;
        CachedStacks       = null;
        if (Instance == this)
            Instance = null;
    }
}

// ---------------------------------------------------------------------------
// Patch 1 - FrameProfilerUtil.Enter thread-safety guard
//
// ARL's FindByVariant calls FrameProfilerUtil.Enter regardless of which thread
// it is on.  The profiler is not thread-safe and NPEs when Enter is called from
// a thread pool thread.  This prefix makes Enter a no-op off the game thread
// so resolving GetMatchingStacks in Task.Run is safe.
// ---------------------------------------------------------------------------
[HarmonyPatch]
[HarmonyPatchCategory("interactionhelpfix")]
class FrameProfilerUtil_Enter_ThreadGuard
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(FrameProfilerUtil), "Enter", [typeof(string)]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error("[interactionhelpfix] Could not find FrameProfilerUtil.Enter - skipping thread guard");
            yield break;
        }
        yield return method;
    }

    static bool Prefix()
    {
        if (TethysServerPatchesCore.Configuration?.VanillaFixes.AsyncInteractionHelp == false)
            return true;
        var state = InteractionHelpFixState.Instance;
        if (state == null) return true;
        return Thread.CurrentThread.ManagedThreadId == state.GameThreadId;
    }
}

// ---------------------------------------------------------------------------
// Patch 2 - DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp(WorldInteraction[])
//
// The previous implementation patched the no-arg HudElementInteractionHelp
// wrapper.  On .NET 10 the JIT inlines that tiny private method, making the
// Harmony prefix unreachable; profiling confirms the ~6 s freeze still lands
// inside this util method.  Patching the inner method directly is reliable.
//
// Strategy:
//   Cache hit  -> swap all GetMatchingStacks delegates to pre-resolved Itemstacks,
//                 null the delegates, let vanilla compose run fast (~1 ms), restore.
//   Cache miss -> null all GetMatchingStacks so vanilla runs instantly without
//                 delegated-item rows (interaction help appears immediately, partial);
//                 resolve off-thread; next game tick PendingCompose updates the cache
//                 and re-invokes this method, which then takes the cache-hit path.
// ---------------------------------------------------------------------------
[HarmonyPatch]
[HarmonyPatchCategory("interactionhelpfix")]
class DrawWorldInteractionUtil_ComposeBlockWorldInteractionHelp_Patch
{
    static MethodInfo _wiUtilCompose;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(DrawWorldInteractionUtil),
            "ComposeBlockWorldInteractionHelp", [typeof(WorldInteraction[])]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[interactionhelpfix] Could not find DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp(WorldInteraction[]) - async patch will not apply");
            yield break;
        }
        _wiUtilCompose = method;
        yield return method;
    }

    // State passed from Prefix to Finalizer so the Finalizer can restore the
    // WorldInteraction objects that the Prefix modified.
    class PatchState
    {
        public WorldInteraction[] ActiveWis;
        public ItemStack[][] OrigItemstacks;
        public InteractionStacksDelegate[] OrigDelegates;
    }

    static bool Prefix(DrawWorldInteractionUtil __instance, ref WorldInteraction[] wis, out PatchState __state)
    {
        __state = null;

        if (TethysServerPatchesCore.Configuration?.VanillaFixes.AsyncInteractionHelp == false)
            return true;

        var state = InteractionHelpFixState.Instance;
        if (state == null) return true;

        // Re-entry: PendingCompose called us with pre-swapped delegates already in place.
        if (state.IsComposing)
            return true;

        var capi = state.Capi;
        if (capi == null || wis == null || wis.Length == 0)
            return true;

        // Per-ActionLangCode cap: show at most N entries per interaction type so that
        // every category is represented even when one type has many variants (e.g. anvil metals).
        int max = TethysServerPatchesCore.Configuration?.VanillaFixes.MaxInteractionHelpEntries ?? 16;
        if (max > 0)
        {
            var counts = new Dictionary<string, int>();
            var outWis = new List<WorldInteraction>(wis.Length);
            foreach (var wi in wis)
            {
                var actionKey = wi.ActionLangCode ?? "";
                counts.TryGetValue(actionKey, out int n);
                if (n < max) { counts[actionKey] = n + 1; outWis.Add(wi); }
            }
            wis = [..outWis];
        }

        // Only intercept if at least one interaction has a (potentially slow) delegate.
        bool hasDelegate = false;
        foreach (var wi in wis)
            if (wi.Itemstacks != null && wi.GetMatchingStacks != null) { hasDelegate = true; break; }
        if (!hasDelegate) return true;

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null) return true;
        var block = capi.World.BlockAccessor.GetBlock(blockSel.Position);
        if (block == null || block.BlockId == 0) return true;

        var key = (block.BlockId,
                   blockSel.Position.X,
                   blockSel.Position.Y,
                   blockSel.Position.Z,
                   blockSel.SelectionBoxIndex);

        // Save originals — the Finalizer restores these after vanilla's compose body
        // runs, even if the body throws.
        var origItemstacks = new ItemStack[wis.Length][];
        var origDelegates  = new InteractionStacksDelegate[wis.Length];
        for (int i = 0; i < wis.Length; i++)
        {
            origItemstacks[i] = wis[i].Itemstacks;
            origDelegates[i]  = wis[i].GetMatchingStacks;
        }
        __state = new PatchState { ActiveWis = wis, OrigItemstacks = origItemstacks, OrigDelegates = origDelegates };
        state.IsComposing = true;

        // Cache hit: swap to pre-resolved stacks, vanilla compose runs without any delegate calls.
        if (state.CacheKey == key
            && state.CachedStacks != null
            && state.CachedStacks.Length == wis.Length)
        {
            for (int i = 0; i < wis.Length; i++)
            {
                wis[i].GetMatchingStacks = null;
                if (state.CachedStacks[i] != null)
                    wis[i].Itemstacks = state.CachedStacks[i];
            }
            return true;
        }

        // Cache miss: null delegates so vanilla runs instantly (partial help, no delegated items).
        for (int i = 0; i < wis.Length; i++)
            wis[i].GetMatchingStacks = null;

        // Capture everything the background task needs before leaving the main thread.
        var capturedWis        = wis;
        var capturedWiUtil     = __instance;
        var capturedKey        = key;
        var capturedItemstacks = origItemstacks;
        var capturedDelegates  = origDelegates;
        var capturedBlockSel   = blockSel;
        var capturedEntitySel  = capi.World.Player.CurrentEntitySelection;
        var capturedState      = state;

        Task.Run(() =>
        {
            var resolved = new ItemStack[capturedWis.Length][];
            for (int i = 0; i < capturedWis.Length; i++)
            {
                if (capturedItemstacks[i] != null && capturedDelegates[i] != null)
                {
                    try   { resolved[i] = capturedDelegates[i](capturedWis[i], capturedBlockSel, capturedEntitySel) ?? capturedItemstacks[i]; }
                    catch { resolved[i] = capturedItemstacks[i]; }
                }
                else
                {
                    resolved[i] = capturedItemstacks[i];
                }
            }

            capturedState.PendingCompose = () =>
            {
                capturedState.CacheKey           = capturedKey;
                capturedState.CachedInteractions = capturedWis;
                capturedState.CachedStacks       = resolved;

                // Only recompose if the player is still targeting the same block;
                // otherwise the cache is primed for the next time they look at it.
                var c   = capturedState.Capi;
                var sel = c?.World.Player.CurrentBlockSelection;
                if (sel == null) return;
                var blk = c.World.BlockAccessor.GetBlock(sel.Position);
                if (blk == null) return;
                var cur = (blk.BlockId, sel.Position.X, sel.Position.Y, sel.Position.Z, sel.SelectionBoxIndex);
                if (cur != capturedKey) return;

                _wiUtilCompose.Invoke(capturedWiUtil, [capturedWis]);
            };
        });

        return true;
    }

    // Runs after vanilla's compose body (even if it threw) to restore the
    // WorldInteraction objects back to their original state.
    static void Finalizer(PatchState __state)
    {
        if (__state == null) return;
        var wis = __state.ActiveWis;
        for (int i = 0; i < wis.Length; i++)
        {
            wis[i].Itemstacks        = __state.OrigItemstacks[i];
            wis[i].GetMatchingStacks = __state.OrigDelegates[i];
        }
        if (InteractionHelpFixState.Instance is {} state)
            state.IsComposing = false;
    }
}
