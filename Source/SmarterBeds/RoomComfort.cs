using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// How good a bed is for a pawn, measured in the unit the game itself uses: mood points.
    ///
    /// Vanilla already prices a night's sleep in mood: the "slept in a bedroom / barracks / outside"
    /// thoughts have a stage per room-impressiveness step (a mediocre bedroom is +1, a mediocre
    /// barracks is -4, sleeping outside is -4), and Royalty adds -6 each for a title-holder without a
    /// personal bedroom or with unmet bedroom requirements, while the Jealous trait adds -8. Scoring
    /// beds with those same numbers keeps this mod honest: a "better room" is exactly one that would
    /// make the pawn happier, and a gap of 3 really is about 3 mood.
    /// </summary>
    public static class RoomComfort
    {
        public static float Points(Building_Bed bed, Pawn pawn, SmarterBedsSettings s)
        {
            Room room = bed.GetRoom();
            bool outdoors = room == null || room.PsychologicallyOutdoors;

            float points = RoomMood(room, outdoors, pawn) * s.roomMoodWeight;
            // Rest effectiveness is a multiplier around 1.0; ten points per full 1.0 is about a point per 10%.
            points += (bed.GetStatValue(StatDefOf.BedRestEffectiveness) - 1f) * s.restEffectivenessWeight;
            points -= TitlePenalty(bed, room, outdoors, pawn, s);
            points -= JealousyPenalty(room, outdoors, pawn);
            return points;
        }

        /// <summary>
        /// The part of a bed's score that comes from the penalties negotiation can actually fix:
        /// a title-holder's bedroom penalties and the Jealous trait. Positive means penalised.
        /// </summary>
        public static float DebuffPenalty(Building_Bed bed, Pawn pawn, SmarterBedsSettings s)
        {
            Room room = bed.GetRoom();
            bool outdoors = room == null || room.PsychologicallyOutdoors;
            return TitlePenalty(bed, room, outdoors, pawn, s) + JealousyPenalty(room, outdoors, pawn);
        }

        /// <summary>The mood the vanilla "slept in ..." thought would give for this room.</summary>
        private static float RoomMood(Room room, bool outdoors, Pawn pawn)
        {
            if (outdoors) return StageMood(ThoughtDefOf.SleptOutside, 0f, flat: true);
            if (pawn.story?.traits != null && pawn.story.traits.HasTrait(TraitDefOf.Ascetic)) return 0f;

            float impressiveness = room.GetStat(RoomStatDefOf.Impressiveness);
            if (room.Role == RoomRoleDefOf.Bedroom) return StageMood(ThoughtDefOf.SleptInBedroom, impressiveness);
            if (room.Role == RoomRoleDefOf.Barracks) return StageMood(ThoughtDefOf.SleptInBarracks, impressiveness);
            return 0f;
        }

        private static float StageMood(ThoughtDef def, float impressiveness, bool flat = false)
        {
            if (def?.stages == null || def.stages.Count == 0) return 0f;
            int index = flat ? 0 : RoomStatDefOf.Impressiveness.GetScoreStageIndex(impressiveness);
            if (index < 0 || index >= def.stages.Count) return 0f;
            return def.stages[index]?.baseMoodEffect ?? 0f;
        }

        /// <summary>
        /// Royalty, mirroring the game: a title-holder needs a personal bedroom (nobody but a love
        /// partner sharing the room), and if they have one it has to meet the title's requirements.
        /// Each failure is the same -6 the game charges. Ascetics ignore both.
        /// </summary>
        public static float TitlePenalty(Building_Bed bed, Room room, bool outdoors, Pawn pawn, SmarterBedsSettings s)
        {
            var title = Requirements.TitleOf(pawn);
            if (title == null) return 0f;
            if (pawn.story?.traits != null && pawn.story.traits.HasTrait(TraitDefOf.Ascetic)) return 0f;

            if (outdoors || !HasPersonalBedroom(bed, room, pawn))
                return MoveColonyUtility.TitleAndRoleRequirementsGracePeriodActive ? 0f : s.unmetRequirementPenalty;

            if (Requirements.Unmet(pawn, room) > 0 && !title.RoomRequirementGracePeriodActive(pawn))
                return s.unmetRequirementPenalty;
            return 0f;
        }

        /// <summary>Same test as the game's HasPersonalBedroom, but for a bed the pawn does not own yet.</summary>
        private static bool HasPersonalBedroom(Building_Bed bed, Room room, Pawn pawn)
        {
            var loveCluster = pawn.GetLoveCluster();
            foreach (var other in room.ContainedBeds)
            {
                if (other == bed) continue;
                foreach (var owner in other.OwnersForReading)
                {
                    if (owner != pawn && !owner.RaceProps.Animal && !loveCluster.Contains(owner)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The Jealous trait: unhappy when another colonist owns a room at least 10% more impressive.
        /// Same comparison the game's own thought uses.
        /// </summary>
        public static float JealousyPenalty(Room room, bool outdoors, Pawn pawn)
        {
            if (pawn.story?.traits == null || !pawn.story.traits.HasTrait(TraitDefOf.Jealous)) return 0f;
            float own = outdoors ? 0f : room.GetStat(RoomStatDefOf.Impressiveness);

            foreach (var other in pawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (other == pawn || other.HostFaction != null || other.ownership == null) continue;
                Room theirs = other.ownership.OwnedRoom;
                if (theirs == null || theirs == room) continue;
                if (theirs.GetStat(RoomStatDefOf.Impressiveness) - own >= System.Math.Abs(own * 0.1f)) return JealousMood();
            }
            return 0f;
        }

        private static float JealousMood()
        {
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("Jealous");
            float v = def?.stages != null && def.stages.Count > 0 ? def.stages[0].baseMoodEffect : -8f;
            return -v; // a penalty is positive here
        }
    }
}
