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
    private bool _vsroofingInstalled;
    private bool _immersiveSawingInstalled;
    private bool _rightClickPickupPatched;
    private bool _immersiveWoodSawingInstalled;
    private bool _smithingPlusInstalled;
    private bool _deadPlayerModelLibInstalled;
    private bool _emberlandsSleepersInstalled;

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
        _immersiveWoodSawingInstalled = api.ModLoader.IsModEnabled("immersivewoodsawing");
        _vsroofingInstalled = api.ModLoader.IsModEnabled("vsroofing");
        _immersiveSawingInstalled = api.ModLoader.IsModEnabled("immersivewoodsawing");
        _smithingPlusInstalled = api.ModLoader.IsModEnabled("smithingplus");
        _deadPlayerModelLibInstalled = api.ModLoader.IsModEnabled("dead") && api.ModLoader.IsModEnabled("playermodellib");
        _emberlandsSleepersInstalled = api.ModLoader.IsModEnabled("emberlandssleepers");
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
        if (_vsroofingInstalled)
        {
            Logger.Notification("Patching category vsroofing");
            HarmonyInstance.PatchCategory("vsroofing");
        }
        if (_immersiveSawingInstalled)
        {
            Logger.Notification("Patching category immersivesawingcompat");
            HarmonyInstance.PatchCategory("immersivesawingcompat");
        }
        if (_emberlandsSleepersInstalled)
        {
            Logger.Notification("Patching category emberlandssleepers");
            HarmonyInstance.PatchCategory("emberlandssleepers");
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

        Logger.Notification("Patching category quenchannealing");
        HarmonyInstance.PatchCategory("quenchannealing");

        if (_smithingPlusInstalled)
        {
            Logger.Notification("Patching category quenchannealing-smithingplus");
            HarmonyInstance.PatchCategory("quenchannealing-smithingplus");
        }

        if (_deadPlayerModelLibInstalled)
        {
            Logger.Notification("Patching category dead-playermodellib");
            HarmonyInstance.PatchCategory("dead-playermodellib");
        }

        // vsrightclickpickup changes pickup semantics so the dropped-entity interaction path that
        // triggers the NRE never fires — no need for the defensive patch when it is present.
        if (_immersiveWoodSawingInstalled && !rightClickPickupConflict)
        {
            Logger.Notification("Patching category immersivewoodsawing");
            HarmonyInstance.PatchCategory("immersivewoodsawing");
        }
        else if (_immersiveWoodSawingInstalled)
        {
            Logger.Notification("Skipping immersivewoodsawing patch: vsrightclickpickup (or equivalent) installed");
        }

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
            HarmonyInstance.UnpatchCategory("quenchannealing");
            if (_smithingPlusInstalled)
            {
                HarmonyInstance.UnpatchCategory("quenchannealing-smithingplus");
            }
            if (_immersiveWoodSawingInstalled)
            {
                HarmonyInstance.UnpatchCategory("immersivewoodsawing");
            }
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
            if (_vsroofingInstalled)
            {
                HarmonyInstance.UnpatchCategory("vsroofing");
            }
            if (_clothierHeirloomsModInstalled)
            {
                HarmonyInstance.UnpatchCategory("clothierheirloomsmod");
            }
            if (_immersiveSawingInstalled)
            {
                HarmonyInstance.UnpatchCategory("immersivesawingcompat");
            }
            if (_deadPlayerModelLibInstalled)
            {
                HarmonyInstance.UnpatchCategory("dead-playermodellib");
            }
            if (_emberlandsSleepersInstalled)
            {
                HarmonyInstance.UnpatchCategory("emberlandssleepers");
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
