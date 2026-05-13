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
/// Shared state for the interaction help async patch. Initialised by TethysServerPatchesClient
/// before the category is applied.
/// </summary>
static class InteractionHelpFixState
{
    /// <summary>ManagedThreadId of the VS game/render thread, set at patch-apply time.</summary>
    public static int GameThreadId;

    /// <summary>Client API reference, set in StartClientSide.</summary>
    public static ICoreClientAPI Capi;

    /// <summary>
    /// Compose action queued by the background resolver for execution on the game thread.
    /// Written from the thread pool, read + cleared atomically by the game-tick drain listener.
    /// </summary>
    public static volatile Action PendingCompose;

    // ----- Result cache -----
    // Keyed on (blockId, x, y, z, selectionBoxIndex) so the expensive resolve only runs once
    // per distinct block the player targets.

    public static (int blockId, int x, int y, int z, int selIdx) CacheKey;
    /// <summary>The WorldInteraction[] that was resolved for CacheKey (same reference as passed to compose).</summary>
    public static WorldInteraction[] CachedInteractions;
    /// <summary>Pre-resolved ItemStack[] per interaction index for CacheKey.</summary>
    public static ItemStack[][] CachedStacks;
}

// ---------------------------------------------------------------------------
// Patch 1 - FrameProfilerUtil.Enter thread-safety guard
//
// ARL's FindByVariant calls FrameProfilerUtil.Enter regardless of which thread
// it is on.  The profiler is not thread-safe and NPEs when Enter is called from
// a thread pool thread (null currentEntry or null stopwatch context depending on
// game version).  This prefix makes Enter a no-op off the game thread so moving
// GetMatchingStacks resolution to Task.Run is safe.
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
        if (TethysServerPatchesCore.Configuration?.VanillaFixes.AsyncInteractionHelp != true)
            return true;
        return Thread.CurrentThread.ManagedThreadId == InteractionHelpFixState.GameThreadId;
    }
}

// ---------------------------------------------------------------------------
// Patch 2 - HudElementInteractionHelp.ComposeBlockWorldInteractionHelp (no-arg)
//
// The private ComposeBlockWorldInteractionHelp() triggers
// DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp(WorldInteraction[])
// which iterates each WorldInteraction and calls wi.GetMatchingStacks().
// For blocks patched by AttributeRenderingLibrary + ImprovedMetallurgy the
// GetMatchingStacks delegate calls FindByVariant which can take 1-6 seconds on
// the main thread.
//
// Strategy:
//   - Cache hit  -> swap delegates to pre-resolved stacks, call compose, restore.
//                  Main-thread cost is only the fast Cairo/GUI work (~1 ms).
//   - Cache miss -> resolve GetMatchingStacks off-thread in a Task, store a
//                  pending compose action that the game-tick drain listener
//                  executes on the next tick.  Returns false immediately so the
//                  main thread is never blocked.
// ---------------------------------------------------------------------------
[HarmonyPatch]
[HarmonyPatchCategory("interactionhelpfix")]
class HudElementInteractionHelp_ComposeBlockWorldInteractionHelp_Async
{
    static FieldInfo _wiUtilField;
    static MethodInfo _getWorldInteractions;
    static MethodInfo _wiUtilCompose;

    static IEnumerable<MethodBase> TargetMethods()
    {
        // Private no-arg overload that orchestrates getWorldInteractions() + wiUtil.Compose()
        var method = AccessTools.Method(typeof(HudElementInteractionHelp),
            "ComposeBlockWorldInteractionHelp", Type.EmptyTypes);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[interactionhelpfix] Could not find HudElementInteractionHelp.ComposeBlockWorldInteractionHelp() - interaction help async patch will not apply");
            yield break;
        }

        _wiUtilField = AccessTools.Field(typeof(HudElementInteractionHelp), "wiUtil");
        _getWorldInteractions = AccessTools.Method(typeof(HudElementInteractionHelp),
            "getWorldInteractions", Type.EmptyTypes);
        _wiUtilCompose = AccessTools.Method(typeof(DrawWorldInteractionUtil),
            "ComposeBlockWorldInteractionHelp", [typeof(WorldInteraction[])]);

        if (_wiUtilField == null || _getWorldInteractions == null || _wiUtilCompose == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[interactionhelpfix] One or more reflected members not found - interaction help async patch will not apply");
            yield break;
        }

        yield return method;
    }

    static bool Prefix(HudElementInteractionHelp __instance)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaFixes.AsyncInteractionHelp != true)
            return true;
        if (!__instance.IsOpened()) return true;

        var capi = InteractionHelpFixState.Capi;
        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null) return true;

        var block = capi.World.BlockAccessor.GetBlock(blockSel.Position);
        if (block == null || block.BlockId == 0) return true;

        var key = (block.BlockId,
                   blockSel.Position.X,
                   blockSel.Position.Y,
                   blockSel.Position.Z,
                   blockSel.SelectionBoxIndex);

        var wiUtil = (DrawWorldInteractionUtil)_wiUtilField.GetValue(__instance);

        // -- Cache hit: compose instantly on the main thread ------------------
        if (InteractionHelpFixState.CacheKey == key
            && InteractionHelpFixState.CachedInteractions != null
            && InteractionHelpFixState.CachedStacks != null)
        {
            ComposeWithPreResolved(wiUtil,
                InteractionHelpFixState.CachedInteractions,
                InteractionHelpFixState.CachedStacks);
            return false;
        }

        // -- Cache miss: get interaction definitions (fast) and resolve off-thread --
        var interactions = (WorldInteraction[])_getWorldInteractions.Invoke(__instance, null);
        if (interactions == null || interactions.Length == 0) return true;

        // Only async-path if at least one interaction has a GetMatchingStacks delegate;
        // if none do, the vanilla loop is already fast.
        bool needsAsync = false;
        foreach (var wi in interactions)
        {
            if (wi.Itemstacks != null && wi.GetMatchingStacks != null) { needsAsync = true; break; }
        }
        if (!needsAsync) return true;

        // Capture mutable game-state references before leaving the main thread.
        // BlockSelection objects are replaced (not mutated) by the game loop, so
        // holding the reference is safe for reads.
        var capturedBlockSel   = blockSel;
        var capturedEntitySel  = capi.World.Player.CurrentEntitySelection;
        var capturedKey        = key;
        var capturedWiUtil     = wiUtil;

        Task.Run(() =>
        {
            var resolved = new ItemStack[interactions.Length][];
            for (int i = 0; i < interactions.Length; i++)
            {
                var wi = interactions[i];
                if (wi.Itemstacks != null && wi.GetMatchingStacks != null)
                {
                    try   { resolved[i] = wi.GetMatchingStacks(wi, capturedBlockSel, capturedEntitySel); }
                    catch { resolved[i] = wi.Itemstacks; }
                }
                else
                {
                    resolved[i] = wi.Itemstacks;
                }
            }

            // Queue the compose for the game-tick drain listener (never blocks the main thread).
            Interlocked.Exchange(ref InteractionHelpFixState.PendingCompose, () =>
            {
                ComposeWithPreResolved(capturedWiUtil, interactions, resolved);
                InteractionHelpFixState.CacheKey          = capturedKey;
                InteractionHelpFixState.CachedInteractions = interactions;
                InteractionHelpFixState.CachedStacks       = resolved;
            });
        });

        return false; // main thread is free immediately; tooltip appears on next tick
    }

    /// <summary>
    /// Temporarily replaces each interaction's GetMatchingStacks delegate with a
    /// lambda returning pre-resolved stacks, calls the vanilla compose method
    /// (now fast - no ARL iteration), then restores the original delegates.
    /// Must be called on the main/game thread.
    /// </summary>
    static void ComposeWithPreResolved(
        DrawWorldInteractionUtil wiUtil,
        WorldInteraction[]       interactions,
        ItemStack[][]            resolved)
    {
        var originals = new InteractionStacksDelegate[interactions.Length];
        for (int i = 0; i < interactions.Length; i++)
        {
            originals[i] = interactions[i].GetMatchingStacks;
            var stacks = resolved[i];
            interactions[i].GetMatchingStacks = stacks != null
                ? (wi, bs, es) => stacks
                : null;
        }
        try   { _wiUtilCompose.Invoke(wiUtil, [interactions]); }
        finally
        {
            for (int i = 0; i < interactions.Length; i++)
                interactions[i].GetMatchingStacks = originals[i];
        }
    }
}
