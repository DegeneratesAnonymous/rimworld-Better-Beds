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
3. Negotiation happens at bedtime, only for a pawn who owns a bed and is about to sleep with a problem: an active sleep-related mood debuff (barracks, cold, heat, slave in room, outside, ground) or an unmet title requirement. Checked at most once an hour per pawn.
   - Only against pawns who also own a bed and aren't using it right now (reservation check), never against their love partner.
   - Requirement case: A's room fails A's requirements, B's room is better for A, and A outranks B (title seniority).
   - Quality case (own toggle, large threshold): equal rank, and the quality gain exceeds the threshold. Ties are broken by time in the colony, then id, so no pair can swap back and forth.
   - Skipped if a free room is at least as good for A.
   - The winner takes the loser's bed and releases their old one, then heads to it; the loser is left bedless and finds a room at their own bedtime.
   - Per-pawn cooldown after a swap. Excluded: shared beds and couples, medical beds, prisoners, guests, slaves, children.
   - Optional mild "lost my room" thought (off by default) and an optional message.

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
- Ideology role bedroom requirements aren't weighted yet (Royalty titles are, from M6).
