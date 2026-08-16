using System.Threading;
using TethysServerPatches.Config;
using TethysServerPatches.State;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace TethysServerPatches;
class TethysServerPatchesClient : TethysServerPatchesCore
{
    public static TethysServerPatchesClient Instance { get; private set; }

    public ICoreClientAPI ClientApi => Api as ICoreClientAPI;
    public IClientNetworkChannel ClientNetworkChannel => NetworkChannel as IClientNetworkChannel;

    public override void StartPre(ICoreAPI api)
    {
        InteractionHelpFixState.Create(Thread.CurrentThread.ManagedThreadId);
        base.StartPre(api); // creates HarmonyInstance and applies other categories
        HarmonyInstance.PatchCategory("interactionhelpfix");
        HarmonyInstance.PatchCategory("grindingwheelnullfix");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        InteractionHelpFixState.Instance.Capi = api;
        api.Event.RegisterGameTickListener(_ =>
        {
            InteractionHelpFixState.Instance?.PendingCompose?.Invoke();
        }, 1);

        ClientNetworkChannel.SetMessageHandler<Configuration>(ReceiveServerConfiguration);
    }

    public override void Dispose()
    {
        HarmonyInstance?.UnpatchCategory("interactionhelpfix");
        HarmonyInstance?.UnpatchCategory("grindingwheelnullfix");
        InteractionHelpFixState.Instance?.Dispose();

        base.Dispose();
        if (Instance == this)
            Instance = null;
    }

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    protected override Configuration LoadConfiguration(ICoreAPI api)
    {
        // Loads nothing client-side
        return null;
    }

    private void ReceiveServerConfiguration(Configuration configuration)
    {
        // We'll just plop the decoded config in the static prop. Should be fine for local servers.
        Configuration = configuration;
    }
}
