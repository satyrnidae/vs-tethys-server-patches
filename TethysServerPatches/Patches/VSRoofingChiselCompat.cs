using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;

namespace TethysServerPatches.Patches;

// Bypasses hammer-in-offhand checks for chisels that have needshammer:false.
//
// Three methods need patching:
//  1. CollectibleBehaviorRoofChisel.OnHeldAttackStart  — the VS Roofing behavior's own check
//  2. CollectibleBehaviorRoofChisel.OnHeldInteractStart — same
//  3. ItemChisel.OnHeldAttackStart — the vanilla chisel base class checks hammer BEFORE
//     calling behaviors, so TrueChisel's attack path never reaches the behavior unless we
//     patch here too. (ItemChisel.OnHeldInteractStart calls base first, so #2 is sufficient
//     for interact.)
//
// All three contain the same ldc.i4.5/ceq/ldc.i4.0/ceq/brfalse.s pattern at the hammer
// check, and arg.1 is the active ItemSlot in every case, so one transpiler covers all.
[HarmonyPatch]
[HarmonyPatchCategory("vsroofing")]
class VSRoofingChiselCompat
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var roofChiselType = AccessTools.TypeByName("VSRoofing.CollectibleBehaviorRoofChisel");
        if (roofChiselType == null)
            TethysServerPatchesCore.Logger.Error(
                "[vsroofing] Could not find VSRoofing.CollectibleBehaviorRoofChisel — behavior patches will not apply");
        else
        {
            var attack = AccessTools.Method(roofChiselType, "OnHeldAttackStart");
            if (attack == null)
                TethysServerPatchesCore.Logger.Error("[vsroofing] Could not find CollectibleBehaviorRoofChisel.OnHeldAttackStart");
            else
                yield return attack;

            var interact = AccessTools.Method(roofChiselType, "OnHeldInteractStart");
            if (interact == null)
                TethysServerPatchesCore.Logger.Error("[vsroofing] Could not find CollectibleBehaviorRoofChisel.OnHeldInteractStart");
            else
                yield return interact;
        }

        // ItemChisel.OnHeldAttackStart has its own hammer check that fires before behaviors.
        // RoofBlock is not a BlockMicroBlock, so IsChiselingAllowedFor returns false for it,
        // which causes base.OnHeldAttackStart (behaviors) to be called — but only after the
        // hammer check passes. We need to bypass that check too.
        var itemChiselType = AccessTools.TypeByName("Vintagestory.GameContent.ItemChisel");
        if (itemChiselType == null)
            TethysServerPatchesCore.Logger.Error("[vsroofing] Could not find Vintagestory.GameContent.ItemChisel");
        else
        {
            var chiselAttack = AccessTools.Method(itemChiselType, "OnHeldAttackStart");
            if (chiselAttack == null)
                TethysServerPatchesCore.Logger.Error("[vsroofing] Could not find ItemChisel.OnHeldAttackStart");
            else
                yield return chiselAttack;
        }
    }

    // Returns true if the held item requires a hammer (normal case).
    // Returns false if it has needshammer:false (qp chisel special ability).
    public static bool ItemNeedsHammer(ItemSlot slot)
        => slot?.Itemstack?.Collectible?.Attributes?["needshammer"].AsBool(true) != false;

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var matcher = new CodeMatcher(instructions, generator);

        // All three target methods compile their hammer check to the same IL sequence:
        //   ldc.i4.5   (EnumTool.Hammer = 5)
        //   ceq         → 1 if offhand tool IS hammer, 0 if not
        //   ldc.i4.0
        //   ceq         → negate: 1 = no hammer, 0 = has hammer
        //   brfalse.s [proceed]  → jump to proceed if 0 (has hammer)
        //
        // We insert immediately after the brfalse.s:
        //   ldarg.1     (ItemSlot slot — arg 1 in all three methods)
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
