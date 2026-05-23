using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TethysServerPatches.Patches;

// Guards BlockCookingContainer.GetMatchingCookingRecipe against re-entrant calls on
// the same thread.
//
// Root cause: EternalStew patches GetMatchingCookingRecipe. Its implementation calls
// CookingRecipe.Matches, which dispatches via an interface back into
// GetMatchingCookingRecipe on the same frame, producing unbounded recursion and a
// StackOverflowException on the main server thread during firepit burn ticks.
//
// The guard returns null (no matching recipe) for the inner call. canSmeltInput()
// treats null as "nothing to smelt" and idles the firepit for that tick — safe.
[HarmonyPatch]
[HarmonyPatchCategory("cookingrecipereentrancyfix")]
class BlockCookingContainer_GetMatchingCookingRecipe_ReentrancyGuard
{
    [ThreadStatic]
    static bool _executing;

    [ThreadStatic]
    static bool _warned;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("Vintagestory.GameContent.BlockCookingContainer");
        if (type == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[cookingrecipereentrancyfix] Could not find BlockCookingContainer — patch will not apply");
            yield break;
        }

        var method = AccessTools.Method(type, "GetMatchingCookingRecipe",
            [typeof(IWorldAccessor), typeof(ItemStack[]), typeof(int).MakeByRefType()]);
        if (method == null)
        {
            TethysServerPatchesCore.Logger.Error(
                "[cookingrecipereentrancyfix] Could not find GetMatchingCookingRecipe — patch will not apply");
            yield break;
        }

        yield return method;
    }

    static bool Prefix(ref CookingRecipe __result)
    {
        if (TethysServerPatchesCore.Configuration?.VanillaFixes.FixCookingRecipeReentrancy == false)
            return true;

        if (_executing)
        {
            if (!_warned)
            {
                _warned = true;
                TethysServerPatchesCore.Logger.Warning(
                    "[cookingrecipereentrancyfix] Re-entrant call to BlockCookingContainer." +
                    "GetMatchingCookingRecipe detected and blocked. Likely cause: EternalStew " +
                    "recipe matching loop. Firepit will idle this tick. (logged once per thread)");
            }
            __result = null;
            return false;
        }

        _executing = true;
        return true;
    }

    static void Finalizer()
    {
        _executing = false;
    }
}
