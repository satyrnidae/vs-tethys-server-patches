using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TethysServerPatches.Config;
using Vintagestory.API.Client;

namespace TethysServerPatches.Patches;

[HarmonyPatch]
[HarmonyPatchCategory("rptts")]
class KittenTTSEngine_InitializationGreet
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ttschat, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase initializationGreeting;
        if (asm == null
            || (type = asm.GetType("RPTTS.KittenTTSEngine", throwOnError: false)) == null
            || (initializationGreeting = type.GetMethod("InitializationGreet")) == null)
        {
            TethysServerPatchesCore.Logger.Error("Failed to patch method even though mod is loaded! Did the name change?");
            throw new Exception();
        }
        return [initializationGreeting];
    }

    static bool Prefix(Object __instance, Random ___MessageRNG, ICoreClientAPI ___ClientAPI)
    {
        RpTtsPatches opts = TethysServerPatchesCore.Configuration.RpTtsPatches;
        if (opts.Enabled)
        {
            if (opts.InitializationGreetings.Length != 0)
            {
                var threadCount = (int)__instance.GetType().GetMethod("GetThreadCount").Invoke(__instance, []);

                ___ClientAPI.Logger.Notification($"[tethysserverpatches x rptts] Threads available: {threadCount} (out of {Environment.ProcessorCount})");

                var localVoiceId = (int)__instance.GetType().GetProperty("LocalVoiceID").GetValue(__instance);
                float playerPitch = (float)__instance.GetType().GetProperty("PlayerPitch").GetValue(__instance);

                string text = opts.InitializationGreetings[___MessageRNG.Next(opts.InitializationGreetings.Length)];
                ___ClientAPI.Logger.Notification($"[tethysserverpatches x rptts] Initialization sanity started: '{text}'");
                __instance.GetType().GetMethod("Speak").Invoke(__instance, [text, localVoiceId, playerPitch, null, 1f, 1f]);
                ___ClientAPI.Logger.Notification("[tethysserverpatches x rptts] Initialization sanity ended.");
                return false;
            }

            if (opts.SkipGreeting)
            {
                ___ClientAPI.Logger.Notification("[tethysserverpatches x rptts] Skipping initialization sanity check");
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("rptts")]
class KittenTTSEngine_Speak
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var asm = Assembly.Load("ttschat, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        Type type;
        MethodBase speak;
        if (asm == null
            || (type = asm.GetType("RPTTS.KittenTTSEngine", throwOnError: false)) == null
            || (speak = type.GetMethod("Speak")) == null)
        {
            TethysServerPatchesCore.Logger.Error("Failed to patch method even though mod is loaded! Did the name change?");
            throw new Exception();
        }
        return [speak];
    }

    static bool Prefix(Object __instance, Random ___MessageRNG, ref string text)
    {
        RpTtsPatches opts = TethysServerPatchesCore.Configuration.RpTtsPatches;
        if (!opts.Enabled)
        {
            return true;
        }

        var forbidLongMessages = (bool)__instance.GetType().GetProperty("ForbidLongMessages").GetValue(__instance);
        if (forbidLongMessages && text.Length > 84 && opts.ShortenedMessageBackups.Length > 0) // Magic number
        {
            text = opts.ShortenedMessageBackups[___MessageRNG.Next(opts.ShortenedMessageBackups.Length)];
        }

        return true;
    }
}
