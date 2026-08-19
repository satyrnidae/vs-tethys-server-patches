using System;
using HarmonyLib;
using Vintagestory.API.Common;

namespace TethysServerPatches.Utils;

static class BetterHoeDesirePathsCompatUtil
{
    public static bool IsDesirePathsSoilPath(Block block)
        => block.Code?.Domain == "desirepaths" && block.Code.Path.StartsWith("soilpath", StringComparison.Ordinal);

    public static T GetConfigValue<T>(object config, string propertyName)
        => (T)AccessTools.Property(config.GetType(), propertyName).GetValue(config);
}
