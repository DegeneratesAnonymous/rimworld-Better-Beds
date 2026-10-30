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

## M6: Requirements and negotiation

1. Requirements are a pawn property, read from the game. Royalty title bedroom requirements are used now; Ideology role room requirements are not exposed on the role precept in the reference API, so they are a follow-up. An unmet requirement is a heavy score penalty in the bedtime search, not a ban.
2. A pawn with no bed searches for the best available room (existing search plus the requirement penalty). A pawn who lost a room gets no special handling.
3. Negotiation is a recreation activity, always decided by a contested d20 roll. A pawn who owns a bed and has a sleep-related mood debuff (barracks, cold, heat, slave in room, outside, ground) or an unmet title requirement can choose "negotiate for a better room" during joy time. They walk to the other pawn, talk for a while, and the roll decides at the end.
   - The other pawn must own a bed (alone; shared beds are excluded, a double bed with one owner is fine), not be using it right now, and not be the initiator's love partner. Skipped if a free room is at least as good.
   - Anyone may challenge anyone they have a reason to. Reasons: the room fixes an unmet title requirement, or (own toggle) the quality gain is above a threshold.
   - Roll: asker = best of Persuade (Social skill + the defender's opinion of them / 10) or Intimidate (Melee x 0.75); defender = the matching skill. Both add title (seniority / 100 x title weight, default 3) and up to +5 for time in the colony; the defender gets +3 for holding the room. Ties go to the defender. Targets are ranked by gain x win chance; under 10% is not attempted.
   - Social XP for the asker: 60 on a win, 30 on a fail.
   - Mood for the loser on a win: 20 x (0.25 x relationship + 0.25 x room quality), relationship = opinion / 100, room quality = gain / 500. Intimidation is always a debuff; persuasion is a buff only if the loser likes the asker. Intimidation also leaves a 15 day opinion penalty, win or lose.
   - Cooldown: 3 days between the same two pawns (either direction), after every conversation. A pawn can still negotiate with others. A loser can't challenge the same winner for 15 days.
   - The winner takes the loser's bed and releases their old one; the loser is left bedless and finds a room at their own bedtime.
   - Excluded: prisoners, guests, slaves, children, medical beds.
   - Optional messages (on) and mood effects (on) can each be turned off in settings.

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
