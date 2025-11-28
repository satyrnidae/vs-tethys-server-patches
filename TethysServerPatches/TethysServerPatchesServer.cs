using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TethysServerPatches.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TethysServerPatches
{
    class TethysServerPatchesServer : TethysServerPatchesCore
    {
        private ICoreServerAPI ServerApi => Api as ICoreServerAPI;
        private IServerNetworkChannel ServerNetworkChannel => NetworkChannel as IServerNetworkChannel;

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

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
            api.World.Config.SetBool("AltMetalPotRecipes", configInstance.AllClassesPatches.AltMetalPotRecipes);
            api.World.Config.SetBool("CheaperChefPots", configInstance.AllClassesPatches.CheaperChefPots);
            api.World.Config.SetBool("ChefBuffs", configInstance.AllClassesPatches.ClassCustomizations.ChefBuffs);
            api.World.Config.SetBool("HomesteaderBuffs", configInstance.AllClassesPatches.ClassCustomizations.HomesteaderBuffs);


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
