using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

// Dead's corpse renderer (Dead.Rendering.EntityDeadShapeRenderer) subclasses the base vanilla
// EntityShapeRenderer, not PlayerModelLib's CustomPlayerShapeRenderer (which subclasses
// EntityPlayerShapeRenderer and is how PlayerModelLib swaps a live player's base body to match
// their selected custom model, e.g. FemaleSeraph). That means a corpse's base body is always
// the vanilla shape declared on dead:entities/playercorpse.json, regardless of which custom
// model the dying player had selected — even though Dead already copies the "skinModel"
// WatchedAttribute onto the corpse (DeadModSystem.CopySkin) and worn-gear shapes DO get
// correctly swapped (a separate, behavior-level PlayerModelLib hook that isn't tied to the
// renderer class). The mismatched base body breaks any custom-model-specific geometry (breast
// bumps, custom heads/arms/legs, etc.) that worn gear step-parents onto.
//
// EntityShapeRenderer exposes OverrideEntityShape/OverrideCompositeShape specifically for this
// kind of substitution. We set them from PlayerModelLib's already-loaded CustomModelData for
// whatever model code the corpse carries, so this covers any custom player model mod built on
// PlayerModelLib, not just FemaleSeraph.
[HarmonyPatch]
[HarmonyPatchCategory("dead-playermodellib")]
class DeadPlayerModelCompat
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var rendererType = AccessTools.TypeByName("Dead.Rendering.EntityDeadShapeRenderer");
        if (rendererType == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[dead-playermodellib] Could not find Dead.Rendering.EntityDeadShapeRenderer — corpse model compat will not apply");
            yield break;
        }

        var ctor = AccessTools.Constructor(rendererType, new[] { typeof(Entity), typeof(ICoreClientAPI) });
        if (ctor == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[dead-playermodellib] Could not find EntityDeadShapeRenderer(Entity, ICoreClientAPI) constructor");
            yield break;
        }
        yield return ctor;
    }

    static void Postfix(EntityShapeRenderer __instance, Entity entity, ICoreClientAPI api)
    {
        try
        {
            ApplyModelOverride(__instance, entity, api);
        }
        catch (Exception ex)
        {
            TethysServerPatchesCore.Logger.Warning(
                $"[dead-playermodellib] Failed to apply custom model override to corpse {entity.EntityId}: {ex}");
        }
    }

    static void ApplyModelOverride(EntityShapeRenderer renderer, Entity entity, ICoreClientAPI api)
    {
        var skinModel = entity.WatchedAttributes.GetString("skinModel");
        if (string.IsNullOrEmpty(skinModel))
            return;

        var modelSystem = api.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");
        if (modelSystem == null)
            return;

        var modelSystemType = modelSystem.GetType();

        var modelsLoadedProp = AccessTools.Property(modelSystemType, "ModelsLoaded");
        if (modelsLoadedProp?.GetValue(modelSystem) is not bool modelsLoaded || !modelsLoaded)
            return;

        var customModels = AccessTools.Property(modelSystemType, "CustomModels")?.GetValue(modelSystem) as IDictionary;
        if (customModels == null || !customModels.Contains(skinModel))
            return;

        var customModelData = customModels[skinModel];
        var customModelDataType = customModelData.GetType();
        var shape = AccessTools.Property(customModelDataType, "Shape")?.GetValue(customModelData) as Shape;
        var shapePath = AccessTools.Property(customModelDataType, "ShapePath")?.GetValue(customModelData) as string;
        if (shape == null || string.IsNullOrEmpty(shapePath))
            return;

        renderer.OverrideEntityShape = shape;

        var compositeShape = entity.Properties.Client.Shape?.Clone() ?? new CompositeShape();
        compositeShape.Base = new AssetLocation(shapePath);
        renderer.OverrideCompositeShape = compositeShape;

        entity.MarkShapeModified();
    }
}
