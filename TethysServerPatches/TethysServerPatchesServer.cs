using System;
using TethysServerPatches.Config;
using Vintagestory.API.Common;
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

        }

        public override void Dispose()
        {
            base.Dispose();
            if (ServerApi != null)
            {
                ServerApi.Event.PlayerJoin -= Event_PlayerJoin;
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

        private void Event_PlayerJoin(IServerPlayer player)
        {
            Logger.Debug($"Player {player.PlayerName} joined, sending configuration client-side.");
            ServerNetworkChannel.SendPacket(Configuration, player);
        }
    }
}
