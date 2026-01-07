using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches
{
    [HarmonyPatchCategory("survival")]
    [HarmonyPatch(typeof(BlockEntityCookedContainer))]
    [HarmonyPatch(nameof(BlockEntityCookedContainer.ServeInto))]
    class ChefServeInto
    {
        [HarmonyPostfix]
        static void ServeInto(IPlayer player, ItemSlot slot, bool __result)
        {
            TethysServerPatchesCore.Logger.Notification($"Player culinary stat: {player.Entity.Stats.GetBlended("servingNutritionMul")}, {player.Entity.Stats.GetBlended("servingHealthMul")}");

            // Need to also patch NutritionHealthMul in BlockMeal to respect new attribute multiplier
            if (!__result) return; // Nothing to do...
        }
    }
}
