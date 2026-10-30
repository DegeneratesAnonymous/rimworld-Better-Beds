# Wil's Better Beds

A RimWorld mod for 1.7 (unstable branch) and 1.6 about who sleeps where.

- **Better bed choice.** A colonist without a bed takes the nicest bed they can reach, scored in mood points using the game's own numbers for bedrooms, barracks and sleeping outside. Distance does not count against a bed.
- **Royal and trait awareness.** Title-holders need a personal bedroom that meets their title, and the Jealous trait envies better rooms. Both are scored exactly as the game scores them.
- **Room negotiation.** A colonist who sleeps badly can pick "negotiate for a better room" as recreation, walk to another colonist, and ask. A contested d20 roll decides it, using Social (or Melee, to intimidate), opinion, royal title and time in the colony. The winner takes the room, the loser finds another at bedtime, and both feel it in their mood. The conversation shows in the play log and, with Interaction Bubbles, as speech bubbles.
- **You have the last word.** When you set a bed's owner with the Set Owner button, the assignment is locked: nobody negotiates for that room and its owner does not negotiate either. Unassign the owner to release it.
- **Last resort sleeping.** A colonist with no bed dozes in a chair instead of on the floor, for a smaller mood penalty.
- **Assign rooms.** The Assign tab gets an **Assign rooms** button. It lists every room with beds or cribs (beds taken, cribs taken, room score, size, owners). Hover a room to point at its beds in the world, double-click to jump the camera there, and use **Assign** to set owners room by room.
- **Family and couples.** Colonists prefer to share a room with family they get on with. A colonist with negative relations with a parent, child or sibling wants a room outside 35 tiles of them. A couple wants a room of just the two of them, close to family, and a couple far from family gets a "room far from family" thought. New couples are moved into a free room of their own.
- **God mode.** A selected colonist gets a **Force negotiation** button that sends them to ask for a better room right now.

Prisoner, guest, slave, medical, and child bed choice is left entirely to vanilla.

**Status:** pre-release scaffold. See [PLAN.md](PLAN.md) for milestones.

## Requirements

- RimWorld 1.7 (unstable branch) or 1.6
- [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077)

## Compatibility

Wil's Better Beds is built to coexist with other bed mods rather than overrule them. The approach:

- Bed choice is a *postfix*, running last on `RestUtility.FindBedFor`. Vanilla and every other mod decide first; we only replace the answer when it is a free bed (or none) for a bedless colonist, and never a bed someone already owns, such as a partner's.
- Which beds a colonist may use is decided by the game's own `IsValidBedFor`, which other mods patch. Anything they forbid, we forbid.
- Rooms and titles are read through the game's methods, so mods that change title requirements are honoured.
- Nothing is added to any other mod's bed definitions. The "set by you" locks are kept in the save, keyed by bed and owner, so they work for every bed from every mod.
- Every patch is applied on its own and every feature that runs from the UI, a patch or a thought catches its own errors. If a game update or another mod breaks one thing, that one feature is skipped and logged once, and the rest keep working. The bottom of the mod options shows how many patches are active and which, if any, were skipped, and the dev log has the same line at startup.
- Needs no DLC. Royalty (titles), Ideology (slaves, precepts) and Biotech (cribs) features switch on when those are present.

| Mod | Behaviour |
| --- | --- |
| Hospitality (Continued) | Guest beds are separate bed defs and `IsValidBedFor` keeps colonists out of them, so we never take one. Guests are never part of negotiation. |
| Interaction Bubbles | Negotiation lines are written as ordinary interactions, so each speaker gets a bubble. |
| Dubs Mint Menus | Its assign window ends up in the same assignment method as vanilla's, so setting an owner through it locks the bed. |
| Vanilla Factions Expanded - Empire | Changes royal bedroom requirements through the game's title methods, which we read. |
| Replace Stuff, EBSG Framework, Worksites Expanded, Vanilla Races Expanded - Android | Patch bed choice or ownership. We run after them and call the game's own claim and validity methods. |
| Realistic Rooms Rewritten | Only relabels room size stages; room impressiveness is untouched. |

Known limits: Replace Stuff rebuilds a bed as a new object, which can drop the lock on a replaced bed. Ideology roles carry no bedroom requirements in the game's data, so only Royalty titles are scored. Temperature debuffs count as a reason to ask for a room but are not part of the room score.

## Building

RimWorld 1.7 moved from Unity to Ludeon's Waylith engine, which runs on .NET 10. 1.6 still runs on Unity and .NET Framework 4.7.2. The project builds one version at a time and puts each DLL in its own version folder.

You need the **.NET 10 SDK**. For an IDE, use Visual Studio 2026 or Rider 2026; Visual Studio 2022 and older Rider versions can't build .NET 10 projects. No RimWorld install is required to compile: game references come from the `Krafs.Rimworld.Ref` NuGet package.

```
dotnet build Source/SmarterBeds/SmarterBeds.csproj -c Release                      # 1.7 -> 1.7/Assemblies/
dotnet build Source/SmarterBeds/SmarterBeds.csproj -c Release -p:GameVersion=1.6   # 1.6 -> 1.6/Assemblies/
```

1.7 is unstable and changes often. When Ludeon pushes a 1.7 update, bump `RimWorldRefVersion` in the `.csproj` to the newest `1.7.*-unstable` Krafs.Rimworld.Ref package and rebuild. Code that must differ between versions can use `#if RW_1_7` / `#if RW_1_6`.

## Testing in game

Run the deploy script. It builds both versions and copies the mod into RimWorld's `Mods` folder:

```
scripts\deploy-to-game.ps1
```

Use `-Game "D:\path\to\RimWorld"` if your game is not at the default location, or `-NoBuild` to copy without rebuilding. In your mod manager, refresh, activate **Wil's Better Beds**, sort, and launch. To play 1.7, switch RimWorld to the `unstable` branch in Steam (Properties, Betas tab).

**Do not use a junction or symlink to the repo.** RimWorld 1.7 runs on .NET 10, which refuses to read a folder reached through one ("The path cannot be traversed because it contains an untrusted mount point") and the game starts to a black screen. The deploy script refuses to run if the destination is a link.

Also note that DLLs are not committed, so installing the mod by cloning the repo into `Mods` gives you a mod with no code. Use the deploy script, or a release zip.

Enable dev mode, turn on **Log decisions** in mod settings, and watch the debug log as colonists pick beds.

## Repo layout

| Path | Contents |
| --- | --- |
| `About/` | Mod metadata and preview image |
| `1.7/Assemblies/`, `1.6/Assemblies/` | Built DLLs per game version (ignored by git, built by CI) |
| `Languages/English/Keyed/` | All player-facing text |
| `Source/SmarterBeds/` | C# source |
| `Source/SmarterBeds/Patches/` | Harmony patches |
| `scripts/` | `deploy-to-game.ps1`, builds and installs the mod into your game |

## Releasing

Push a tag like `v0.1.0`. The Release workflow builds both DLLs and attaches a ready-to-install zip to a GitHub Release. Upload to the Workshop from the in-game Mods screen with dev mode on, then commit the generated `About/PublishedFileId.txt`.

## License

MIT. See [LICENSE](LICENSE).
