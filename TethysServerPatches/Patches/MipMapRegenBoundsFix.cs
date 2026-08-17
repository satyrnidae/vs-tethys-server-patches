using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace TethysServerPatches.Patches;

// Suppresses an ArgumentOutOfRangeException in TextureAtlasManager.RegenMipMaps
// that hard-crashes the client render loop after an abrupt disconnect.
//
// Root cause: RuntimeUploadTextureToPos defers RegenMipMaps(atlasNumber) through two
// nested EnqueueMainThreadTask calls, so it can run several frames later. If the
// client session is torn down in between (atlas list disposed/shrunk), atlasNumber
// is stale and AtlasTextures[atlasNumber] throws, killing the whole client.
//
// Fix: prefix skips the regen when atlasNumber is out of range for the current
// AtlasTextures list.
//
// IMPORTANT — patch lifetime: this category is patched once per client process and
// deliberately NEVER unpatched in TethysServerPatchesClient.Dispose(). The crash this
// fixes fires from a deferred main-thread task that runs AFTER session Dispose() (see
// ClientMain.DestroyGameSession -> Dispose -> clientSystems[].Dispose()), so unpatching
// on the normal per-session cycle would leave the fix inactive by the time it's needed.
[HarmonyPatch]
[HarmonyPatchCategory("mipmapregenboundsfix")]
class TextureAtlasManager_RegenMipMaps_BoundsFix
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = typeof(TextureAtlasManager);
        var method = AccessTools.Method(type, "RegenMipMaps");
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[mipmapregenboundsfix] Could not find TextureAtlasManager.RegenMipMaps — patch not applied");
            yield break;
        }
        yield return method;
    }

    static bool Prefix(TextureAtlasManager __instance, int atlasNumber)
    {
        var atlasTextures = __instance.AtlasTextures;
        if (atlasTextures == null || atlasNumber < 0 || atlasNumber >= atlasTextures.Count)
        {
            return false;
        }
        return true;
    }
}
