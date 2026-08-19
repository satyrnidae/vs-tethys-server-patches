using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace TethysServerPatches.Utils;

// Emberland's Sleepers wipes a disconnecting player's inventory synchronously inside
// SpawnSleeperFor, before the replacement sleeper NPC has actually spawned successfully.
// The mod's own snapshot/reclaim system (TryStartReclaim/RestoreFromSnapshot) already
// recovers correctly if the sleeper fails to spawn *and the mod stays loaded* on the next
// login. It has no recovery path at all if the mod is later disabled, removed, or fails to
// load — the wiped inventory is gone, and its snapshot is stranded in a mod-private file
// only that mod version can read.
//
// This util keeps an independent, Tethys-owned backup of the tracked inventories
// (character/hotbar/backpack) immediately before the wipe (called from
// Patches/EmberlandsSleepersSafeDisconnect.cs). The fallback restore lives in
// TethysServerPatchesServer.Event_PlayerNowPlaying, which only exists at the Tethys core
// level (not gated on the target mod being installed), so it can still recover the player's
// items even when Emberland's Sleepers never runs again.
public static class EmberlandsSleepersSafeDisconnectUtil
{
    public static readonly string[] TrackedInventoryClassNames =
    [
        GlobalConstants.characterInvClassName,
        GlobalConstants.hotBarInvClassName,
        GlobalConstants.backpackInvClassName,
    ];

    public static string BackupDir(ICoreAPI api) =>
        api.GetOrCreateDataPath(Path.Combine("ModData", api.World.SavegameIdentifier, "tethysserverpatches", "emberlandssleepers-backup"));

    public static string BackupPath(ICoreAPI api, string playerUid) =>
        Path.Combine(BackupDir(api), playerUid + ".dat");

    public static bool TrackedInventoriesEmpty(IPlayer player)
    {
        foreach (var className in TrackedInventoryClassNames)
        {
            var inventory = player.InventoryManager.GetOwnInventory(className);
            if (inventory != null && !inventory.Empty) return false;
        }
        return true;
    }

    public static void WriteBackup(ICoreAPI api, IPlayer player)
    {
        var entries = new List<(string ClassName, int SlotIndex, byte[] StackBytes)>();
        foreach (var className in TrackedInventoryClassNames)
        {
            var inventory = player.InventoryManager.GetOwnInventory(className);
            if (inventory == null) continue;

            for (var i = 0; i < inventory.Count; i++)
            {
                var slot = inventory[i];
                if (slot?.Itemstack == null) continue;
                entries.Add((className, i, slot.Itemstack.ToBytes()));
            }
        }

        if (entries.Count == 0) return;

        Directory.CreateDirectory(BackupDir(api));
        using var stream = File.Create(BackupPath(api, player.PlayerUID));
        using var writer = new BinaryWriter(stream);
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            writer.Write(entry.ClassName);
            writer.Write(entry.SlotIndex);
            writer.Write(entry.StackBytes.Length);
            writer.Write(entry.StackBytes);
        }
    }

    public static bool TryRestoreBackup(ICoreAPI api, IServerPlayer player, out int itemCount)
    {
        itemCount = 0;
        var path = BackupPath(api, player.PlayerUID);
        if (!File.Exists(path)) return false;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var className = reader.ReadString();
                var slotIndex = reader.ReadInt32();
                var stackBytes = reader.ReadBytes(reader.ReadInt32());

                var inventory = player.InventoryManager.GetOwnInventory(className);
                var slot = inventory?[slotIndex];
                if (slot == null) continue;

                var itemstack = new ItemStack();
                using (var stackStream = new MemoryStream(stackBytes))
                using (var stackReader = new BinaryReader(stackStream))
                {
                    itemstack.FromBytes(stackReader);
                }
                if (!itemstack.ResolveBlockOrItem(player.Entity.World)) continue;

                slot.Itemstack = itemstack;
                slot.MarkDirty();
                itemCount++;
            }
        }
        finally
        {
            File.Delete(path);
        }

        return itemCount > 0;
    }

    public static bool HasBackup(ICoreAPI api, string playerUid) => File.Exists(BackupPath(api, playerUid));

    public static int PendingBackupCount(ICoreAPI api)
    {
        var dir = BackupDir(api);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.dat").Length : 0;
    }

    public static void DiscardBackup(ICoreAPI api, string playerUid)
    {
        var path = BackupPath(api, playerUid);
        if (File.Exists(path)) File.Delete(path);
    }
}
