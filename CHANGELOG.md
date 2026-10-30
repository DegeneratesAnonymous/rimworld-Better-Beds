# Changelog

All notable changes are listed here. Versions follow semantic versioning.

## [Unreleased]

### Settings
- Every tooltip now says what lowering and raising the setting does, and the default. Sliders with a calculable effect also show a worked example for the current value (win odds, bed value, intimidation vs persuasion, days of tenure, chair vs floor)
- New switches: royal title bedroom rules, the Jealous trait, the Assign rooms button; a slider for the chair-sleep penalty
- The family, neighbour and chair sliders now set the real mood of their thoughts, not only the bed score
- Family distance and the dislike threshold have their own section, since neighbours use them even with family rules off
- Messages and the chair options no longer disappear when negotiation is off

### Fixed
- The tooltip of the "sleeping too close to ..." thoughts named the colonist themselves instead of the person they dislike
- A negotiation could end without being settled if the asker's recreation filled up mid-conversation; it now always reaches the roll
- Nobody asks someone who is asleep, drafted, downed or in a mental break
- Old cooldown entries are cleared every hour, not only after a successful swap
- Couple housing scores each bed once per person instead of once per pair of beds

### Added
- Neighbours: a colonist dislikes sleeping within the family distance (35 tiles) of someone they dislike who is not family (-4, up to two people), and likes sleeping near someone of their own ideoligion (+2) or xenotype (+2, "people like me", baseliners excluded). All three count in bed choice, couple housing and negotiation, and show as thoughts
- A disliked relative nearby now costs less than a disliked stranger (-2 against -4, setting renamed so old saved values do not carry over)
- Initial scaffold
- RimWorld 1.7 (Waylith engine, .NET 10) as the default build target, with 1.6 still supported
- Bed choice scored in mood points from the game's own room thoughts, replacing arbitrary weights
- Royal title bedroom rules (personal bedroom, unmet requirements) and the Jealous trait in the score
- Room negotiation as a recreation activity, decided by a contested d20 roll (Social or Melee, opinion, title, time in colony)
- Mood effects for the loser (bullied out / gave it up for a friend) and the winner (got a better room), social experience for the asker
- Negotiation shown in the play log and in Interaction Bubbles
- Per-pair negotiation cooldown and a rematch block
- Player lock: setting a bed's owner with the Set Owner button locks it; nobody negotiates for that room and its owner does not negotiate
- Sleep in a chair as a last resort, with a milder penalty than the floor
- Scrollable settings window with a tooltip on every option and a reset button
- Compatibility: bed choice is a last-running postfix that defers to `IsValidBedFor`, so Hospitality and other bed mods keep working
- Negotiation notification is brief (1 second, adjustable) and fades out
- A bedless colonist takes the nicest bed they can reach; distance no longer counts against a bed
- Assign rooms window on the Assign tab: rooms with beds, hover to highlight, double-click to jump, assign owners per room
- Family: prefer sharing with family you get on with; negative relations want a room outside 35 tiles ("sleeping too close to" thought)
- Couples: prefer a room of just the two of them near family; "room far from family" thought; new couples are moved into a free room of their own
- God mode: Force negotiation button on a selected colonist
- Cribs column in the room list; animal beds, prisoner and slave beds, and Hospitality guest beds are left out
- Assign rooms button moved above the Name column so it no longer overlaps the policy buttons
- The colonist being asked stops and faces the asker while they talk, then carries on with what they were doing
- Negotiation notification stays 4 seconds by default (was 1)
- Compatibility: locks stored in the save instead of on every bed; patches applied individually and fail soft; features catch their own errors; compatibility summary in the options
- Fixes from a full code review: a couple already in their own room is no longer moved to a slightly worse one; a colonist who just moved ignores last night's sleep memory about the old room; forced negotiation checks the asker still has a bed; a colonist dozing in a chair stays in it; errors are reported once instead of every hour; the compatibility readout counts hooks that are really in place
- Performance: family relations are remembered for ten seconds per colonist, bed searches for a bedless colonist are remembered for three, the negotiation check is cached for a few seconds, searches do cheap checks before pathing ones, no text keys are built while searching, and the room list only draws visible rows
- A built-in timer shows how much time each feature uses (hover the compatibility line in the mod options)
