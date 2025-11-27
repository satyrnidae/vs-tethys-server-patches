using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using TethysServerPatches.Config;
using System;
using System.Reflection;

namespace TethysServerPatches;
public abstract class TethysServerPatchesCore : ModSystem
{
    public static ILogger Logger { get; private set; }
    public static string ModId { get; private set; }

    public static Configuration Configuration { get; protected set; }

    protected ICoreAPI Api { get; private set; }
    protected Harmony HarmonyInstance { get; private set; }
    protected INetworkChannel NetworkChannel { get; private set; }

    private bool clothierHeirloomsModInstalled;
    private bool rpttsInstalled;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        Api = api;
        Logger = Mod.Logger;
        ModId = Mod.Info.ModID;
        HarmonyInstance = new Harmony(ModId);
        clothierHeirloomsModInstalled = api.ModLoader.IsModEnabled("clothierheirloomsmod");
        rpttsInstalled = api.ModLoader.IsModEnabled("rptts");
        if (Configuration == null)
        {
            Configuration = LoadConfiguration(api);
        }
        if (clothierHeirloomsModInstalled)
        {
            HarmonyInstance.PatchCategory("clothierheirloomsmod");
        }
        if (rpttsInstalled)
        {
            HarmonyInstance.PatchCategory("rptts");
        }
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        var netChannel = api.Network.GetChannel(ModId);
        if (netChannel != null)
        {
            Logger.Warning($"Channel {ModId} was already registered at startup!");
            NetworkChannel ??= netChannel;
        }
        else
            NetworkChannel = api.Network.RegisterChannel(ModId) ??
                throw new Exception($"Failed to register channel {ModId} on side {api.Side}");

        NetworkChannel.RegisterMessageType(typeof(Configuration));
    }

    public override void Dispose()
    {
        if (HarmonyInstance != null)
        {
            if (clothierHeirloomsModInstalled)
            {
                HarmonyInstance.UnpatchCategory("clothierheirloomsmod");
            }
            if (rpttsInstalled)
            {
                HarmonyInstance.UnpatchCategory("rptts");
            }
        }
        NetworkChannel = null;
        HarmonyInstance = null;
        Logger = null;
        ModId = null;
        Api = null;
        Configuration = null;
        base.Dispose();
    }

    public override double ExecuteOrder()
    {
        return 1.0;
    }

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return false;
    }

    protected abstract Configuration LoadConfiguration(ICoreAPI api);
}