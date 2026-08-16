using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace TethysServerPatches.State;

/// <summary>
/// Shared state for the interaction help async patch. Created by TethysServerPatchesClient
/// before the category is applied; disposed when the mod unloads.
/// </summary>
sealed class InteractionHelpFixState : IDisposable
{
    public static InteractionHelpFixState Instance { get; private set; }

    public static InteractionHelpFixState Create(int gameThreadId)
    {
        Instance = new InteractionHelpFixState(gameThreadId);
        return Instance;
    }

    InteractionHelpFixState(int gameThreadId) => GameThreadId = gameThreadId;

    /// <summary>ManagedThreadId of the VS game/render thread, set at creation time.</summary>
    public int GameThreadId { get; }

    /// <summary>Client API reference, set in StartClientSide.</summary>
    public ICoreClientAPI Capi { get; set; }

    /// <summary>DrawWorldInteractionUtil instance, captured on first compose call.</summary>
    public DrawWorldInteractionUtil WiUtil { get; set; }

    /// <summary>DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp(WorldInteraction[]) method ref.</summary>
    public MethodInfo WiUtilComposeMethod { get; set; }

    // ----- PendingCompose - thread-safe Action queue -----
    // Getter atomically takes and clears the pending action (take-and-clear semantics).
    // Setter atomically replaces the pending action (background thread queues here).

    private volatile Action _pendingCompose;

    public Action PendingCompose
    {
        get => Interlocked.Exchange(ref _pendingCompose, null);
        set => Interlocked.Exchange(ref _pendingCompose, value);
    }

    /// <summary>
    /// Re-entry guard: true while vanilla's compose body is running with pre-swapped delegates,
    /// so a recursive call to ComposeBlockWorldInteractionHelp passes through unmodified.
    /// </summary>
    public bool IsComposing { get; set; }

    // ----- Delegate-resolution result cache -----
    // Keyed on (blockId, x, y, z, selectionBoxIndex) so the expensive resolve only runs once
    // per distinct block the player targets.

    public (int blockId, int x, int y, int z, int selIdx) CacheKey { get; set; }
    public WorldInteraction[] CachedInteractions { get; set; }
    public ItemStack[][] CachedStacks { get; set; }

    // ----- Wis population cache (Block and Entity) -----
    // Keyed by block.BlockId or entity.Code.ToString() so the expensive GetInteractionHelp
    // scan runs only once per interactable type across all game sessions.

    public ConcurrentDictionary<int, WorldInteraction[]> BlockWisCache { get; } = new();
    // One SemaphoreSlim(1,1) per block type: allows parallel resolution across types,
    // prevents duplicate tasks for the same type.
    public ConcurrentDictionary<int, SemaphoreSlim> BlockWisLocks { get; } = new();
    public ConcurrentDictionary<string, WorldInteraction[]> EntityWisCache { get; } = new();
    public ConcurrentDictionary<string, SemaphoreSlim> EntityWisLocks { get; } = new();

    public void Dispose()
    {
        PendingCompose      = null;
        Capi                = null;
        WiUtil              = null;
        WiUtilComposeMethod = null;
        IsComposing         = false;
        CachedInteractions  = null;
        CachedStacks        = null;
        BlockWisCache.Clear();
        foreach (var sem in BlockWisLocks.Values) sem.Dispose();
        BlockWisLocks.Clear();
        EntityWisCache.Clear();
        foreach (var sem in EntityWisLocks.Values) sem.Dispose();
        EntityWisLocks.Clear();
        if (Instance == this)
            Instance = null;
    }
}
