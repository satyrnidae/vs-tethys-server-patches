using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TethysServerPatches.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace TethysServerPatches;
class TethysServerPatchesClient : TethysServerPatchesCore
{
    public static TethysServerPatchesClient Instance { get; private set; }

    public ICoreClientAPI ClientApi => Api as ICoreClientAPI;
    public IClientNetworkChannel ClientNetworkChannel => NetworkChannel as IClientNetworkChannel;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        ClientNetworkChannel.SetMessageHandler<Configuration>(ReceiveServerConfiguration);
    }

    public override void Dispose()
    {
        base.Dispose();
        if (Instance == this)
        {
            Instance = null;
        }
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
