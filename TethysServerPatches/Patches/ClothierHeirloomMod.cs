using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        var asm = Assembly.Load("ClothierHeirloomsmod, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase getMethod;
        if (asm == null
            || (type = asm.GetType("ClothierHeirloomsmod.NSBlockEntity.BlockEntityAutoloom", throwOnError: false)) == null
            || (getMethod = type.GetProperty("InputGrindProps")?.GetGetMethod()) == null)
        {
            TethysServerPatchesCore.Logger.Error("Failed to patch method even though mod is loaded! Did the name change?");
            throw new Exception();
        }
        return [getMethod];
    }

    // Add support for wool and whatever
    static bool Prefix(BlockEntity __instance, InventoryQuern ___inventory, ref int ___inputnum, ref ItemStack __result)
    {
        if (!TethysServerPatchesCore.Configuration.ClothiersHeirloomsPatches.Enabled)
        {
            return true;
        }

        ItemSlot val = ___inventory[0];
        JsonObject weavingProps = val.Itemstack?.Collectible?.Attributes?["clothierheirloomsmod:weavingProps"];
        if (weavingProps != null && weavingProps.Exists)
        {
            int inputnum = weavingProps["input"].AsInt();
            if (val.Itemstack.StackSize >= inputnum)
            {
                var jsonItemStack = weavingProps["output"].AsObject<JsonItemStack>(null, val.Itemstack.Collectible.Code.Domain);
                if (jsonItemStack.Resolve(__instance.Api.World, TethysServerPatchesCore.ModId))
                {
                    __result = jsonItemStack.ResolvedItemstack;
                    ___inputnum = inputnum; // bit of a hack job tbh
                }
            }
        }
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("clothierheirloomsmod")]
class BlockEntitySpinner_get_InputGrindProps
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ClothierHeirloomsmod, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
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

    // Add support for wool and whatever
    static bool Prefix(BlockEntity __instance, InventoryQuern ___inventory, ref ItemStack __result)
    {
        if (!TethysServerPatchesCore.Configuration.ClothiersHeirloomsPatches.Enabled)
        {
            return true;
        }

        ItemSlot val = ___inventory[0];
        JsonObject spinnerProps = val.Itemstack?.Collectible?.Attributes?["clothierheirloomsmod:spinningProps"];
        if (spinnerProps != null && spinnerProps.Exists)
        {
            int inputnum = spinnerProps["input"].AsInt();
            if (val.Itemstack.StackSize >= inputnum)
            {
                var jsonItemStack = spinnerProps["output"].AsObject<JsonItemStack>(null, val.Itemstack.Collectible.Code.Domain);
                if (jsonItemStack.Resolve(__instance.Api.World, TethysServerPatchesCore.ModId))
                {
                    __result = jsonItemStack.ResolvedItemstack;
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
        var asm = Assembly.Load("ClothierHeirloomsmod, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
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
        {
            return true;
        }

        ItemSlot val = ___inventory[0];
        JsonObject spinnerProps = val.Itemstack?.Collectible?.Attributes?["clothierheirloomsmod:spinningProps"];
        if (spinnerProps != null && spinnerProps.Exists)
        {
            __result = spinnerProps["input"].AsInt();
            return false;
        }
        return true;
    }
}
