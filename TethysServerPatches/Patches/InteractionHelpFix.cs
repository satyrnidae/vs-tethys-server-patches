using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using TethysServerPatches.State;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace TethysServerPatches.Patches;

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
// Patch 2 - HudElementInteractionHelp.getWorldInteractions() wis population cache
//
// This is the single call site that dispatches (via vtable) to either
// block.GetPlacedBlockInteractionHelp or entity.GetInteractionHelp.  Patching
// here - rather than on the base Block/Entity methods - correctly intercepts
// overrides like BlockAnvil, which does its expensive ObjectCacheUtil item-
// registry scan inside its own override before the base call.
//
//   Cache hit  -> return cached wis immediately (sub-ms), skip original.
//   Computing  -> semaphore already held; return [] so the HUD shows nothing
//                 rather than stalling.
//   Cache miss -> acquire semaphore, return [], launch Task.Run.
//                 Task calls the virtual method directly (vtable dispatch)
//                 off the main thread.  On completion: store cache, release
//                 semaphore, set PendingCompose so the HUD recomposes.
// ---------------------------------------------------------------------------
[HarmonyPatch]
[HarmonyPatchCategory("interactionhelpfix")]
class HudElementInteractionHelp_GetWorldInteractions_WisCache
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(HudElementInteractionHelp), "getWorldInteractions");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error("[interactionhelpfix] Could not find HudElementInteractionHelp.getWorldInteractions - wis population cache will not apply");
            yield break;
        }
        yield return method;
    }

    static bool Prefix(ref WorldInteraction[] __result)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaFixes.AsyncInteractionHelp == false)
            return true;

        var state = InteractionHelpFixState.Instance;
        if (state == null) return true;

        // Background tasks call GetPlacedBlockInteractionHelp/GetInteractionHelp directly,
        // never through this method, so no re-entry risk; BypassWisCache not needed here.

        var capi = state.Capi;
        if (capi == null) return true;

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel != null)
        {
            var block = blockSel.DidOffset
                ? capi.World.BlockAccessor.GetBlock(blockSel.Position.AddCopy(blockSel.Face.Opposite.Normali))
                : capi.World.BlockAccessor.GetBlock(blockSel.Position);
            if (block == null || block.BlockId == 0) return true;

            int blockId = block.BlockId;

            if (state.BlockWisCache.TryGetValue(blockId, out var cached))
            {
                __result = cached;
                return false;
            }

            var sem = state.BlockWisLocks.GetOrAdd(blockId, _ => new SemaphoreSlim(1, 1));
            if (!sem.Wait(0)) { __result = []; return false; }

            __result = [];

            var capturedBlock  = block;
            var capturedWorld  = capi.World;
            var capturedSel    = blockSel;
            var capturedPlayer = capi.World.Player;
            var capturedId     = blockId;
            var capturedSem    = sem;
            var capturedState  = state;

            Task.Run(() =>
            {
                WorldInteraction[] wis;
                try   { wis = capturedBlock.GetPlacedBlockInteractionHelp(capturedWorld, capturedSel, capturedPlayer) ?? []; }
                catch { wis = []; }
                finally { capturedSem.Release(); }

                capturedState.BlockWisCache[capturedId] = wis;

                capturedState.PendingCompose = () =>
                {
                    var c      = capturedState.Capi;
                    var sel    = c?.World.Player.CurrentBlockSelection;
                    if (sel == null) return;
                    var blk    = sel.DidOffset
                        ? c.World.BlockAccessor.GetBlock(sel.Position.AddCopy(sel.Face.Opposite.Normali))
                        : c.World.BlockAccessor.GetBlock(sel.Position);
                    if (blk?.BlockId != capturedId) return;
                    var wiUtil  = capturedState.WiUtil;
                    var compose = capturedState.WiUtilComposeMethod;
                    if (wiUtil == null || compose == null) return;
                    compose.Invoke(wiUtil, [wis]);
                };
            });

            return false;
        }

        var entSel = capi.World.Player.CurrentEntitySelection;
        if (entSel?.Entity != null && entSel.Entity is not EntityItem)
        {
            var entity     = entSel.Entity;
            var entityCode = entity.Code?.ToString();
            if (entityCode == null) return true;

            if (state.EntityWisCache.TryGetValue(entityCode, out var cached))
            {
                __result = cached;
                return false;
            }

            var sem = state.EntityWisLocks.GetOrAdd(entityCode, _ => new SemaphoreSlim(1, 1));
            if (!sem.Wait(0)) { __result = []; return false; }

            __result = [];

            var capturedEntity = entity;
            var capturedWorld  = capi.World;
            var capturedEs     = entSel;
            var capturedPlayer = capi.World.Player;
            var capturedCode   = entityCode;
            var capturedSem    = sem;
            var capturedState  = state;

            Task.Run(() =>
            {
                WorldInteraction[] wis;
                try   { wis = capturedEntity.GetInteractionHelp(capturedWorld, capturedEs, capturedPlayer) ?? []; }
                catch { wis = []; }
                finally { capturedSem.Release(); }

                capturedState.EntityWisCache[capturedCode] = wis;

                capturedState.PendingCompose = () =>
                {
                    var c      = capturedState.Capi;
                    var esel   = c?.World.Player.CurrentEntitySelection;
                    if (esel?.Entity?.Code?.ToString() != capturedCode) return;
                    var wiUtil  = capturedState.WiUtil;
                    var compose = capturedState.WiUtilComposeMethod;
                    if (wiUtil == null || compose == null) return;
                    compose.Invoke(wiUtil, [wis]);
                };
            });

            return false;
        }

        return true;
    }
}

// ---------------------------------------------------------------------------
// Patch 3 - DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp(WorldInteraction[])
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

        // Capture WiUtil and compose method for wis population cache recompose triggers.
        state.WiUtil ??= __instance;
        state.WiUtilComposeMethod ??= _wiUtilCompose;

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
            if (wi.GetMatchingStacks != null) { hasDelegate = true; break; }
        if (!hasDelegate) return true;

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null) return true;
        var block = blockSel.DidOffset
            ? capi.World.BlockAccessor.GetBlock(blockSel.Position.AddCopy(blockSel.Face.Opposite.Normali))
            : capi.World.BlockAccessor.GetBlock(blockSel.Position);
        if (block == null || block.BlockId == 0) return true;

        var key = (block.BlockId,
                   blockSel.Position.X,
                   blockSel.Position.Y,
                   blockSel.Position.Z,
                   blockSel.SelectionBoxIndex);

        // Save originals - the Finalizer restores these after vanilla's compose body
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
                if (capturedDelegates[i] != null)
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
                var blk = sel.DidOffset
                    ? c.World.BlockAccessor.GetBlock(sel.Position.AddCopy(sel.Face.Opposite.Normali))
                    : c.World.BlockAccessor.GetBlock(sel.Position);
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
