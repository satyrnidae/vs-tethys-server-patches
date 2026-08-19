using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TethysServerPatches.Utils;

// Loose rocks/boulders sit on top of the same worm density map the wormgrunter tool reads
// (ModSystemWormGrunting), bucketed into 3x3x3 regions via pos/3 - see AddHarvest/
// GetEarthWormAmount. Collecting one disturbs the ground the same way grunting does, so it
// pulls one worm out of that region's count.
public static class RockWormSpawnUtil
{
    public static void TrySpawnWorm(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaTweaks.RockWormSpawn.Enabled != true) return;
        if (world.Side != EnumAppSide.Server) return;
        if (byPlayer == null || byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative) return;

        var wormSystem = world.Api.ModLoader.GetModSystem<ModSystemWormGrunting>();
        if (wormSystem == null || wormSystem.GetEarthWormAmount(pos) <= 0f) return;

        wormSystem.AddHarvest(pos, 1);

        var entityType = world.GetEntityType(new AssetLocation("earthworm"));
        if (entityType == null) return;

        var entity = world.ClassRegistry.CreateEntity(entityType);
        entity.Pos.X = pos.X + (float)world.Rand.NextDouble();
        entity.Pos.Y = pos.Y;
        entity.Pos.Z = pos.Z + (float)world.Rand.NextDouble();
        entity.Pos.Yaw = (float)world.Rand.NextDouble() * 2f * (float)Math.PI;
        world.SpawnEntity(entity);
    }
}
