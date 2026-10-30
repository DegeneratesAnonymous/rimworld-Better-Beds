# Plan

Primary target: RimWorld 1.7 on the unstable branch. 1.6 builds are kept until 1.7 reaches the main branch.

## Milestones

- [x] M0: Mod loads in 1.6 and logs "Loaded" from a Harmony-initialized `Mod` class
- [ ] M1: Verify in game that the patch fires; use verbose logging to learn the call patterns
- [ ] M2: Scorer picks private, higher-impressiveness rooms for unassigned colonists (code in place, needs in-game tuning)
- [ ] M3: Overflow toggle, gizmo, icon, and save/load round trip (code in place, needs in-game verification)
- [ ] M4: Settings window with weights (code in place, needs tuning of defaults)
- [ ] M5: Compatibility pass and first Workshop release
- [ ] M6: Room requirements and room negotiation (code in place, needs in-game verification)

## M6: Rooms, requirements, negotiation

### The unit: mood points
Bed quality is how many mood points a night there is worth, using the numbers the game already has, so a "better room" is exactly one that makes the pawn happier.
- Room: the vanilla "slept in a bedroom / barracks" thought for the room's impressiveness stage (bedroom -2..+8, barracks -7..+3), outdoors -4. Ascetics ignore it.
- Rest: (bed rest effectiveness - 1) x 10.
- Royalty, mirroring the game: a title-holder needs a personal bedroom (only love partners share it), else -6; if it is personal, it must meet the title's requirements, else -6. Respects the "allow room requirements" toggle and both grace periods. Ideology roles have no bedroom requirements in the game's data.
- Jealous trait: -8 when someone owns a room 10% more impressive (the game's own comparison).
- Choosing a free bed also subtracts 0.01 per cell walked and 20 for overflow beds.

### Choosing a bed
Postfix on `RestUtility.FindBedFor`, running last. Vanilla and other mods decide first; we only replace a free bed (or none) for a bedless colonist, never a bed someone owns. Candidates must pass the game's `IsValidBedFor`.

### Negotiation
A colonist who owns a bed alone and has a sleep debuff can choose "negotiate for a better room" as recreation. The debuff is any of: slept in barracks / outside / on the ground / cold / hot / in a room with a slave, or the game's situational thoughts for title bedroom requirements, no personal bedroom, or bedroom jealousy.
- Target: owns a bed alone, is not using it, not the asker's love partner, not locked, not on pair cooldown, not a recent winner over the asker. Skipped if a free bed is as good.
- Reason: the move removes a title or jealousy penalty, or (optional) is an upgrade of at least 3 mood.
- Roll (d20 each, ties to the defender): asker = Persuade (Social + opinion/10) or Intimidate (Melee x 0.75; never by Kind pawns or against friends; chosen only if 2+ better), defender = matching skill; both add title (seniority/100 x 3) and up to +3 for time in the colony (1 per 45 days); defender +3 for holding the room. Targets ranked by gain x win chance; under 10% is not attempted.
- Tuning (simulated over typical colonists): equal pawns ~34% to win when asking, one title step ~ +13 points, three steps ~ 74-84%, opinion -50..+50 moves 17%..57%.
- Asker earns Social XP: 60 on a win, 30 on a fail.
- Moods: 0.25 x relationship (opinion/10, -10..+10) + 0.25 x room gain (mood points, capped at 12), floor 1, cap 8. Loser: intimidated = debuff; persuaded and likes the winner = buff; else none. Winner (the only one getting a new room): buff. Intimidation also leaves a 15 day opinion penalty on the loser.
- Cooldown 3 days per pair (either direction); a loser cannot challenge the same winner for 15 days.
- Shown in the play log (so Interaction Bubbles draws speech bubbles for the ask and the answer).

### Player control
A bed assigned through the assign window (vanilla or a reskin of it) is locked to that owner. Locked beds are never targets and their owners never ask. A lock button on every bed toggles it; a lock stops applying when the owner changes.

### Sleeping rough
With no bed to use, the game's "lie down where you stand" is replaced by dozing in a free indoor chair (upright, since the game has no sitting posture), with a -3 thought in place of the -4 floor thought.

### Open
- Verify in game: the recreation activity is picked, bubbles appear, the chair looks acceptable.
- Temperature debuffs trigger a request but are not in the room score.
- A titled colonist is not moved into a suitable free room by negotiation.

## 1.7 checks

- [ ] Mod loads on the current 1.7 unstable build with Harmony 3.x
- [ ] Re-test after each 1.7 unstable update; bump `RimWorldRefVersion` when a new Krafs package appears
- [ ] Same test checklist passes on 1.6

## Test checklist (dev mode)

- [ ] Dorm with 4 beds near the work area, 2 empty private bedrooms farther away: new colonist claims a private room
- [ ] All private rooms full: new colonist takes a dorm bed
- [ ] Dorm beds marked overflow, one private room free: overflow is skipped
- [ ] Prisoner, guest, sick colonist, slave, and child each still get vanilla behavior
- [ ] Save, reload, confirm overflow flags persist
- [ ] Remove the mod from a save, confirm the save still loads

## Known limits in v1

- Only fully unowned beds are considered, so a colonist won't join a half-claimed double bed. Vanilla handles that case.
- Ideology roles carry no bedroom requirements in the game's data (their room requirements are for ritual buildings), so only Royalty titles are weighted.
