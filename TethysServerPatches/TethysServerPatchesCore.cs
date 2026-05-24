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

    private bool _clothierHeirloomsModInstalled;
    private bool _fgcInstalled;
    private bool _rpttsInstalled;
    private bool _rightClickPickupPatched;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        Api = api;
        Logger = Mod.Logger;
        ModId = Mod.Info.ModID;
        HarmonyInstance = new Harmony(ModId);
        _clothierHeirloomsModInstalled = api.ModLoader.IsModEnabled("clothierheirloomsmod");
        _fgcInstalled = api.ModLoader.IsModEnabled("fromgoldencombs");
        _rpttsInstalled = api.ModLoader.IsModEnabled("rptts");
        Configuration ??= LoadConfiguration(api);
        if (_clothierHeirloomsModInstalled)
        {
            Logger.Notification("Patching category clothierheirloomsmod");
            HarmonyInstance.PatchCategory("clothierheirloomsmod");
        }
        if (_fgcInstalled)
        {
            Logger.Notification("Patching category fromgoldencombs");
            HarmonyInstance.PatchCategory("fromgoldencombs");
        }
        if (_rpttsInstalled)
        {
            Logger.Notification("Patching category rptts");
            HarmonyInstance.PatchCategory("rptts");
        }

        var rightClickPickupConflict = api.ModLoader.IsModEnabled("vsrightclickpickup")
            || api.ModLoader.IsModEnabled("clicktopick") || api.ModLoader.IsModEnabled("precisepickedup");
        if (!rightClickPickupConflict)
        {
            Logger.Notification("Patching category rightclickpickup");
            HarmonyInstance.PatchCategory("rightclickpickup");
            _rightClickPickupPatched = true;
        }
        else
        {
            Logger.Notification("Skipping rightclickpickup patch: conflicting mod installed (vsrightclickpickup or clicktopick)");
        }

        Logger.Notification("Patching category cookingrecipereentrancyfix");
        HarmonyInstance.PatchCategory("cookingrecipereentrancyfix");

        //HarmonyInstance.PatchCategory("survival");
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
            //HarmonyInstance.UnpatchCategory("survival");
            HarmonyInstance.UnpatchCategory("cookingrecipereentrancyfix");
            if (_rightClickPickupPatched)
            {
                HarmonyInstance.UnpatchCategory("rightclickpickup");
            }
            if (_fgcInstalled)
            {
                HarmonyInstance.UnpatchCategory("fromgoldencombs");
            }
            if (_rpttsInstalled)
            {
                HarmonyInstance.UnpatchCategory("rptts");
            }
            if (_clothierHeirloomsModInstalled)
            {
                HarmonyInstance.UnpatchCategory("clothierheirloomsmod");
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
