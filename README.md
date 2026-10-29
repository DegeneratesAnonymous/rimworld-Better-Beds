# Smarter Beds

A RimWorld mod for 1.7 (unstable branch) and 1.6. Colonists without a bed claim the best free **private bedroom** instead of whatever bed is nearest. Beds marked **overflow** (a toggle on any bed) are only used when nothing better is free, so your unrenovated dorm stays empty until refugees show up.

Vanilla picks a bed by bed type, then distance. This mod scores each free bed by room impressiveness, whether the room is a private bedroom, bed rest effectiveness, distance, and the overflow flag. Weights are adjustable in mod settings.

Prisoner, guest, slave, medical, and child bed choice is left entirely to vanilla.

**Status:** pre-release scaffold. See [PLAN.md](PLAN.md) for milestones.

## Requirements

- RimWorld 1.7 (unstable branch) or 1.6
- [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077)

## Building

RimWorld 1.7 moved from Unity to Ludeon's Waylith engine, which runs on .NET 10. 1.6 still runs on Unity and .NET Framework 4.7.2. The project builds one version at a time and puts each DLL in its own version folder.

You need the **.NET 10 SDK**. For an IDE, use Visual Studio 2026 or Rider 2026; Visual Studio 2022 and older Rider versions can't build .NET 10 projects. No RimWorld install is required to compile: game references come from the `Krafs.Rimworld.Ref` NuGet package.

```
dotnet build Source/SmarterBeds/SmarterBeds.csproj -c Release                      # 1.7 -> 1.7/Assemblies/
dotnet build Source/SmarterBeds/SmarterBeds.csproj -c Release -p:GameVersion=1.6   # 1.6 -> 1.6/Assemblies/
```

1.7 is unstable and changes often. When Ludeon pushes a 1.7 update, bump `RimWorldRefVersion` in the `.csproj` to the newest `1.7.*-unstable` Krafs.Rimworld.Ref package and rebuild. Code that must differ between versions can use `#if RW_1_7` / `#if RW_1_6`.

## Testing in game

Link the repo into your Mods folder so every build is live on the next game launch. To play 1.7, switch RimWorld to the `unstable` branch in Steam (Properties, Betas tab). On Windows, from an admin Command Prompt:

```
mklink /J "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\SmarterBeds" "C:\path\to\rimworld-smarter-beds"
```

Enable dev mode, turn on **Log bed decisions** in mod settings, and watch the debug log as colonists pick beds.

## Repo layout

| Path | Contents |
| --- | --- |
| `About/` | Mod metadata and preview image |
| `1.7/Assemblies/`, `1.6/Assemblies/` | Built DLLs per game version (ignored by git, built by CI) |
| `Languages/English/Keyed/` | All player-facing text |
| `Textures/UI/` | Gizmo icon |
| `Source/SmarterBeds/` | C# source |
| `Source/SmarterBeds/Patches/` | Harmony patches |

## Releasing

Push a tag like `v0.1.0`. The Release workflow builds both DLLs and attaches a ready-to-install zip to a GitHub Release. Upload to the Workshop from the in-game Mods screen with dev mode on, then commit the generated `About/PublishedFileId.txt`.

## License

MIT. See [LICENSE](LICENSE).
