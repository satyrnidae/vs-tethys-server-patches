using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace TethysServerPatches.Patches;

// Suppresses a NullReferenceException in FromGoldenCombs' PushEventOnBlockHarvested
// that disconnects any player who harvests a block whose drop table contains a null
// BlockDropItemStack (reproducibly triggered by harvesting wild fruiting bushes such
// as strawberries).
//
// Root cause: PushEventOnBlockHarvested.OnBlockInteractStop iterates over the block's
// drop stacks via EnumerableExtensions.Foreach and passes each to a lambda that
// immediately dereferences the stack without a null guard. When the bush yields a
// null entry the lambda throws, which propagates to the server's packet handler and
// force-disconnects the client.
//
// Fix: Finalizer catches the NullReferenceException, logs a warning, and swallows it
// so the server tick completes normally. The player loses the FGC harvest event for
// that interaction but is not disconnected.
[HarmonyPatch]
[HarmonyPatchCategory("fromgoldencombs")]
class PushEventOnBlockHarvested_OnBlockInteractStop_NullFix
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("FromGoldenCombs.BlockBehaviors.PushEventOnBlockHarvested");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[fromgoldencombs] Could not find PushEventOnBlockHarvested — null-harvest patch will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "OnBlockInteractStop");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[fromgoldencombs] Could not find OnBlockInteractStop — null-harvest patch will not apply");
            yield break;
        }

        yield return method;
    }

    static Exception Finalizer(Exception __exception, IPlayer byPlayer)
    {
        if (__exception is NullReferenceException)
        {
            TethysServerPatchesCore.Logger.Warning(
                $"[fromgoldencombs] Suppressed NullReferenceException in PushEventOnBlockHarvested." +
                $"OnBlockInteractStop for player '{byPlayer?.PlayerName ?? "unknown"}'. " +
                $"A drop stack was null; the FGC harvest event was skipped but the player was not disconnected.");
            return null;
        }

        return __exception;
    }
}
