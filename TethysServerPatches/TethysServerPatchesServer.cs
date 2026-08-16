using System;
using TethysServerPatches.Config;
using TethysServerPatches.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TethysServerPatches
{
    class TethysServerPatchesServer : TethysServerPatchesCore
    {
        private ICoreServerAPI ServerApi => Api as ICoreServerAPI;
        private IServerNetworkChannel ServerNetworkChannel => NetworkChannel as IServerNetworkChannel;

        public static CharacterSystem CharacterSystem { get; private set; }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            CharacterSystem = api.ModLoader.GetModSystem<CharacterSystem>() ?? throw new Exception(
                $"Failed to locate the {nameof(CharacterSystem)} built-in mod. Are you running with Survival Mod enabled?");

            api.Event.PlayerJoin += Event_PlayerJoin;
            api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;

            if (Configuration.EmberlandsSleepersPatches.Enabled && Configuration.EmberlandsSleepersPatches.SafeDisconnectBackup)
            {
                var pendingBackups = EmberlandsSleepersSafeDisconnectUtil.PendingBackupCount(api);
                if (pendingBackups > 0)
                {
                    Logger.Notification(
                        $"[emberlandssleepers] {pendingBackups} disconnect inventory backup(s) are pending reclaim from a previous session.");
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            if (ServerApi != null)
            {
                ServerApi.Event.PlayerJoin -= Event_PlayerJoin;
                ServerApi.Event.PlayerNowPlaying -= Event_PlayerNowPlaying;
            }
        }

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        protected override Configuration LoadConfiguration(ICoreAPI api)
        {
            var loadSuccessful = false;
            Configuration configInstance = null;
            try
            {
                configInstance = api.LoadModConfig<Configuration>(ModId + ".json");
                loadSuccessful = true;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to load mod configuration for Tethys Server Patches: {e.Message}!");
                Logger.Warning("Default configuration will be used. Please correct your config file.");
            }

            configInstance ??= new Configuration();
            api.World.Config.SetBool("TethysServerPatches_AltMetalPotRecipes", configInstance.AllClassesPatches.AltMetalPotRecipes);
            api.World.Config.SetBool("TethysServerPatches_CheaperChefPots", configInstance.AllClassesPatches.CheaperChefPots);
            api.World.Config.SetBool("TethysServerPatches_Chef_AddFarmer", configInstance.AllClassesPatches.ClassCustomizations.ChefBuffs && configInstance.AllClassesPatches.ClassCustomizations.ChefTraitFlags.AddFarmer);
            api.World.Config.SetBool("TethysServerPatches_Chef_AddKnifeSkills", configInstance.AllClassesPatches.ClassCustomizations.ChefBuffs && configInstance.AllClassesPatches.ClassCustomizations.ChefTraitFlags.AddKnifeSkills);
            api.World.Config.SetBool("TethysServerPatches_Chef_AddForager", configInstance.AllClassesPatches.ClassCustomizations.ChefBuffs && configInstance.AllClassesPatches.ClassCustomizations.ChefTraitFlags.AddForager);
            api.World.Config.SetBool("TethysServerPatches_Chef_ReplaceExhaustedWithNearsighted", configInstance.AllClassesPatches.ClassCustomizations.ChefBuffs && configInstance.AllClassesPatches.ClassCustomizations.ChefTraitFlags.ReplaceExhaustedWithNearsighted);
            api.World.Config.SetBool("TethysServerPatches_Chef_RemoveClumsy", configInstance.AllClassesPatches.ClassCustomizations.ChefBuffs && configInstance.AllClassesPatches.ClassCustomizations.ChefTraitFlags.RemoveClumsy);
            api.World.Config.SetBool("TethysServerPatches_Homesteader_AddClothier", configInstance.AllClassesPatches.ClassCustomizations.HomesteaderBuffs && configInstance.AllClassesPatches.ClassCustomizations.HomesteaderTraitFlags.AddClothier);
            api.World.Config.SetBool("TethysServerPatches_Homesteader_AddScavenger", configInstance.AllClassesPatches.ClassCustomizations.HomesteaderBuffs && configInstance.AllClassesPatches.ClassCustomizations.HomesteaderTraitFlags.AddScavenger);
            api.World.Config.SetBool("TethysServerPatches_FixCabbageOffsets", configInstance.VanillaFixes.FixCabbageOffsets);
            api.World.Config.SetBool("TethysServerPatches_StackableTemporalGears", configInstance.VanillaTweaks.StackableTemporalGears);
            api.World.Config.SetBool("TethysServerPatches_CustomOmokPieces", configInstance.VanillaTweaks.CustomOmokPieces);
            api.World.Config.SetBool("TethysServerPatches_ButcheringBoneTools", configInstance.VanillaTweaks.ButcheringBoneTools);
            api.World.Config.SetBool("TethysServerPatches_Annealing", configInstance.VanillaTweaks.Annealing);
            api.World.Config.SetBool("TethysServerPatches_RandomElkGender", configInstance.VanillaTweaks.RandomElkGender);
            api.World.Config.SetBool("TethysServerPatches_RockWormSpawn", configInstance.VanillaTweaks.RockWormSpawn.Enabled);
            api.World.Config.SetBool("TethysServerPatches_MoreHackles", configInstance.AldiClassesPatches.MoreHackles);
            api.World.Config.SetBool("TethysServerPatches_CarbonPoleBaitFix", configInstance.AldiClassesPatches.CarbonPoleBaitFix);
            api.World.Config.SetBool("TethysServerPatches_CastawayDisablePlateMold", configInstance.CastawayPatches.DisablePlateMold);
            api.World.Config.SetBool("TethysServerPatches_Toolsmith_DisableColdSmithing", configInstance.Toolsmith.DisableColdSmithing);
            api.World.Config.SetBool("TethysServerPatches_Toolsmith_ButcheringStrongBoneHandles", configInstance.Toolsmith.ButcheringStrongBoneHandles);
            api.World.Config.SetBool("TethysServerPatches_ForestPreserve_ReduceWoodOutputs", configInstance.ForestPreservePatches.ReduceWoodOutputs);
            api.World.Config.SetBool("TethysServerPatches_LongTermFood_DisableSoybeanMilkPressing", configInstance.LongTermFoodPatches.DisableSoybeanMilkPressing);
            api.World.Config.SetBool("TethysServerPatches_CureFirewood_CharcoalPitFullEfficiency", configInstance.CureFirewoodPatches.CharcoalPitFullEfficiency);

            // VS Roofing added its own chiseltools:itemtypes/truechisel behavior patch in 1.7.1,
            // making our own truechisel-* addmerge redundant (and duplicated) from that version on.
            api.World.Config.SetBool("TethysServerPatches_VSRoofingLegacy", IsModOlderThan(api, "vsroofing", "1.7.1"));

            // ChiselTools fixed its own broken texture paths and reworked the palette-light recipe
            // in 1.17.4, making our fixup patches redundant (and, for the recipe fix, no longer
            // even applicable, since the ingredient key it targets is gone).
            api.World.Config.SetBool("TethysServerPatches_ChiselToolsLegacy", IsModOlderThan(api, "chiseltools", "1.17.4"));

            // Improved Metallurgy started shipping its own toolsmith binding compat (nails/strips
            // for toolsteel, hadfieldsteel, inconel) in 1.1.8, duplicating the BindingPartDefine/
            // BindingStatDefine entries our own compat config registers under the same codes.
            api.World.Config.SetBool("TethysServerPatches_ImprovedMetallurgyLegacy", IsModOlderThan(api, "improvedmetallurgy", "1.1.8"));

            if (loadSuccessful)
            {
                try
                {
                    api.StoreModConfig(configInstance, ModId + ".json");
                }
                catch (Exception e)
                {
                    Logger.Error($"Failed to save configuration for Tethys Server Patches: {e.Message}!");
                }
            }

            return configInstance;
        }

        private static bool IsModOlderThan(ICoreAPI api, string modid, string version)
        {
            var mod = api.ModLoader.GetMod(modid);
            return mod != null && GameVersion.IsLowerVersionThan(mod.Info.Version, version);
        }

        private void Event_PlayerJoin(IServerPlayer player)
        {
            Logger.Debug($"Player {player.PlayerName} joined, sending configuration client-side.");
            ServerNetworkChannel.SendPacket(Configuration, player);
        }

        private void Event_PlayerNowPlaying(IServerPlayer player)
        {
            var emberlandsSleepers = Configuration.EmberlandsSleepersPatches;
            if (!emberlandsSleepers.Enabled || !emberlandsSleepers.SafeDisconnectBackup) return;
            if (!EmberlandsSleepersSafeDisconnectUtil.HasBackup(ServerApi, player.PlayerUID)) return;

            if (!EmberlandsSleepersSafeDisconnectUtil.TrackedInventoriesEmpty(player))
            {
                // Emberland's Sleepers (or its own reclaim logic) already restored the player
                // normally; the backup is now redundant.
                EmberlandsSleepersSafeDisconnectUtil.DiscardBackup(ServerApi, player.PlayerUID);
                return;
            }

            if (EmberlandsSleepersSafeDisconnectUtil.TryRestoreBackup(ServerApi, player, out var itemCount))
            {
                Logger.Warning(
                    $"[emberlandssleepers] Restored {itemCount} item(s) to {player.PlayerName} from a disconnect backup — " +
                    "Emberland's Sleepers did not reclaim them automatically (mod missing/disabled or reclaim failed).");
            }
        }
    }
}
