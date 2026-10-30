using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WilsBetterBeds
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
        public static float Points(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s, bool company = true, Pawn leaving = null)
        {
            Room room = bed.GetRoom();
            bool outdoors = room == null || room.PsychologicallyOutdoors;

            float points = RoomMood(room, outdoors, pawn) * s.roomMoodWeight;
            // Rest effectiveness is a multiplier around 1.0; ten points per full 1.0 is about a point per 10%.
            points += (bed.GetStatValue(StatDefOf.BedRestEffectiveness) - 1f) * s.restEffectivenessWeight;
            points -= TitlePenalty(bed, room, outdoors, pawn, s);
            points -= JealousyPenalty(room, outdoors, pawn);
            points += TraitMood(room, outdoors, pawn, s);
            points -= TemperaturePenalty(bed, pawn, s);
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
        public static float DebuffPenalty(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s, Pawn leaving = null)
        {
            Room room = bed.GetRoom();
            bool outdoors = room == null || room.PsychologicallyOutdoors;
            return TitlePenalty(bed, room, outdoors, pawn, s) + JealousyPenalty(room, outdoors, pawn) - TraitMood(room, outdoors, pawn, s) + TemperaturePenalty(bed, pawn, s) + FamilyPenalty(bed, pawn, s, leaving) + NeighborPenalty(bed, pawn, s, leaving) + PrisonerPenalty(bed, pawn, s);
        }

        /// <summary>
        /// Distance from family, which is what the two family thoughts describe:
        /// - negative relations with a relative: unhappy with a bed inside the family radius of theirs;
        /// - a couple with family in the colony: unhappy with a bed outside the radius of all of them.
        /// Mirrors the "too close to a disliked relative" and "room far from family" thoughts.
        /// </summary>
        public static float FamilyPenalty(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s, Pawn leaving = null)
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
        public static float NeighborPenalty(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s, Pawn leaving = null)
        {
            if (!s.neighborsEnabled || !Family.IsAdultColonist(pawn)) return 0f;
            return Family.CountNear(pawn, Family.HostileNeighbors(pawn), bed, s, leaving, 2) * s.neighborHostilePenalty;
        }

        /// <summary>Sleeping within the family distance of a bed that has a prisoner in it. Counted once, however many there are.</summary>
        public static float PrisonerPenalty(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s)
        {
            if (!s.neighborsEnabled || s.prisonerNeighborPenalty <= 0f || !Family.IsAdultColonist(pawn)) return 0f;
            return Family.PrisonersNear(bed, s) ? s.prisonerNeighborPenalty : 0f;
        }

        /// <summary>The comfort of sleeping near people of your own ideoligion and your own kind.</summary>
        public static float NeighborMood(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s, Pawn leaving = null)
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
        private static float CompanyMood(Building_Bed bed, Room room, bool outdoors, Pawn pawn, WilsBetterBedsSettings s, Pawn leaving)
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

        private static ThoughtDef greedyThought, asceticThought;
        private static bool traitThoughtsLoaded;

        /// <summary>
        /// The mood the Greedy and Ascetic traits give for this room, read from the game's own thoughts
        /// ("greedy for impressive bedroom": -4 to -8 unless the room is impressive; "ascetic has ... bedroom":
        /// +3 to +5 for a humble one, -5 for an impressive one), by the room's impressiveness stage.
        /// Positive is good for the sleeper.
        /// </summary>
        public static float TraitMood(Room room, bool outdoors, Pawn pawn, WilsBetterBedsSettings s)
        {
            if (!s.traitsEnabled || pawn.story?.traits == null) return 0f;
            if (!traitThoughtsLoaded)
            {
                greedyThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Greedy");
                asceticThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Ascetic");
                traitThoughtsLoaded = true;
            }

            float mood = 0f;
            bool hasRoom = room != null && !outdoors;
            int stage = hasRoom ? RoomStatDefOf.Impressiveness.GetScoreStageIndex(room.GetStat(RoomStatDefOf.Impressiveness)) : -1;

            // The game's workers: Greedy has no room at stage 0 and counts the room's stage from 1; Ascetic has nothing without a room.
            if (greedyThought != null && pawn.story.traits.HasTrait(TraitDefOf.Greedy))
                mood += StageEffect(greedyThought, stage + 1);
            if (asceticThought != null && hasRoom && pawn.story.traits.HasTrait(TraitDefOf.Ascetic))
                mood += StageEffect(asceticThought, stage);
            return mood;
        }

        private static float StageEffect(ThoughtDef def, int index)
        {
            if (def?.stages == null || index < 0 || index >= def.stages.Count) return 0f;
            return def.stages[index]?.baseMoodEffect ?? 0f;
        }

        /// <summary>
        /// How big an upgrade, in mood points, this colonist asks for when nothing is wrong with their room.
        /// High expectations (an ideoligion role, a royal title, a rich colony) make them pickier, by the
        /// "expectations" setting: at 100%, a colonist with supreme expectations asks for about a fifth of it.
        /// </summary>
        public static float UpgradeThreshold(Pawn pawn, WilsBetterBedsSettings s)
        {
            float threshold = s.qualityNegotiationThreshold;
            if (s.expectationWeight <= 0f) return threshold;
            try
            {
                var expectation = ExpectationsUtility.CurrentExpectationFor(pawn);
                if (expectation == null) return threshold;
                float level = Mathf.Clamp01(expectation.order / 9f); // from extremely low (0) to royal (9)
                return Mathf.Max(0.5f, threshold * (1f - s.expectationWeight * level));
            }
            catch (System.Exception) { return threshold; }
        }

        /// <summary>
        /// The game's own test for "slept in the cold" and "slept in the heat": the temperature where the bed
        /// stands against the sleeper's comfortable range (their race, plus any genes that move it). Either
        /// is -4 in the game, which is the default here. It is the temperature now, so a room that is
        /// heated, or cooled, counts as the better room, and one that has gone cold with the weather does not.
        /// </summary>
        public static float TemperaturePenalty(Building_Bed bed, Pawn pawn, WilsBetterBedsSettings s)
        {
            if (!s.temperatureEnabled || s.temperaturePenalty <= 0f || bed == null || bed.Map == null) return 0f;

            float min = pawn.def.GetStatValueAbstract(StatDefOf.ComfyTemperatureMin);
            float max = pawn.def.GetStatValueAbstract(StatDefOf.ComfyTemperatureMax);
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (!gene.Active) continue;
                    min += gene.def.statOffsets.GetStatOffsetFromList(StatDefOf.ComfyTemperatureMin);
                    max += gene.def.statOffsets.GetStatOffsetFromList(StatDefOf.ComfyTemperatureMax);
                }
            }

            float temperature = bed.AmbientTemperature;
            return temperature < min || temperature > max ? s.temperaturePenalty : 0f;
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
        public static float TitlePenalty(Building_Bed bed, Room room, bool outdoors, Pawn pawn, WilsBetterBedsSettings s)
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
            if (!WilsBetterBedsMod.Settings.jealousyEnabled) return 0f;
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
