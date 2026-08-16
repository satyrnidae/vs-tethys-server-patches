using System;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace TethysServerPatches.Utils;

static class QuenchAnnealingUtil
{
    public static bool IsAnnealable(ItemStack itemstack)
    {
        var quenchIteration = itemstack.Attributes.GetInt("quenchIteration");
        var temperIteration = itemstack.Attributes.GetInt("temperIteration");
        return quenchIteration > 0 && temperIteration >= quenchIteration;
    }

    public static CollectibleBehaviorQuenchable.MetalPropertyVariant GetMetalProps(CollectibleBehaviorQuenchable behavior)
    {
        return AccessTools.Field(typeof(CollectibleBehaviorQuenchable), "metalProps")
            .GetValue(behavior) as CollectibleBehaviorQuenchable.MetalPropertyVariant;
    }

    public static string AnnealableLine(CollectibleBehaviorQuenchable.MetalPropertyVariant metalProps)
    {
        return Lang.Get("tethysserverpatches:itemstack-annealable", metalProps.temperMinTemp, metalProps.temperMaxTemp);
    }

    // Inserts directly after the "Quenchable." line (mirroring where vanilla puts "Temperable.")
    // instead of at the end, and guards against double-insertion: GetHeldItemInfo-style compose
    // methods can run more than once per tooltip build (see the dedup guard in RightClickPickup.cs).
    public static void InsertAnnealableLine(StringBuilder dsc, CollectibleBehaviorQuenchable.MetalPropertyVariant metalProps)
    {
        var annealableLine = AnnealableLine(metalProps);
        var dscText = dsc.ToString();
        if (dscText.Contains(annealableLine)) return;

        var quenchableLine = Lang.Get("itemstack-quenchable", metalProps.quenchMinTemp, metalProps.quenchMaxTemp);
        var insertAt = dscText.IndexOf(quenchableLine, StringComparison.Ordinal);
        if (insertAt < 0)
        {
            dsc.AppendLine(annealableLine);
            return;
        }

        insertAt += quenchableLine.Length;
        while (insertAt < dsc.Length && (dsc[insertAt] == '\r' || dsc[insertAt] == '\n'))
        {
            insertAt++;
        }
        dsc.Insert(insertAt, annealableLine + Environment.NewLine);
    }

    // Inserted right before the shatter-chance line, i.e. directly under "Times tempered"/"Times
    // quenched" (and their power/duration gain lines), same dedup guard as InsertAnnealableLine.
    public static void InsertTimesAnnealedLine(StringBuilder dsc, CollectibleBehaviorQuenchable behavior, IWorldAccessor world, ItemStack itemstack)
    {
        var annealIteration = itemstack.Attributes.GetInt("annealIteration");
        if (annealIteration <= 0) return;

        var timesAnnealedLine = Lang.Get("tethysserverpatches:quenchable-annealed-amount", annealIteration);
        var dscText = dsc.ToString();
        if (dscText.Contains(timesAnnealedLine)) return;

        var shatterChanceLine = Lang.Get("quenchable-shatter-chance", behavior.GetShatterChance(world, itemstack));
        var insertAt = dscText.IndexOf(shatterChanceLine, StringComparison.Ordinal);
        if (insertAt < 0)
        {
            dsc.AppendLine(timesAnnealedLine);
            return;
        }
        dsc.Insert(insertAt, timesAnnealedLine + Environment.NewLine);
    }
}
