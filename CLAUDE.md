# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**TethysServerPatches** is a Vintage Story mod providing compatibility patches and server-specific tweaks. It patches other mods at runtime using HarmonyLib and applies JSON asset patches for recipes and game data. Mod ID: `tethysserverpatches`. Targets Vintage Story 1.22 only (.NET 10.0). No multi-version support is intended.

## Build

```powershell
# Full build + package (validates JSON, compiles, zips release)
.\build.ps1

# Skip JSON validation
.\build.ps1 --skipJsonValidation=true
```

The build script wraps `dotnet run --project CakeBuild/CakeBuild.csproj`. Output goes to `bin/Release/Mods/mod/publish/`; release zip lands in `Releases/tethysserverpatches/`.

There are no automated tests — validation is manual in a running Vintage Story instance.

## Architecture

### Mod System Hierarchy

```
ModSystem (Vintage Story base)
  └── TethysServerPatchesCore   (TethysServerPatchesCore.cs)
        ├── TethysServerPatchesServer  (server-side: loads config, broadcasts to clients on PlayerJoin)
        └── TethysServerPatchesClient  (client-side: receives config over network channel)
```

`TethysServerPatchesCore.StartPre()` is the initialization entry point. It creates the Harmony instance, loads `Configuration`, and activates the appropriate Harmony patch categories based on which mods are detected (`api.ModLoader.IsModEnabled(modid)`). `ExecuteOrder` is 1.0 so this mod loads early.

### Configuration (`Config/Configuration.cs`)

Loaded from `assets/tethysserverpatches/modid.json` at server startup and transmitted to clients via ProtoBuf over a named network channel. All config classes use `[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]`. Each feature section has an `Enabled` flag; Harmony patches check this flag in their prefix before doing anything.

### Harmony Patches (`Patches/`)

Each patch file targets a specific mod. The pattern:

```csharp
[HarmonyPatch]
[HarmonyPatchCategory("modname")]
class TargetClass
{
    static IEnumerable<MethodBase> TargetMethods() { /* reflection-based resolution */ }
    static bool Prefix(...) { /* return false to skip original */ }
}
```

Patches are applied only when the target mod is enabled. Method resolution uses reflection to handle version variations.

### Asset Patches (`assets/tethysserverpatches/patches/`)

JSON patches are organized by target mod under `patches/external/<modname>/` or `patches/<vanilla-domain>/`. These use the standard Vintage Story JSON patch format (`op`, `path`, `value`). Translations go in `lang/en.json`.

## Vintage Story Environment

- **Game install** (`$VINTAGE_STORY`, equiv. `~/AppData/Roaming/Vintagestory`): DLLs, EXEs, built-in mods, unpacked base assets.
- **Data dir** (`~/AppData/Roaming/Vintagestorydata`): Mods, ModsByServer, etc.
- **Built-in mod namespace**: Built-in mods use the `game:` domain. Never use `survival:` or `creative:` namespaces in patches — they are remapped to `game:`.

## Decompiling with ilspycmd

`ilspycmd` is installed as a global .NET tool (v10.0.1). Use it to inspect base game or mod DLLs when you need to understand an API, find method signatures, or verify a patch target.

**Decompile a single type to stdout:**
```bash
ilspycmd "$VINTAGE_STORY/VintagestoryAPI.dll" -t Vintagestory.API.Common.EntityAgent -r "$VINTAGE_STORY"
```

**Decompile a full assembly to a browsable project:**
```bash
ilspycmd "$VINTAGE_STORY/Vintagestory.dll" -p -o /tmp/vs-decompiled -r "$VINTAGE_STORY"
```

**Show raw IL instead of C#:**
```bash
ilspycmd "$VINTAGE_STORY/VintagestoryAPI.dll" -t Vintagestory.API.Common.EntityAgent -il -r "$VINTAGE_STORY"
```

**List all types in an assembly:**
```bash
ilspycmd "$VINTAGE_STORY/VintagestoryAPI.dll" -l class
```

Key flags:
- `-t <FQN>` — decompile one type only (fastest for targeted lookups)
- `-r <dir>` — reference path for resolving dependencies; pass `$VINTAGE_STORY` so cross-assembly refs resolve
- `-p -o <dir>` — emit a full compilable project (needed for whole-assembly browsing)
- `-il` — emit IL bytecode instead of C#
- `--no-dead-code` / `--no-dead-stores` — cleaner output for reading

Built-in mod DLLs live in `$VINTAGE_STORY/Mods/`; third-party mods in `~/AppData/Roaming/Vintagestorydata/Mods/` or, if installed by a server, under `~/AppData/Roaming/Vintagestorydata/ModsByServer/<host>.-<port>/` (e.g. `ModsByServer/srv.satyrn.dev.-15223/`).

## Naming Conventions

- **Item/block display names**: Use sentence case — capitalize only the first word and proper nouns. If a parenthetical is present, reset capitalization inside it (e.g. `"Strong bone handle (Copper)"`, not `"Strong Bone Handle (copper)"`).

## Adding a New Feature

1. **Config flag** — add a field to `Configuration.cs` (ProtoBuf serializes it automatically).
2. **Mod detection** — use `api.ModLoader.IsModEnabled("modid")` in `StartPre()` to gate Harmony category activation.
3. **Harmony patch** — create a new file in `Patches/` following the `[HarmonyPatchCategory]` pattern; check `Configuration.Feature.Enabled` in the prefix.
4. **Asset patches** — add JSON files under `patches/external/<modname>/` if recipe or block data needs changing.
5. **Translations** — add entries to `lang/en.json` for any new user-visible strings.

## Git

All commits and tags **must be signed** (GPG). Never use `--no-gpg-sign` or `--no-verify`.
