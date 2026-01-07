using ProtoBuf;

namespace TethysServerPatches.Config;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Configuration
{
    public PatchFlag ClothiersHeirloomsPatches = new();
    public RpTtsPatches RpTtsPatches = new();
    public AllClassesPatchOptions AllClassesPatches = new();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class PatchFlag
{
    public bool Enabled = true;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class RpTtsPatches
{
    public bool Enabled = true;
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
