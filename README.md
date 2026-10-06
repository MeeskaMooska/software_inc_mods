# Software Inc. mod workspace

This folder targets installed Software Inc. Beta 1.8.42. AI Compute 0.3.0 adds 2018 progression, player-first rival research, wider compute scaling, benchmark-driven discovery, persistent assistant provider locks, and a corrected hosted-model dependency. Automated validation passes; new campaign and UI runtime acceptance remain pending. See the mod guide and playtest checklist for scope and limitations.

## Local setup

- Game: `C:\Program Files (x86)\Steam\steamapps\common\Software Inc`
- Installed version: **Beta 1.8.42**, confirmed from the `Versioning` constants and enum in `Assembly-CSharp.dll`.
- Steam app ID: `362620`; installed build ID: `24837201`.
- Managed game libraries: `<game>\Software Inc_Data\Managed`.
- .NET SDK: `10.0.400`, available through `C:\Program Files\dotnet\dotnet.exe`.
- Blender directory: `C:\Program Files\Blender Foundation\Blender 5.2`.

The executable's version metadata reports Unity 2018.4.36, not the game's Beta version. Use the game version above when checking compatibility. The inspected assembly reports CLR metadata version `v4.0.30319`; a modern .NET SDK alone does not establish which .NET Framework targeting packs are available.

## What we can build

| Route | Examples | Install folder under the game |
| --- | --- | --- |
| Data mods | New software and hardware products, features, AI company types, product names, employee personalities | `Mods\<mod>` |
| Furniture | Desks, computers, decorations, equipment, inherited furniture variants | `Furniture\<mod>` |
| Materials | Wall, floor, roof and path textures | `Materials\<mod>` |
| C# code mods | Custom windows, HUD tools, automation and gameplay behavior | `DLLMods\<mod>` |
| Localization | Translations and localized text | `Localization\<language>` |

See the [official modding overview](https://softwareinc.coredumping.com/wiki/index.php/Modding), [data guide](https://softwareinc.coredumping.com/wiki/index.php/Data_Modding), and [furniture guide](https://softwareinc.coredumping.com/wiki/index.php/Furniture_Modding).

## Data mods

Data definitions use TyD text files. A mod can contain `SoftwareTypes`, `CompanyTypes`, `NameGenerators`, and a root `Personalities.tyd`. Software definitions can include categories, market preferences, development effort, dependencies and unlock dates. Hardware also defines manufacturing components and assembly processes.

New software needs an associated company type if AI competitors should develop it. Features can use SIPL scripts for supported product events.

The wiki includes vanilla data downloads, but its newest listed archive is Beta 1.7.15. Treat it as reference material and validate against our installed Beta 1.8.42. Overriding a software type's `Features` list replaces the list, so merging features requires care.

Sources: [Data Modding](https://softwareinc.coredumping.com/wiki/index.php/Data_Modding), [TyD syntax](https://softwareinc.coredumping.com/wiki/index.php/TyD).

## C# mods

The game supports source `.cs` files and compiled `.dll` files. The wiki documents a C# 3 limit for the built-in compiler, an enum compiler bug, and source compilation as the route required for Steam Workshop uploads. Follow those constraints when targeting Workshop distribution.

Local metadata inspection confirmed these abstract methods:

```text
ModMeta:
  string Name { get; }
  void ConstructOptionsScreen(UnityEngine.RectTransform parent, bool inGame)

ModBehaviour:
  void OnActivate()
  void OnDeactivate()
```

Use `OnActivate`, despite the wiki's older `OnActive` wording. Both classes also expose serialization hooks in this build.

For a compiled project, choose a compatible .NET Framework target and reference the installed `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, and necessary Unity libraries. Check actual API signatures before implementing a feature. Unsubscribe event handlers when a mod deactivates.

Source: [Code Modding](https://softwareinc.coredumping.com/wiki/index.php/Code_Modding). API signatures above were independently checked against the local game assembly without executing it.

## Development and testing

Keep each mod's source in its own folder here. Copy its deployable package to the corresponding game mod folder when ready for testing. Add `meta.tyd` for its name, author and description.

Bind the debug console in the game's key bindings. Useful documented commands include:

- Data: `RELOAD_MOD`, `TEST_DEV_MOD`, `CHECK_SPEC_REP`, `CHECK_ADDON_MARKET`.
- Localization: `GENERATE_LOCALIZATION`.
- Code: `RECOMPILE_DLL_MOD`, `RELOAD_DLL_MOD`, `UNLOAD_DLL_MOD`, `SHOW_INSPECTOR`.
- Furniture: `RELOAD_FURNITURE`, `FURNITURE_DEBUG`, `FURNITURE_THUMBNAIL`.

Data reloads do not update a currently running game. Test data changes in a fresh test company. Code reloads can leave the game unstable; verify with a clean restart as well. Record the tested game version and check both enable/disable behavior and save/load behavior where relevant.

Sources: [Console commands](https://softwareinc.coredumping.com/wiki/index.php/Console), [Code Modding](https://softwareinc.coredumping.com/wiki/index.php/Code_Modding).

## AI Compute

The expanded implementation includes dated model tiers and features, compute allocation, paid datasets, dense/sparse architectures, quantization, context, capability-specific assistance, generative media, workload-based inference, model fallback/preferences, fine-tuning, full retraining, native subscriptions and paid rival training. The data mod adds hosted chat, coding and image applications under one software type. Both mod halves install into the game's `Mods` and `DLLMods` directories. See [the prototype guide](mods/AI%20Compute/README.md) for gameplay, balance and limitations. Run `tools/Build-AICompute.ps1` to compile against the installed game API, run the offline/native-metadata test suites and create `build/AI-Compute-0.3.0-source.zip`.

The [feasibility report](research/ai-training/FEASIBILITY.md) preserves the initial API investigation. The prototype guide and code supersede its proposed balance. Runtime acceptance checks are in [the playtest checklist](tests/AICompute-playtest.md), with actual game captures in [the review gallery](review/SCREENSHOTS.md). Computer Use was repaired on October 4 by preserving its corrupt sandbox bookkeeping file and allowing native startup to regenerate it; no permission profile was changed.
