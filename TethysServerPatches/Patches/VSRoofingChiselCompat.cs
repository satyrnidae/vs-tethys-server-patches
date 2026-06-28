using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;

namespace TethysServerPatches.Patches;

// VSRoofing's CollectibleBehaviorRoofChisel.OnHeldAttackStart and OnHeldInteractStart
// hard-code a hammer-in-offhand requirement. The ChiselTools "qp chisel" (truechisel-*)
// has needshammer:false on its variants, meaning it should work without a hammer in hand.
// This transpiler inserts a needshammer attribute check immediately after the vanilla
// hammer check so that qp chisels bypass the hammer requirement for roof fill blocks.
[HarmonyPatch]
[HarmonyPatchCategory("vsroofing")]
class VSRoofingChiselCompat
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("VSRoofing.CollectibleBehaviorRoofChisel");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[vsroofing] Could not find VSRoofing.CollectibleBehaviorRoofChisel — chisel compat patch will not apply");
            yield break;
        }

        var attack = AccessTools.Method(type, "OnHeldAttackStart");
        if (attack == null)
            TethysServerPatchesCore.Logger.Error("[vsroofing] Could not find OnHeldAttackStart");
        else
            yield return attack;

        var interact = AccessTools.Method(type, "OnHeldInteractStart");
        if (interact == null)
            TethysServerPatchesCore.Logger.Error("[vsroofing] Could not find OnHeldInteractStart");
        else
            yield return interact;
    }

    // Returns true if the held item requires a hammer (normal case).
    // Returns false if it has needshammer:false (qp chisel special ability).
    public static bool ItemNeedsHammer(ItemSlot slot)
        => slot?.Itemstack?.Collectible?.Attributes?["needshammer"].AsBool(true) != false;

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var matcher = new CodeMatcher(instructions, generator);

        // The hammer check in both methods compiles to this IL sequence:
        //   ldc.i4.5   (EnumTool.Hammer = 5)
        //   ceq         → 1 if offhand tool IS hammer, 0 if not
        //   ldc.i4.0
        //   ceq         → negate: 1 = no hammer, 0 = has hammer
        //   brfalse.s [proceed]  → jump to proceed if 0 (has hammer)
        //
        // We insert immediately after the brfalse.s:
        //   ldarg.1     (ItemSlot slot — arg 1 in both methods)
        //   call ItemNeedsHammer
        //   brfalse.s [proceed]  → also jump to proceed if needshammer:false
        matcher.MatchStartForward(
            new CodeMatch(OpCodes.Ldc_I4_5),
            new CodeMatch(OpCodes.Ceq),
            new CodeMatch(OpCodes.Ldc_I4_0),
            new CodeMatch(OpCodes.Ceq),
            new CodeMatch(i => i.opcode == OpCodes.Brfalse_S || i.opcode == OpCodes.Brfalse)
        );

        if (matcher.IsInvalid)
        {
            TethysServerPatchesCore.Logger.Error(
                "[vsroofing] Could not find hammer check pattern in roof chisel method — patch not applied");
            return instructions;
        }

        // Advance from ldc.i4.5 to the brfalse.s (4 steps)
        matcher.Advance(4);
        var proceedLabel = (Label)matcher.Instruction.operand;

        // Insert our check after the brfalse.s
        matcher.Advance(1).Insert(
            new CodeInstruction(OpCodes.Ldarg_1),
            CodeInstruction.Call(typeof(VSRoofingChiselCompat), nameof(ItemNeedsHammer)),
            new CodeInstruction(OpCodes.Brfalse_S, proceedLabel)
        );

        return matcher.InstructionEnumeration();
    }
}
