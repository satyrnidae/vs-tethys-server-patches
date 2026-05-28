using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace TethysServerPatches.Patches;

// Suppresses a NullReferenceException in CollectibleBehaviorEntityDeconstructTool.OnHeldInteractStop
// that occurs when Immersive Wood Sawing is installed and a non-deconstructible entity (e.g. a
// dropped item) is in the player's entity selection at the moment a 6-second sawing interaction
// completes.
//
// Root cause: CollectibleBehaviorEntityDeconstructTool.OnHeldInteractStop is dispatched to every
// CollectibleBehavior on the held item, not only the one that initiated the interaction.  When
// Immersive Sawing drives a 6-second block-sawing interaction, secondsUsed reaches ≥ 6f and the
// entitySel null-guard passes if a dropped EntityItem happens to be in the player's crosshair.
// The method then unconditionally calls entitySel.Entity.Die() and reads
// entitySel.Entity.Properties.Attributes["deconstructDrops"], both of which throw because
// EntityItem has no "deconstructDrops" attributes (and Properties.Attributes may itself be null).
//
// Fix: Prefix that mirrors the deconstructibility check from OnHeldInteractStart — if the targeted
// entity is not explicitly marked deconstructible the prefix stops the "saw" animation and skips
// the original, preventing the Die() call and the attribute lookup.
[HarmonyPatch]
[HarmonyPatchCategory("immersivewoodsawing")]
class CollectibleBehaviorEntityDeconstructTool_OnHeldInteractStop_NullFix
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("Vintagestory.GameContent.CollectibleBehaviorEntityDeconstructTool");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[immersivewoodsawing] Could not find CollectibleBehaviorEntityDeconstructTool " +
                "— null-deconstruct patch will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "OnHeldInteractStop");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[immersivewoodsawing] Could not find CollectibleBehaviorEntityDeconstructTool." +
                "OnHeldInteractStop — null-deconstruct patch will not apply");
            yield break;
        }

        yield return method;
    }

    static bool Prefix(float secondsUsed, EntityAgent byEntity, EntitySelection entitySel)
    {
        // Let the original handle the early-exit cases it already guards (< 6s, null entitySel).
        if (secondsUsed < 6f || entitySel == null)
            return true;

        // The original assumes entitySel.Entity is a deconstructible entity, but
        // OnHeldInteractStop is called for every behavior regardless of which one started the
        // interaction.  Guard with the same check OnHeldInteractStart uses so the deconstruction
        // path is only reached for actually deconstructible entities.
        if (entitySel.Entity?.Properties?.Attributes?.IsTrue("deconstructible") != true)
        {
            byEntity.StopAnimation("saw");
            return false;
        }

        return true;
    }
}
