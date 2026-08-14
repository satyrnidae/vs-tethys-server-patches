using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

// Emberland's Sleepers already blocks damage during a short post-logoff grace window
// (EntitySleeper.IsProtected), but once that window lapses the sleeper body is a normal
// damageable/targetable entity. Any hostile creature — vanilla lore mobs (drifters, bowtorn,
// shivers, eidolons, ...) or modded ones — can then find, path to, and kill an offline
// player's body, dropping its contents. These patches make sleeper bodies permanently immune
// to non-player creature attacks: one blocks any damage that still lands, the other stops
// hostile AI from ever targeting a sleeper in the first place.

[HarmonyPatch]
[HarmonyPatchCategory("emberlandssleepers")]
class EntitySleeper_ShouldReceiveDamage_BlockMobDamage
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.Enabled == false)
            yield break;
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.DisableMobAttacks != true)
            yield break;

        var type = AccessTools.TypeByName("EmberlandsSleepers.EntitySleeper");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find EntitySleeper — mob attack block will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "ShouldReceiveDamage");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find EntitySleeper.ShouldReceiveDamage — mob attack block will not apply");
            yield break;
        }

        yield return method;
    }

    static void Postfix(DamageSource damageSource, ref bool __result)
    {
        if (__result && damageSource?.Source == EnumDamageSource.Entity)
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("emberlandssleepers")]
class AiTaskBaseTargetable_CanSense_SkipSleepers
{
    static Type _sleeperType;

    static IEnumerable<MethodBase> TargetMethods()
    {
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.Enabled == false)
            yield break;
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.DisableMobAttacks != true)
            yield break;

        _sleeperType = AccessTools.TypeByName("EmberlandsSleepers.EntitySleeper");
        if (_sleeperType == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find EntitySleeper — mob targeting block will not apply");
            yield break;
        }

        yield return AccessTools.Method(typeof(AiTaskBaseTargetable), nameof(AiTaskBaseTargetable.CanSense));
    }

    static bool Prefix(Entity e, ref bool __result)
    {
        if (_sleeperType.IsInstanceOfType(e))
        {
            __result = false;
            return false;
        }

        return true;
    }
}
