using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

[HarmonyPatch]
[HarmonyPatchCategory("clothierheirloomsmod")]
class BlockEntityAutoloom_get_InputGrindProps
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ClothierHeirloomsmod, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase getMethod;
        if ((type = asm.GetType("ClothierHeirloomsmod.NSBlockEntity.BlockEntityAutoloom", throwOnError: false)) != null
            && (getMethod = type.GetProperty("InputGrindProps")?.GetGetMethod()) != null) return [getMethod];

        TethysServerPatchesCore.Logger.Error("Failed to patch method even though mod is loaded! Did the name change?");
        throw new Exception();
    }

    static bool Prefix(BlockEntity __instance, InventoryQuern ___inventory, ref int ___inputnum, ref ItemStack __result)
    {
        if (!TethysServerPatchesCore.Configuration.ClothiersHeirloomsPatches.Enabled)
            return true;

        var val = ___inventory[0];
        var weavingProps = val.Itemstack?.Collectible?.Attributes?["weavingProps"];
        if (weavingProps is not { Exists: true }) return false;

        int inputNum = weavingProps["inputQuantity"].AsInt(1);
        if (val.Itemstack.StackSize < inputNum) return false;

        string outputType = weavingProps["outputType"].AsString();
        int outputQuantity = weavingProps["outputQuantity"].AsInt(1);
        if (outputType == null) return false;

        var loc = new AssetLocation(outputType);
        var item = __instance.Api.World.GetItem(loc);
        if (item != null) { __result = new ItemStack(item, outputQuantity); ___inputnum = inputNum; return false; }
        var block = __instance.Api.World.GetBlock(loc);
        if (block != null) { __result = new ItemStack(block, outputQuantity); ___inputnum = inputNum; return false; }
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("clothierheirloomsmod")]
class BlockEntitySpinner_get_InputGrindProps
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ClothierHeirloomsmod, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase getMethod;
        if (asm == null
            || (type = asm.GetType("ClothierHeirloomsmod.NSBlockEntity.BlockEntitySpinner", throwOnError: false)) == null
            || (getMethod = type.GetProperty("InputGrindProps")?.GetGetMethod()) == null)
        {
            TethysServerPatchesCore.Logger.Error("Failed to patch method even though mod is loaded! Did the name change?");
            throw new Exception();
        }
        return [getMethod];
    }

    static bool Prefix(BlockEntity __instance, InventoryQuern ___inventory, ref ItemStack __result)
    {
        if (!TethysServerPatchesCore.Configuration.ClothiersHeirloomsPatches.Enabled)
            return true;

        ItemSlot val = ___inventory[0];
        JsonObject spinnerProps = val.Itemstack?.Collectible?.Attributes?["spinningProps"];
        if (spinnerProps != null && spinnerProps.Exists)
        {
            int inputnum = spinnerProps["inputQuantity"].AsInt(1);
            if (val.Itemstack.StackSize >= inputnum)
            {
                string outputType = spinnerProps["outputType"].AsString();
                int outputQuantity = spinnerProps["outputQuantity"].AsInt(1);
                if (outputType != null)
                {
                    var loc = new AssetLocation(outputType);
                    var item = __instance.Api.World.GetItem(loc);
                    if (item != null) { __result = new ItemStack(item, outputQuantity); return false; }
                    var block = __instance.Api.World.GetBlock(loc);
                    if (block != null) { __result = new ItemStack(block, outputQuantity); return false; }
                }
            }
        }
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("clothierheirloomsmod")]
class BlockEntitySpinner_get_inputnum
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ClothierHeirloomsmod, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase getMethod;
        if (asm == null
            || (type = asm.GetType("ClothierHeirloomsmod.NSBlockEntity.BlockEntitySpinner", throwOnError: false)) == null
            || (getMethod = type.GetProperty("inputnum")?.GetGetMethod()) == null)
        {
            TethysServerPatchesCore.Logger.Error("Failed to patch method even though mod is loaded! Did the name change?");
            throw new Exception();
        }
        return [getMethod];
    }

    static bool Prefix(BlockEntity __instance, InventoryQuern ___inventory, ref int __result)
    {
        if (!TethysServerPatchesCore.Configuration.ClothiersHeirloomsPatches.Enabled)
            return true;

        ItemSlot val = ___inventory[0];
        JsonObject spinnerProps = val.Itemstack?.Collectible?.Attributes?["spinningProps"];
        if (spinnerProps != null && spinnerProps.Exists)
        {
            __result = spinnerProps["inputQuantity"].AsInt(1);
            return false;
        }
        return true;
    }
}
