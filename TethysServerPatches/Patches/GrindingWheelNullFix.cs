using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace TethysServerPatches.Patches;

// Suppresses a NullReferenceException in BlockEntityGrindingWheel.get_AngleRad
// that hard-crashes the client render loop.
//
// Root cause: the getter calls GetBehavior<BEBehaviorMPConsumer>().AngleRad with no
// null guard. When a grinding wheel's block entity loses its BEBehaviorMPConsumer
// (e.g. a mod alters the block definition mid-save), the getter throws, which
// propagates through MechBlockRenderer.UpdateCustomFloatBuffer up to the main
// render loop and kills the client.
//
// Fix: prefix returns AngleRad=0 when the behavior is absent, so the wheel
// renders as stationary rather than crashing.
[HarmonyPatch]
[HarmonyPatchCategory("grindingwheelnullfix")]
class BlockEntityGrindingWheel_AngleRad_NullFix
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = typeof(BlockEntityGrindingWheel);
        var getter = AccessTools.PropertyGetter(type, "AngleRad");
        if (getter == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[grindingwheelnullfix] Could not find BlockEntityGrindingWheel.AngleRad getter — patch not applied");
            yield break;
        }
        yield return getter;
    }

    static bool Prefix(BlockEntityGrindingWheel __instance, ref float __result)
    {
        if (((Vintagestory.API.Common.BlockEntity)(object)__instance).GetBehavior<BEBehaviorMPConsumer>() == null)
        {
            __result = 0f;
            return false;
        }
        return true;
    }
}
