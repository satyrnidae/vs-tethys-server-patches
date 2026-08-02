using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

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

// Vanilla CollectibleBehaviorQuenchable only ever moves an item toward more quench/temper
// stacks: quench raises power/durability bonus and shatter risk, temper (once per un-tempered
// quench) tones both back down. There is no way to walk a badly-shattery item back once
// tempering has caught up (temperIteration >= quenchIteration). Annealing fills that gap: while
// the item still has quenchIteration > 0 and tempering is unavailable, cooling it through the
// same temper temperature band instead anneals it — halves whatever power/duration bonus the
// item is currently carrying, halves the accumulated shatter chance, and steps quenchIteration
// back by one so another quench is needed before annealing (or tempering) is available again.
[HarmonyPatch]
[HarmonyPatchCategory("quenchannealing")]
class CollectibleBehaviorQuenchable_TrySettleWorkItem_Anneal
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(CollectibleBehaviorQuenchable), "trySettleWorkItem",
            [typeof(IWorldAccessor), typeof(ItemStack), typeof(float), typeof(string)]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[quenchannealing] Could not find CollectibleBehaviorQuenchable.trySettleWorkItem — patch not applied");
            yield break;
        }
        yield return method;
    }

    static void Prefix(CollectibleBehaviorQuenchable __instance, IWorldAccessor world, ItemStack itemstack, float temperature, string currentState)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.Annealing != true) return;
        if (currentState != "temper") return;

        var quenchIteration = itemstack.Attributes.GetInt("quenchIteration");
        var temperIteration = itemstack.Attributes.GetInt("temperIteration");
        if (quenchIteration <= 0 || temperIteration < quenchIteration) return;

        var metalProps = AccessTools.Field(typeof(CollectibleBehaviorQuenchable), "metalProps")
            .GetValue(__instance) as CollectibleBehaviorQuenchable.MetalPropertyVariant;
        if (metalProps == null || temperature > metalProps.settledTemperature) return;

        // Mirrors the >1h-in-temper-range gate trySettleWorkItem itself uses for tempering,
        // so annealing settles on the same cadence tempering would have.
        var lastInTemperRangeHours = itemstack.Attributes.GetDouble("lastintemperrangetotalhours", -999.0);
        if (world.Calendar.ElapsedHours - lastInTemperRangeHours <= 1.0) return;

        AnnealItem(__instance, world, itemstack, quenchIteration);
    }

    static void AnnealItem(CollectibleBehaviorQuenchable behavior, IWorldAccessor world, ItemStack itemstack, int quenchIteration)
    {
        var collObj = itemstack.Collectible;
        var buffable = collObj.GetBehavior<CollectibleBehaviorBuffable>();

        var newQuenchIteration = quenchIteration - 1;
        // Annealing back down to quench 0 means "as if it was never quenched" — clear the
        // bonuses and shatter chance outright instead of leaving a diminishing halved remainder.
        var fullyReset = newQuenchIteration <= 0;

        var durationBonus = behavior.GetDurationBonus(world, itemstack);
        if (durationBonus > 0f)
        {
            var newDurationBonus = fullyReset ? 0f : durationBonus / 2f;
            var remainingFraction = (float)collObj.GetRemainingDurability(itemstack) / collObj.GetMaxDurability(itemstack);
            behavior.SetDurationBonus(world, itemstack, newDurationBonus);
            SetOrRemoveBuff(buffable, itemstack, "maxdurability", newDurationBonus);
            collObj.SetDurability(itemstack, (int)(remainingFraction * collObj.GetMaxDurability(itemstack)));
        }

        var powerValue = behavior.GetPowerValue(world, itemstack);
        if (powerValue > 0f)
        {
            var newPowerValue = fullyReset ? 0f : powerValue / 2f;
            behavior.SetPowerValue(world, itemstack, newPowerValue);
            SetOrRemoveBuff(buffable, itemstack, "attackpower", newPowerValue);
            SetOrRemoveBuff(buffable, itemstack, "miningspeed", newPowerValue);
        }

        // Vanilla's own GetShatterChance falls back to BreakChancePerQuench (5%) whenever the
        // attribute is unset, so that's the effective floor even on a never-quenched item —
        // annealing should never leave the item safer than that baseline.
        var shatterChance = behavior.GetShatterChance(world, itemstack);
        var newShatterChance = fullyReset ? behavior.BreakChancePerQuench : Math.Max(behavior.BreakChancePerQuench, shatterChance / 2f);
        behavior.SetShatterChance(world, itemstack, newShatterChance);

        itemstack.Attributes.SetInt("quenchIteration", newQuenchIteration);

        // Tempering can never be ahead of quenching (a temper needs a not-yet-tempered quench to
        // consume) — e.g. quench 2/temper 1 anneals to quench 1/temper 1, but quench 1/temper 1
        // anneals to quench 0/temper 0.
        var temperIteration = itemstack.Attributes.GetInt("temperIteration");
        if (temperIteration > newQuenchIteration)
        {
            itemstack.Attributes.SetInt("temperIteration", newQuenchIteration);
        }

        itemstack.Attributes.SetInt("annealIteration", itemstack.Attributes.GetInt("annealIteration") + 1);
    }

    static void SetOrRemoveBuff(CollectibleBehaviorBuffable buffable, ItemStack itemstack, string statCode, float newBonus)
    {
        if (buffable == null) return;

        if (newBonus <= 0f)
        {
            var buffs = buffable.GetItemBuffs(itemstack);
            var index = buffs.FindIndex(b => b.Code == "hardened" && b.StatCode == statCode);
            if (index >= 0)
            {
                buffs.RemoveAt(index);
                buffable.StoreItemBuffs(itemstack, buffs);
            }
            return;
        }

        buffable.AddBuff(itemstack, new AppliedCollectibleBuff
        {
            Code = "hardened",
            Multiplier = 1f + newBonus,
            StatCode = statCode
        }, EnumBuffAddType.ReplaceOnDuplicate);
    }
}

// Mirrors vanilla's own "itemstack-temperable" line (shown while quenchIteration > temperIteration)
// with an "itemstack-annealable" line shown under the mirror-image condition, so players get the
// same at-a-glance signal for the new mechanic. Smithing Plus recolors the temperature range on
// this and the vanilla lines when installed — see SmithingPlusAnnealingCompat.
[HarmonyPatch]
[HarmonyPatchCategory("quenchannealing")]
class CollectibleBehaviorQuenchable_GetHeldItemInfo_Annealable
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var method = AccessTools.Method(typeof(CollectibleBehaviorQuenchable), "GetHeldItemInfo",
            [typeof(ItemSlot), typeof(StringBuilder), typeof(IWorldAccessor), typeof(bool)]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[quenchannealing] Could not find CollectibleBehaviorQuenchable.GetHeldItemInfo — annealable tooltip not applied");
            yield break;
        }
        yield return method;
    }

    static void Postfix(CollectibleBehaviorQuenchable __instance, ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.Annealing != true) return;
        var itemstack = inSlot.Itemstack;
        if (itemstack == null) return;

        QuenchAnnealingUtil.InsertTimesAnnealedLine(dsc, __instance, world, itemstack);

        if (!QuenchAnnealingUtil.IsAnnealable(itemstack)) return;

        var metalProps = QuenchAnnealingUtil.GetMetalProps(__instance);
        if (metalProps == null) return;

        QuenchAnnealingUtil.InsertAnnealableLine(dsc, metalProps);
    }
}
