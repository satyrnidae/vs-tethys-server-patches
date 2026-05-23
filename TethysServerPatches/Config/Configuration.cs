using ProtoBuf;

namespace TethysServerPatches.Config;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Configuration
{
    public PatchFlag ClothiersHeirloomsPatches = new();
    public RpTtsPatches RpTtsPatches = new();
    public AllClassesPatchOptions AllClassesPatches = new();
    public VanillaFixes VanillaFixes = new();
    public VanillaTweaks VanillaTweaks = new();
    public AldiClassesPatchOptions AldiClassesPatches = new();
    public CastawayPatchOptions CastawayPatches = new();
    public ToolsmithTweaks Toolsmith = new();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class VanillaTweaks
{
    public bool StackableTemporalGears = true;
    public RightClickPickupOptions RightClickPickup = new();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class RightClickPickupOptions
{
    public bool Enabled = true;
    public bool RequireEmptyHand = false;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class AldiClassesPatchOptions
{
    public bool MoreHackles = true;
    public bool CarbonPoleBaitFix = true;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class CastawayPatchOptions
{
    public bool DisablePlateMold = true;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class PatchFlag
{
    public bool Enabled;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class VanillaFixes
{
    public bool FixCabbageOffsets = true;
    public bool AsyncInteractionHelp = true;
    // Max entries composed per unique interaction type (ActionLangCode); 0 = no limit.
    public int MaxInteractionHelpEntries = 16;
    // Guards BlockCookingContainer.GetMatchingCookingRecipe against re-entrant calls.
    // Prevents a stack overflow triggered by EternalStew's recipe matching recursing
    // back into GetMatchingCookingRecipe through CookingRecipe.Matches.
    public bool FixCookingRecipeReentrancy = true;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class RpTtsPatches : PatchFlag
{
    public bool SkipGreeting;
    public string[] InitializationGreetings = [];
    public string[] ShortenedMessageBackups = [];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class AllClassesPatchOptions
{
    public AllClassesClassCustomizations ClassCustomizations = new();
    public bool AltMetalPotRecipes = true;
    public bool CheaperChefPots = true;
}
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class AllClassesClassCustomizations
{
    // Deprecated. Setting to false disabled all ChefTraitFlags flags.
    public bool ChefBuffs = true;
    public ChefTraitFlags ChefTraitFlags = new();
    // Deprecated. Setting to false disabled all HomesteaderTraitFlags flags.
    public bool HomesteaderBuffs = true;
    public HomesteaderTraitFlags HomesteaderTraitFlags = new();

}
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ChefTraitFlags
{
    public bool AddFarmer = true;
    public bool AddKnifeSkills = true;
    public bool AddForager = true;
    public bool ReplaceExhaustedWithNearsighted = true;
    public bool RemoveClumsy = true;
}
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class HomesteaderTraitFlags
{
    public bool AddScavenger = true;
    public bool AddClothier = true;
}
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ToolsmithTweaks
{
    public bool DisableColdSmithing = true;
}
