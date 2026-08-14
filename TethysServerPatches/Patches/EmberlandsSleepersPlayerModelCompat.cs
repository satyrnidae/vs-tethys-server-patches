using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace TethysServerPatches.Patches;

// Emberland's Sleepers clones a logged-out player's skinConfig/voicetype/voicepitch/health
// onto the spawned sleeper NPC (SleepersModSystem.CaptureAppearance/SpawnBody) but never
// copies skinModel/entitySize — the WatchedAttributes keys PlayerModelLib-based player
// model mods (Goat, Skaven Rat, Vintage Feline, Vintage Opossum, ...) read to pick and
// scale a non-Seraph model. Without them the sleeper always renders as default Seraph
// regardless of which model the player has equipped, even though the sleeper's own skin
// behavior already re-resolves the skinnable-part schema from the live game:player entity
// type, so it's otherwise model-agnostic.
//
// Fix: capture skinModel/entitySize alongside CaptureAppearance's own capture (which runs
// synchronously against the still-live player entity, before any chunk-load deferral) and
// apply them onto the sleeper's WatchedAttributes once it spawns. EntitySleeper.OnEntitySpawn
// fires synchronously from SpawnBody's World.SpawnEntity call, after sleeperOwnerUid has
// already been set, so it's a stable place to pick the captured data back up by player UID.
static class EmberlandsSleepersPlayerModelCompatState
{
    public static readonly ConcurrentDictionary<string, (string SkinModel, float? EntitySize)> Pending = new();
}

[HarmonyPatch]
[HarmonyPatchCategory("emberlandssleepers")]
class SleepersModSystem_CaptureAppearance_CaptureSkinModel
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.Enabled == false)
            yield break;

        var type = AccessTools.TypeByName("EmberlandsSleepers.SleepersModSystem");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find SleepersModSystem — player model compat will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "CaptureAppearance");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find CaptureAppearance — player model compat will not apply");
            yield break;
        }

        yield return method;
    }

    static void Postfix(IServerPlayer player)
    {
        var watchedAttributes = player?.Entity?.WatchedAttributes;
        if (watchedAttributes == null) return;

        var skinModel = watchedAttributes.GetString("skinModel");
        float? entitySize = watchedAttributes.HasAttribute("entitySize") ? watchedAttributes.GetFloat("entitySize") : null;
        if (skinModel == null && entitySize == null) return;

        EmberlandsSleepersPlayerModelCompatState.Pending[player.PlayerUID] = (skinModel, entitySize);
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("emberlandssleepers")]
class EntitySleeper_OnEntitySpawn_ApplySkinModel
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.Enabled == false)
            yield break;

        var type = AccessTools.TypeByName("EmberlandsSleepers.EntitySleeper");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find EntitySleeper — player model compat will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "OnEntitySpawn");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find EntitySleeper.OnEntitySpawn — player model compat will not apply");
            yield break;
        }

        yield return method;
    }

    static void Postfix(Entity __instance)
    {
        var watchedAttributes = __instance.WatchedAttributes;
        var ownerUid = watchedAttributes.GetString("sleeperOwnerUid");
        if (ownerUid == null) return;
        if (!EmberlandsSleepersPlayerModelCompatState.Pending.TryRemove(ownerUid, out var data)) return;

        if (data.SkinModel != null)
        {
            watchedAttributes.SetString("skinModel", data.SkinModel);
        }
        if (data.EntitySize.HasValue)
        {
            watchedAttributes.SetFloat("entitySize", data.EntitySize.Value);
        }
    }
}
