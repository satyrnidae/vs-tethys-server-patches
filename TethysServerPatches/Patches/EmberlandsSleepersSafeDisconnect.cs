using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TethysServerPatches.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TethysServerPatches.Patches;

// Emberland's Sleepers wipes a disconnecting player's inventory synchronously inside
// SpawnSleeperFor, before the replacement sleeper NPC has actually spawned successfully.
// The mod's own snapshot/reclaim system (TryStartReclaim/RestoreFromSnapshot) already
// recovers correctly if the sleeper fails to spawn *and the mod stays loaded* on the next
// login. It has no recovery path at all if the mod is later disabled, removed, or fails to
// load — the wiped inventory is gone, and its snapshot is stranded in a mod-private file
// only that mod version can read.
//
// This prefix keeps an independent, Tethys-owned backup of the tracked inventories
// (character/hotbar/backpack) immediately before the wipe. The fallback restore lives in
// TethysServerPatchesServer.Event_PlayerNowPlaying, which only exists at the Tethys core
// level (not gated on the target mod being installed), so it can still recover the player's
// items even when Emberland's Sleepers never runs again.
[HarmonyPatch]
[HarmonyPatchCategory("emberlandssleepers")]
class SleepersModSystem_SpawnSleeperFor_BackupInventory
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.Enabled == false)
            yield break;
        if (TethysServerPatchesCore.Configuration?.EmberlandsSleepersPatches.SafeDisconnectBackup != true)
            yield break;

        var type = AccessTools.TypeByName("EmberlandsSleepers.SleepersModSystem");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find SleepersModSystem — disconnect inventory backup will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "SpawnSleeperFor");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[emberlandssleepers] Could not find SpawnSleeperFor — disconnect inventory backup will not apply");
            yield break;
        }

        yield return method;
    }

    static void Prefix(IServerPlayer player)
    {
        if (player?.Entity?.World?.Api is not ICoreAPI api) return;

        try
        {
            EmberlandsSleepersSafeDisconnectUtil.WriteBackup(api, player);
        }
        catch (Exception e)
        {
            TethysServerPatchesCore.Logger.Error(
                $"[emberlandssleepers] Failed to back up {player.PlayerName}'s inventory before disconnect: {e.Message}");
        }
    }
}
