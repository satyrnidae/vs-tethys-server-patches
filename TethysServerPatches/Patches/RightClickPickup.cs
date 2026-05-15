using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TethysServerPatches.Patches;

// Block.CanPlaceBlock rejects placement when IsInteractable entities occupy the collision box.
// EntityItem.IsInteractable is patched to true (so items are ray-trace selectable), so we
// correct the placement check: re-allow if item entities are the only obstruction.
[HarmonyPatch]
[HarmonyPatchCategory("rightclickpickup")]
class Block_CanPlaceBlock_AllowThroughEntityItem_Patch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var m = AccessTools.Method(typeof(Block), "CanPlaceBlock",
            [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection), typeof(string).MakeByRefType()]);
        if (m != null) yield return m;
    }

    static void Postfix(Block __instance, IWorldAccessor world, BlockSelection blockSel, ref string failureCode, ref bool __result)
    {
        if (__result) return;
        if (failureCode != "entityintersecting") return;
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.RightClickPickup.Enabled != true) return;

        // Re-run the check excluding EntityItem: if nothing else blocks, allow placement.
        var boxes = __instance.GetCollisionBoxes(world.BlockAccessor, blockSel.Position);
        if (boxes == null || boxes.Length == 0) return;
        if (world.GetIntersectingEntities(blockSel.Position, boxes, e => e.IsInteractable && e is not EntityItem).Length == 0)
        {
            __result = true;
            failureCode = null;
        }
    }
}

[HarmonyPatch(typeof(EntityItem), "IsInteractable", MethodType.Getter)]
[HarmonyPatchCategory("rightclickpickup")]
class EntityItem_IsInteractable_Patch
{
    static bool Prefix(ref bool __result)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.RightClickPickup.Enabled != true)
            return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("rightclickpickup")]
class EntityItem_OnInteract_Patch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var m = AccessTools.Method(typeof(Entity), nameof(Entity.OnInteract),
            [typeof(EntityAgent), typeof(ItemSlot), typeof(Vec3d), typeof(EnumInteractMode)]);
        if (m != null) yield return m;
    }

    static bool Prefix(Entity __instance, EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (__instance is not EntityItem ei) return true;
        var opts = TethysServerPatchesCore.Configuration?.VanillaTweaks.RightClickPickup;
        if (opts?.Enabled != true) return true;
        if (mode != EnumInteractMode.Interact) return true;
        if (byEntity.Api.Side != EnumAppSide.Server) return false;

        // RequireEmptyHand: block if holding a different item (empty hand and matching stacks are fine).
        // !RequireEmptyHand: allow pickup regardless of what's in hand.
        if (opts.RequireEmptyHand && itemslot?.Itemstack != null
            && !itemslot.Itemstack.Equals(byEntity.World, ei.Itemstack, GlobalConstants.IgnoredStackAttributes))
        {
            if (byEntity is EntityPlayer ep && ep.Player is IServerPlayer sp)
                sp.SendIngameError("tethysserverpatches-wrongitem", null, ei.Itemstack?.GetName() ?? "");
            return false;
        }

        if (!ei.CanCollect(byEntity)) return false;
        var stack = ei.OnCollected(byEntity);
        if (stack == null) return false;

        if (byEntity.TryGiveItemStack(stack))
        {
            byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/collect"), ei, null, false, 32);
            if (stack.StackSize <= 0)
                ei.Die(EnumDespawnReason.Removed);
        }
        else if (byEntity is EntityPlayer ep2 && ep2.Player is IServerPlayer sp2)
        {
            sp2.SendIngameError("tethysserverpatches-invfull", null, ei.Itemstack?.GetName() ?? "");
        }
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("rightclickpickup")]
class EntityItem_GetName_Patch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var m = AccessTools.Method(typeof(Entity), "GetName");
        if (m != null) yield return m;
    }

    static bool Prefix(Entity __instance, ref string __result)
    {
        if (__instance is not EntityItem ei) return true;
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.RightClickPickup.Enabled != true) return true;
        var stack = ei.Itemstack;
        if (stack?.Collectible == null) return true;
        try
        {
            __result = stack.GetName();
            return false;
        }
        catch
        {
            return true;
        }
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("rightclickpickup")]
class EntityItem_GetInteractionHelp_Patch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var m = AccessTools.Method(typeof(Entity), "GetInteractionHelp",
            [typeof(IClientWorldAccessor), typeof(EntitySelection), typeof(IClientPlayer)]);
        if (m != null) yield return m;
    }

    static void Postfix(Entity __instance, ref WorldInteraction[] __result)
    {
        if (__instance is not EntityItem ei) return;
        var opts = TethysServerPatchesCore.Configuration?.VanillaTweaks.RightClickPickup;
        if (opts?.Enabled != true) return;
        // Dedup guard: don't append twice if postfix runs more than once per compose cycle.
        if (__result != null && Array.Exists(__result, wi => wi.ActionLangCode == "tethysserverpatches:rightclickpickup-action")) return;

        var entry = new WorldInteraction
        {
            MouseButton     = EnumMouseButton.Right,
            ActionLangCode  = "tethysserverpatches:rightclickpickup-action",
            RequireFreeHand = false,
            // RequireEmptyHand=false: any item allowed; don't show an item icon, just the glyph.
            // RequireEmptyHand=true:  show the dropped item's icon so the player knows what they're picking up.
            Itemstacks      = opts.RequireEmptyHand ? [ei.Itemstack?.Clone()] : null
        };
        __result = __result is { Length: > 0 } r ? [..r, entry] : [entry];
    }
}
