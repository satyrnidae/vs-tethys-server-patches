using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using TethysServerPatches.Config;
using System;
using System.Collections.Generic;
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

    private readonly Dictionary<string, bool?> PatchStats = [];

    private bool _clothierHeirloomsModInstalled;
    private bool _fgcInstalled;
    private bool _rpttsInstalled;
    private bool _vsroofingInstalled;
    private bool _immersiveSawingInstalled;
    private bool _immersiveWoodSawingInstalled;
    private bool _smithingPlusInstalled;
    private bool _deadPlayerModelLibInstalled;
    private bool _emberlandsSleepersInstalled;

    private bool TryPatchCategory(string category)
    {
        try
        {
            HarmonyInstance.PatchCategory(category);
            PatchStats[category] = true;
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to apply patch category '{category}': {e}");
            PatchStats[category] = false;
            return false;
        }
    }

    private void TryUnpatchCategory(string category)
    {
        if (PatchStats.GetValueOrDefault(category) != true)
        {
            return;
        }
        try
        {
            HarmonyInstance.UnpatchCategory(category);
            PatchStats[category] = false;
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to unpatch category '{category}': {e}");
        }
    }

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
            TryPatchCategory("clothierheirloomsmod");
        }
        if (_fgcInstalled)
        {
            Logger.Notification("Patching category fromgoldencombs");
            TryPatchCategory("fromgoldencombs");
        }
        if (_rpttsInstalled)
        {
            Logger.Notification("Patching category rptts");
            TryPatchCategory("rptts");
        }
        if (_vsroofingInstalled)
        {
            Logger.Notification("Patching category vsroofing");
            TryPatchCategory("vsroofing");
        }
        if (_immersiveSawingInstalled)
        {
            Logger.Notification("Patching category immersivesawingcompat");
            TryPatchCategory("immersivesawingcompat");
        }
        if (_emberlandsSleepersInstalled)
        {
            Logger.Notification("Patching category emberlandssleepers");
            TryPatchCategory("emberlandssleepers");
        }

        var rightClickPickupConflict = api.ModLoader.IsModEnabled("vsrightclickpickup")
            || api.ModLoader.IsModEnabled("clicktopick") || api.ModLoader.IsModEnabled("precisepickedup");
        if (!rightClickPickupConflict)
        {
            Logger.Notification("Patching category rightclickpickup");
            TryPatchCategory("rightclickpickup");
        }
        else
        {
            Logger.Notification("Skipping rightclickpickup patch: conflicting mod installed (vsrightclickpickup or clicktopick)");
        }

        Logger.Notification("Patching category cookingrecipereentrancyfix");
        TryPatchCategory("cookingrecipereentrancyfix");

        Logger.Notification("Patching category quenchannealing");
        TryPatchCategory("quenchannealing");

        if (_smithingPlusInstalled)
        {
            Logger.Notification("Patching category quenchannealing-smithingplus");
            TryPatchCategory("quenchannealing-smithingplus");
        }

        if (_deadPlayerModelLibInstalled)
        {
            Logger.Notification("Patching category dead-playermodellib");
            TryPatchCategory("dead-playermodellib");
        }

        if (api.ModLoader.IsModEnabled("betterhoe") && api.ModLoader.IsModEnabled("desirepaths"))
        {
            Logger.Notification("Patching category betterhoedesirepathscompat");
            TryPatchCategory("betterhoedesirepathscompat");
        }

        // vsrightclickpickup changes pickup semantics so the dropped-entity interaction path that
        // triggers the NRE never fires — no need for the defensive patch when it is present.
        if (_immersiveWoodSawingInstalled && !rightClickPickupConflict)
        {
            Logger.Notification("Patching category immersivewoodsawing");
            TryPatchCategory("immersivewoodsawing");
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
            //TryUnpatchCategory("survival");
            TryUnpatchCategory("cookingrecipereentrancyfix");
            TryUnpatchCategory("quenchannealing");
            TryUnpatchCategory("quenchannealing-smithingplus");
            TryUnpatchCategory("betterhoedesirepathscompat");
            TryUnpatchCategory("immersivewoodsawing");
            TryUnpatchCategory("rightclickpickup");
            TryUnpatchCategory("fromgoldencombs");
            TryUnpatchCategory("rptts");
            TryUnpatchCategory("vsroofing");
            TryUnpatchCategory("clothierheirloomsmod");
            TryUnpatchCategory("immersivesawingcompat");
            TryUnpatchCategory("dead-playermodellib");
            TryUnpatchCategory("emberlandssleepers");
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
