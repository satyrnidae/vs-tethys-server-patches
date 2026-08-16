using TethysServerPatches.Utils;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TethysServerPatches.Behaviors;

// Registered in place of the vanilla "RightClickPickup" behavior on loosestones/looseflints via a
// JSON patch (see patches/rockwormspawn-loosestones-behavior.json). Runs after the pickup so the
// dropped stack/sound/claim checks stay exactly vanilla; only spawns a worm once the pickup itself
// actually succeeded.
public class BlockBehaviorRightClickPickupWormSpawn : BlockBehaviorRightClickPickup
{
    public BlockBehaviorRightClickPickupWormSpawn(Block block) : base(block) { }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        var picked = base.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);
        if (picked)
        {
            RockWormSpawnUtil.TrySpawnWorm(world, blockSel.Position, byPlayer);
        }
        return picked;
    }
}
