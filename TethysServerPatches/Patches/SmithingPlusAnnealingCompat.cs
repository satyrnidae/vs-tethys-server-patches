using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

static class SmithingPlusAnnealingCompatUtil
{
    public static readonly Regex TemperatureRangeRegex = new(@"(\s*\(\d+°C\s*-\s*\d+°C\))", RegexOptions.Compiled);

    private static string SetColor(string value, string color) => $"<font color=\"{color}\">{value}</font>";

    private static bool InRange(float temperature, int min, int max) => temperature > min && temperature < max;

    public static void ColorAnnealableRange(StringBuilder dsc, string text, float currentTemp, CollectibleBehaviorQuenchable.MetalPropertyVariant metalProps)
    {
        if (!InRange(currentTemp, metalProps.temperMinTemp, metalProps.temperMaxTemp)) return;
        dsc.Replace(text, TemperatureRangeRegex.Replace(text, m => SetColor(m.Value, "darkcyan")));
    }
}

// Smithing Plus recolors the temperature range on the vanilla "Quenchable."/"Temperable." lines to
// darkcyan whenever the item is currently sitting in that range (held-item tooltip). Give the new
// "Annealable." line (added in QuenchAnnealing.cs) the same treatment so it reads as part of
// Smithing Plus's presentation instead of looking bolted on. Only active when smithingplus is
// installed — see TethysServerPatchesCore.
[HarmonyPatch]
[HarmonyPatchCategory("quenchannealing-smithingplus")]
class CollectibleBehaviorQuenchable_GetHeldItemInfo_ColorAnnealable
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(CollectibleBehaviorQuenchable), "GetHeldItemInfo",
            [typeof(ItemSlot), typeof(StringBuilder), typeof(IWorldAccessor), typeof(bool)]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[quenchannealing-smithingplus] Could not find CollectibleBehaviorQuenchable.GetHeldItemInfo — annealable coloring not applied");
            yield break;
        }
        yield return method;
    }

    // Runs after CollectibleBehaviorQuenchable_GetHeldItemInfo_Annealable's postfix appends the
    // plain line, so there's text to recolor by the time this fires.
    [HarmonyPriority(Priority.Low)]
    static void Postfix(CollectibleBehaviorQuenchable __instance, ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.Annealing != true) return;
        var itemstack = inSlot.Itemstack;
        if (itemstack == null || !QuenchAnnealingUtil.IsAnnealable(itemstack)) return;

        var metalProps = QuenchAnnealingUtil.GetMetalProps(__instance);
        if (metalProps == null) return;

        var currentTemp = itemstack.Collectible.GetTemperature(world, itemstack);
        var text = QuenchAnnealingUtil.AnnealableLine(metalProps);
        SmithingPlusAnnealingCompatUtil.ColorAnnealableRange(dsc, text, currentTemp, metalProps);
    }
}

// Vanilla BlockEntityForge.GetBlockInfo never shows the quenchable/temperable/annealable lines at
// all — Smithing Plus is what makes the forge tooltip surface them. Mirror that here for the new
// annealable line, again only while smithingplus is installed.
[HarmonyPatch]
[HarmonyPatchCategory("quenchannealing-smithingplus")]
class BlockEntityForge_GetBlockInfo_Annealable
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(BlockEntityForge), "GetBlockInfo",
            [typeof(IPlayer), typeof(StringBuilder)]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[quenchannealing-smithingplus] Could not find BlockEntityForge.GetBlockInfo — annealable forge tooltip not applied");
            yield break;
        }
        yield return method;
    }

    static void Postfix(BlockEntityForge __instance, StringBuilder dsc)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.Annealing != true) return;
        var workItemStack = __instance.WorkItemStack;
        if (workItemStack == null || !QuenchAnnealingUtil.IsAnnealable(workItemStack)) return;

        var behavior = workItemStack.Collectible.GetBehavior<CollectibleBehaviorQuenchable>();
        var metalProps = behavior == null ? null : QuenchAnnealingUtil.GetMetalProps(behavior);
        if (metalProps == null) return;

        var currentTemp = workItemStack.Collectible.GetTemperature(__instance.Api.World, workItemStack);
        var text = QuenchAnnealingUtil.AnnealableLine(metalProps);
        if (dsc.ToString().Contains(text)) return;
        dsc.AppendLine(text);
        SmithingPlusAnnealingCompatUtil.ColorAnnealableRange(dsc, text, currentTemp, metalProps);
    }
}
