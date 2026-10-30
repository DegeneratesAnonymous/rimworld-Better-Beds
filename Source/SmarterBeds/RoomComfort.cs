using System.Linq;
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
        /// <param name="company">
        /// Include who the pawn would be sharing the room with (a couple on their own, family together).
        /// Off when the caller is deciding on rooms for two people at once and will count that itself.
        /// </param>
        /// <param name="leaving">
        /// Someone who is about to give this bed up (the colonist being asked in a negotiation), so they
        /// are not counted as a roommate or as family living nearby.
        /// </param>
        public static float Points(Building_Bed bed, Pawn pawn, SmarterBedsSettings s, bool company = true, Pawn leaving = null)
        {
            Room room = bed.GetRoom();
            bool outdoors = room == null || room.PsychologicallyOutdoors;

            float points = RoomMood(room, outdoors, pawn) * s.roomMoodWeight;
            // Rest effectiveness is a multiplier around 1.0; ten points per full 1.0 is about a point per 10%.
            points += (bed.GetStatValue(StatDefOf.BedRestEffectiveness) - 1f) * s.restEffectivenessWeight;
            points -= TitlePenalty(bed, room, outdoors, pawn, s);
            points -= JealousyPenalty(room, outdoors, pawn);
            points -= FamilyPenalty(bed, pawn, s, leaving);
            points -= NeighborPenalty(bed, pawn, s, leaving);
            points -= PrisonerPenalty(bed, pawn, s);
            points += NeighborMood(bed, pawn, s, leaving);
            if (company) points += CompanyMood(bed, room, outdoors, pawn, s, leaving);
            return points;
        }

        /// <summary>
        /// The part of a bed's score that comes from penalties negotiation can actually fix: a
        /// title-holder's bedroom penalties, the Jealous trait, and being too close to (or, for a
        /// couple, too far from) family. Positive means penalised.
        /// </summary>
        public static float DebuffPenalty(Building_Bed bed, Pawn pawn, SmarterBedsSettings s, Pawn leaving = null)
        {
            Room room = bed.GetRoom();
            bool outdoors = room == null || room.PsychologicallyOutdoors;
            return TitlePenalty(bed, room, outdoors, pawn, s) + JealousyPenalty(room, outdoors, pawn) + FamilyPenalty(bed, pawn, s, leaving) + NeighborPenalty(bed, pawn, s, leaving) + PrisonerPenalty(bed, pawn, s);
        }

        /// <summary>
        /// Distance from family, which is what the two family thoughts describe:
        /// - negative relations with a relative: unhappy with a bed inside the family radius of theirs;
        /// - a couple with family in the colony: unhappy with a bed outside the radius of all of them.
        /// Mirrors the "too close to a disliked relative" and "room far from family" thoughts.
        /// </summary>
        public static float FamilyPenalty(Building_Bed bed, Pawn pawn, SmarterBedsSettings s, Pawn leaving = null)
        {
            if (!s.familyEnabled || !Family.IsAdultColonist(pawn)) return 0f;

            float penalty = 0f;
            bool anyFriendly = false, anyFriendlyNear = false;
            foreach (var relative in Family.CloseRelatives(pawn))
            {
                if (relative == leaving) continue;
                var theirs = Family.BedOf(relative);
                if (theirs == null) continue;
                bool near = theirs == bed || Family.Distance(bed, theirs) <= s.familyRadius;
                if (Family.IsHostile(pawn, relative, s))
                {
                    if (near) penalty += s.familyHostilePenalty;
                }
                else
                {
                    anyFriendly = true;
                    if (near) anyFriendlyNear = true;
                }
            }

            // A couple with family around, none of it within range: "room far from family".
            if (anyFriendly && !anyFriendlyNear && Family.SoleLover(pawn) != null) penalty += s.familyFarPenalty;
            return penalty;
        }

        /// <summary>
        /// Colonists the pawn dislikes (and who are not family) sleeping within the family distance.
        /// Counted for up to two of them, and worth more than the same dislike of a relative, since
        /// family is put up with.
        /// </summary>
        public static float NeighborPenalty(Building_Bed bed, Pawn pawn, SmarterBedsSettings s, Pawn leaving = null)
        {
            if (!s.neighborsEnabled || !Family.IsAdultColonist(pawn)) return 0f;
            return Family.CountNear(pawn, Family.HostileNeighbors(pawn), bed, s, leaving, 2) * s.neighborHostilePenalty;
        }

        /// <summary>Sleeping within the family distance of a bed that has a prisoner in it. Counted once, however many there are.</summary>
        public static float PrisonerPenalty(Building_Bed bed, Pawn pawn, SmarterBedsSettings s)
        {
            if (!s.neighborsEnabled || s.prisonerNeighborPenalty <= 0f || !Family.IsAdultColonist(pawn)) return 0f;
            return Family.PrisonersNear(bed, s) ? s.prisonerNeighborPenalty : 0f;
        }

        /// <summary>The comfort of sleeping near people of your own ideoligion and your own kind.</summary>
        public static float NeighborMood(Building_Bed bed, Pawn pawn, SmarterBedsSettings s, Pawn leaving = null)
        {
            if (!s.neighborsEnabled || !Family.IsAdultColonist(pawn)) return 0f;
            float mood = 0f;
            if (Family.CountNear(pawn, Family.SameIdeoNeighbors(pawn), bed, s, leaving, 1) > 0) mood += s.ideoNeighborBonus;
            if (Family.CountNear(pawn, Family.SameGeneNeighbors(pawn), bed, s, leaving, 1) > 0) mood += s.geneNeighborBonus;
            return mood;
        }

        /// <summary>
        /// Who the pawn would be sharing the room with, in mood points.
        /// - A pawn with a partner is happiest in a room that holds only the two of them, and a little
        ///   less so in an empty room their partner could move into; sharing with anyone else costs.
        /// - Family sharing a room is a small plus, for pawns on good terms.
        /// </summary>
        private static float CompanyMood(Building_Bed bed, Room room, bool outdoors, Pawn pawn, SmarterBedsSettings s, Pawn leaving)
        {
            if (!s.familyEnabled || outdoors || !Family.IsAdultColonist(pawn)) return 0f;

            float mood = 0f;
            var lovers = Family.Lovers(pawn);
            var others = new System.Collections.Generic.List<Pawn>();
            foreach (var other in room.ContainedBeds)
            {
                foreach (var owner in other.OwnersForReading)
                {
                    if (owner != pawn && owner != leaving && !owner.RaceProps.Animal && !others.Contains(owner)) others.Add(owner);
                }
            }

            if (lovers.Count > 0)
            {
                bool anyoneElse = others.Any(o => !lovers.Contains(o));
                bool loverHere = others.Any(o => lovers.Contains(o));
                if (anyoneElse) mood -= s.coupleSharePenalty;
                else mood += loverHere ? s.coupleRoomBonus : s.coupleRoomBonus * 0.5f;
            }

            int family = 0;
            foreach (var relative in Family.CloseRelatives(pawn))
            {
                if (others.Contains(relative) && !Family.IsHostile(pawn, relative, s)) family++;
            }
            mood += System.Math.Min(family, 2) * s.familyShareBonus;
            return mood;
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
            if (!s.titleRulesEnabled) return 0f;
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
            if (!SmarterBedsMod.Settings.jealousyEnabled) return 0f;
            if (pawn.story?.traits == null || !pawn.story.traits.HasTrait(TraitDefOf.Jealous)) return 0f;
            if (pawn.Map == null) return 0f;
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
