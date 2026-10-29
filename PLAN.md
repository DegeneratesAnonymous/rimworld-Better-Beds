# Plan

Primary target: RimWorld 1.7 on the unstable branch. 1.6 builds are kept until 1.7 reaches the main branch.

## Milestones

- [x] M0: Mod loads in 1.6 and logs "Loaded" from a Harmony-initialized `Mod` class
- [ ] M1: Verify in game that the patch fires; use verbose logging to learn the call patterns
- [ ] M2: Scorer picks private, higher-impressiveness rooms for unassigned colonists (code in place, needs in-game tuning)
- [ ] M3: Overflow toggle, gizmo, icon, and save/load round trip (code in place, needs in-game verification)
- [ ] M4: Settings window with weights (code in place, needs tuning of defaults)
- [ ] M5: Compatibility pass and first Workshop release

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
- Ideology and Royalty bedroom requirements aren't weighted yet.
