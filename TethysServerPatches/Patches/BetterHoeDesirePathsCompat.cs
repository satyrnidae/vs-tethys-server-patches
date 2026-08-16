using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

// Better Hoe (betterhoe) has no awareness of Desire Paths' (desirepaths) soilpath blocks.
// Dry/packed-dirt path mode already works on them by accident, because Better Hoe's terrain
// checks use raw string prefix matching ("soilpath-...".StartsWith("soil") is true) rather than
// exact vanilla-block matching. Two real gaps remain, both patched here against ItemBetterHoe's
// private members:
//  1. Stone path mode is fully blocked on soilpath blocks — CanTransformIntoStonePath and
//     IsFirstStonePathCreation require block.Code.Domain == "game" before doing their path-prefix
//     check, and desirepaths fails that gate outright.
//  2. Till mode silently no-ops on soilpath-forest-* tiles — TillSoil looks up
//     game:farmland-dry-forest, which doesn't exist (vanilla farmland only has
//     verylow/low/medium/compost/high fertility variants). Desire Paths itself treats "forest"
//     fertility as equivalent to "low" (its own dropsByType drops game:soil-low-none for
//     soilpath-forest-*), so that substitution is used here.
static class BetterHoeDesirePathsCompatUtil
{
    public static bool IsDesirePathsSoilPath(Block block)
        => block.Code?.Domain == "desirepaths" && block.Code.Path.StartsWith("soilpath", StringComparison.Ordinal);

    public static T GetConfigValue<T>(object config, string propertyName)
        => (T)AccessTools.Property(config.GetType(), propertyName).GetValue(config);
}

[HarmonyPatch]
[HarmonyPatchCategory("betterhoedesirepathscompat")]
class ItemBetterHoe_CanTransformIntoStonePath_DesirePathsCompat
{
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("BetterHoe.Items.ItemBetterHoe");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[betterhoedesirepathscompat] Could not find BetterHoe.Items.ItemBetterHoe — compat not applied");
            return null;
        }
        var method = AccessTools.Method(type, "CanTransformIntoStonePath", [typeof(Block)]);
        if (method == null)
            TethysServerPatchesCore.Logger.Error(
                "[betterhoedesirepathscompat] Could not find ItemBetterHoe.CanTransformIntoStonePath — stone path compat not applied");
        return method;
    }

    static bool Prefix(Block block, ref bool __result)
    {
        if (!BetterHoeDesirePathsCompatUtil.IsDesirePathsSoilPath(block)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("betterhoedesirepathscompat")]
class ItemBetterHoe_IsFirstStonePathCreation_DesirePathsCompat
{
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("BetterHoe.Items.ItemBetterHoe");
        if (type == null) return null; // already logged by the sibling patch above

        var method = AccessTools.Method(type, "IsFirstStonePathCreation", [typeof(Block)]);
        if (method == null)
            TethysServerPatchesCore.Logger.Error(
                "[betterhoedesirepathscompat] Could not find ItemBetterHoe.IsFirstStonePathCreation — stone path compat not applied");
        return method;
    }

    static bool Prefix(Block block, ref bool __result)
    {
        if (!BetterHoeDesirePathsCompatUtil.IsDesirePathsSoilPath(block)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("betterhoedesirepathscompat")]
class ItemBetterHoe_TillSoil_DesirePathsCompat
{
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("BetterHoe.Items.ItemBetterHoe");
        if (type == null) return null; // already logged by the sibling patch above

        var configType = AccessTools.TypeByName("BetterHoe.Config.BetterHoeConfig");
        if (configType == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[betterhoedesirepathscompat] Could not find BetterHoe.Config.BetterHoeConfig — till compat not applied");
            return null;
        }

        var method = AccessTools.Method(type, "TillSoil",
            [typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), configType]);
        if (method == null)
            TethysServerPatchesCore.Logger.Error(
                "[betterhoedesirepathscompat] Could not find ItemBetterHoe.TillSoil — till compat not applied");
        return method;
    }

    // Only intercepts soilpath-forest-* tiles (the one fertility tier vanilla farmland has no
    // variant for); every other fertility/domain falls through to the original method, which
    // already handles them correctly via the same raw-prefix coincidence as path mode.
    static bool Prefix(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, object config)
    {
        var position = blockSelection.Position;
        var block = byEntity.World.BlockAccessor.GetBlock(position);
        var code = block.Code;
        if (code?.Domain != "desirepaths" || !code.Path.StartsWith("soilpath-forest-", StringComparison.Ordinal))
            return true;

        if (byEntity.World.BlockAccessor.GetBlock(position.UpCopy()).Id != 0 || (byEntity as EntityPlayer)?.Player == null)
            return false;

        var farmland = byEntity.World.GetBlock(new AssetLocation("game", "farmland-dry-low"));
        if (farmland == null) return false;

        TreeAttribute existingFertilityData = null;
        if (byEntity.World.BlockAccessor.GetBlockEntity(position) is BlockEntitySoilNutrition soilNutrition)
        {
            existingFertilityData = new TreeAttribute();
            soilNutrition.ToTreeAttributes(existingFertilityData);
        }

        if (block.Sounds != null)
            byEntity.World.PlaySoundAt(block.Sounds.Place, position, 0.4);

        byEntity.World.BlockAccessor.SetBlock(farmland.BlockId, position);

        var damage = BetterHoeDesirePathsCompatUtil.GetConfigValue<int>(config, "DamageItemStandard")
            + BetterHoeDesirePathsCompatUtil.GetConfigValue<int>(config, "DamageItemExtraPlow");
        if (damage > 0 && slot.Itemstack != null)
        {
            slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, damage);
            if (slot.Empty)
                byEntity.World.PlaySoundAt(new AssetLocation("game", "sounds/effect/toolbreak"), byEntity.Pos.X, byEntity.Pos.InternalY, byEntity.Pos.Z);
        }

        if (byEntity.World.BlockAccessor.GetBlockEntity(position) is BlockEntityFarmland farmlandEntity)
            farmlandEntity.OnCreatedFromSoil(block, existingFertilityData);

        var saturation = BetterHoeDesirePathsCompatUtil.GetConfigValue<float>(config, "ConsumeSaturationStandard")
            + BetterHoeDesirePathsCompatUtil.GetConfigValue<float>(config, "ConsumeSaturationExtraPlow");
        if (saturation > 0f)
            byEntity.GetBehavior<EntityBehaviorHunger>()?.ConsumeSaturation(saturation);

        byEntity.World.BlockAccessor.MarkBlockDirty(position);
        return false;
    }
}
