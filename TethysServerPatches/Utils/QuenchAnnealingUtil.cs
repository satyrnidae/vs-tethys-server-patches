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

        dsc.Insert(SkipLineBreaks(dsc, insertAt + quenchableLine.Length), annealableLine + Environment.NewLine);
    }

    // Anchored immediately before vanilla's "Times tempered" line - the same structural slot
    // vanilla uses for the tempered/quenched counts, right after Quenchable./Temperable./Clay
    // covered. Falls back, in order: right after "Clay covered." if the item hasn't been
    // tempered; otherwise right after whichever of Annealable./Temperable./Quenchable. is
    // present (call this after InsertAnnealableLine so the Annealable. line, if any, is already
    // there to anchor on). Same dedup guard as InsertAnnealableLine.
    public static void InsertTimesAnnealedLine(StringBuilder dsc, ItemStack itemstack, CollectibleBehaviorQuenchable.MetalPropertyVariant metalProps)
    {
        var annealIteration = itemstack.Attributes.GetInt("annealIteration");
        if (annealIteration <= 0) return;

        var timesAnnealedLine = Lang.Get("tethysserverpatches:quenchable-annealed-amount", annealIteration);
        var dscText = dsc.ToString();
        if (dscText.Contains(timesAnnealedLine)) return;

        var quenchIteration = itemstack.Attributes.GetInt("quenchIteration");
        var temperIteration = itemstack.Attributes.GetInt("temperIteration");
        int insertAt;

        if (temperIteration > 0
            && (insertAt = dscText.IndexOf(Lang.Get("quenchable-tempered-amount", temperIteration), StringComparison.Ordinal)) >= 0)
        {
            dsc.Insert(insertAt, timesAnnealedLine + Environment.NewLine);
            return;
        }

        if (itemstack.Attributes.GetBool("clayCovered")
            && (insertAt = dscText.IndexOf(Lang.Get("itemstack-claycovered"), StringComparison.Ordinal)) >= 0)
        {
            dsc.Insert(SkipLineBreaks(dsc, insertAt + Lang.Get("itemstack-claycovered").Length), timesAnnealedLine + Environment.NewLine);
            return;
        }

        if (metalProps != null)
        {
            var headerLine = IsAnnealable(itemstack) ? AnnealableLine(metalProps)
                : quenchIteration > temperIteration ? Lang.Get("itemstack-temperable", metalProps.temperMinTemp, metalProps.temperMaxTemp)
                : Lang.Get("itemstack-quenchable", metalProps.quenchMinTemp, metalProps.quenchMaxTemp);
            insertAt = dscText.IndexOf(headerLine, StringComparison.Ordinal);
            if (insertAt >= 0)
            {
                dsc.Insert(SkipLineBreaks(dsc, insertAt + headerLine.Length), timesAnnealedLine + Environment.NewLine);
                return;
            }
        }

        dsc.Insert(0, timesAnnealedLine + Environment.NewLine);
    }

    private static int SkipLineBreaks(StringBuilder dsc, int index)
    {
        while (index < dsc.Length && (dsc[index] == '\r' || dsc[index] == '\n'))
        {
            index++;
        }
        return index;
    }
}
