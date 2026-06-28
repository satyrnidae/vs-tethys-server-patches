using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace TethysServerPatches.Patches;

// WoodSawing.OnHeldInteractStart unconditionally sets handHandling=PreventDefault even
// when the targeted block isn't sawable (e.g. the Toolsmith workbench). This causes the
// VS engine to consider the item interaction "taken", so TryBeginUseBlock never fires and
// BlockWorkbench.OnBlockInteractStart (which drives sneak+vice disassembly) is never called.
//
// Fix: postfix after WoodSawing.OnHeldInteractStart — if the behavior-chain handling is still
// PassThrough (WoodSawing didn't actually engage), reset handHandling to NotHandled so the
// engine falls through to block interaction normally.
[HarmonyPatch]
[HarmonyPatchCategory("immersivesawingcompat")]
class WoodSawing_OnHeldInteractStart
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ImmersiveWoodSawing, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase method;
        if (asm == null
            || (type = asm.GetType("ImmersiveWoodSawing.WoodSawing", throwOnError: false)) == null
            || (method = AccessTools.Method(type, "OnHeldInteractStart")) == null)
        {
            TethysServerPatchesCore.Logger.Error("[tethysserverpatches] Failed to find ImmersiveWoodSawing.WoodSawing.OnHeldInteractStart to patch");
            throw new Exception("[tethysserverpatches] Could not find WoodSawing.OnHeldInteractStart");
        }
        return [method];
    }

    static void Postfix(ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        if (handling == EnumHandling.PassThrough)
        {
            handHandling = EnumHandHandling.NotHandled;
        }
    }
}
