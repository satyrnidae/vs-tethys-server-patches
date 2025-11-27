using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;

namespace TethysServerPatches;
public class TethysServerPatchesCore : ModSystem
{
    public static ILogger Logger { get; private set; }
    public static string ModId { get; private set; }
    public static ICoreAPI Api { get; private set; }
    public static Harmony HarmonyInstance { get; private set; }
    private bool clothierHeirloomsModInstalled;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        Api = api;
        Logger = Mod.Logger;
        ModId = Mod.Info.ModID;
        HarmonyInstance = new Harmony(ModId);
        clothierHeirloomsModInstalled = api.ModLoader.IsModEnabled("clothierheirloomsmod");
        if (clothierHeirloomsModInstalled)
        {
            HarmonyInstance.PatchCategory("clothierheirloomsmod");
        }
    }
    
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
    }
    
    public override void Dispose()
    {
        if (HarmonyInstance != null && clothierHeirloomsModInstalled)
        {
            HarmonyInstance.UnpatchCategory("clothierheirloomsmod");
        }
        HarmonyInstance = null;
        Logger = null;
        ModId = null;
        Api = null;
        base.Dispose();
    }

    public override double ExecuteOrder()
    {
        return 1.0;
    }
}
