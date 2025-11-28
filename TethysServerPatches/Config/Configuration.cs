using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TethysServerPatches.Config;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Configuration
{
    public PatchFlag ClothiersHeirloomsPatches = new();
    public RPTTSPatchOptions RPTTSPatches = new();
    public AllClassesPatchOptions AllClassesPatches = new();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class PatchFlag
{
    public bool Enabled = true;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class RPTTSPatchOptions
{
    public bool Enabled = true;
    public bool SkipGreeting = false;
    public String[] InitializationGreetings = [];
    public String[] ShortenedMessageBackups = [];
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
    public bool ChefBuffs = true;
    public bool HomesteaderBuffs = true;
}
