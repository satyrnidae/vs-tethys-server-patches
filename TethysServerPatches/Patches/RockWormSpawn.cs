using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TethysServerPatches.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

// Boulders (looseboulders.json, class BlockLooseRock) have no RightClickPickup behavior - they're
// mined with a normal break, so this is the only hook that fires for them. Loose stones/flints
// (BlockLooseStones) are excluded here: they're collected via BlockBehaviorRightClickPickup
// instead (see Behaviors/BlockBehaviorRightClickPickupWormSpawn.cs), which never calls
// OnBlockBroken.
[HarmonyPatch]
[HarmonyPatchCategory("rockwormspawn")]
class BlockLooseRock_OnBlockBroken_SpawnWorm_Patch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var m = AccessTools.Method(typeof(Block), nameof(Block.OnBlockBroken),
            [typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float)]);
        if (m != null) yield return m;
    }

    static void Postfix(Block __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
    {
        if (__instance.GetType() != typeof(BlockLooseRock)) return;
        RockWormSpawnUtil.TrySpawnWorm(world, pos, byPlayer);
    }
}
