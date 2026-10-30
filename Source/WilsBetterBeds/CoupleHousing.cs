using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// When a colonist has a partner and the two do not live in a room of their own, find them one.
    ///
    /// Looks for a room where nobody else owns a bed and there is space for both (a double bed, or
    /// two beds), scores it the way a bed is scored for each of them (including how close it is to
    /// their family, since a couple far from family is unhappy), and moves them in if it is clearly
    /// better than what they have. Nobody is evicted: only free beds, or the couple's own, are used.
    /// A bed whose owner the player set is left alone, since that is the player's decision.
    /// </summary>
    public static class CoupleHousing
    {
        private const float MinImprovement = 1f;
        private const float NoBedMood = -4f; // "slept on the ground"
        private const float RetryDaysWhenNothingFits = 1f;

        /// <summary>
        /// Looks at one colonist: if they are half of a couple with a sleep penalty and no room of their own,
        /// tries to house the two of them. Called for each colonist in turn through the hour. Returns true if
        /// they moved.
        /// </summary>
        public static bool Step(Pawn a, GameComponent_BedNegotiation comp)
        {
            var s = WilsBetterBedsMod.Settings;
            try
            {
                if (!Family.IsAdultColonist(a) || !Family.HasSingleLover(a)) return false; // most colonists stop here, cheaply
                Family.Refresh(a); // this runs once an hour, so read the relations afresh
                var b = Family.SoleLover(a);
                if (b == null) return false;
                Family.Refresh(b);
                if (Family.SoleLover(b) != a) return false;
                if (a.thingIDNumber > b.thingIDNumber) return false; // once per pair
                if (!Family.IsAdultColonist(b) || b.Map != a.Map) return false;
                if (!a.Awake() || !b.Awake()) return false;
                // Content, so nothing to look for: only a couple with a sleep penalty are moved.
                if (!comp.HasRoomDebuff(a) && !comp.HasRoomDebuff(b)) return false;
                // Already in a room of their own: only look again if family distance is still a problem.
                if (Family.IsInCoupleRoom(a) && !FamilyProblem(a, b, s)) return false;
                if (comp.CoupleOnCooldown(a, b)) return false;

                bool moved;
                using (PerfWatch.Measure("couple housing")) moved = TryHouse(a, b, s);
                comp.StartCoupleCooldown(a, b, moved ? s.negotiationCooldownDays : RetryDaysWhenNothingFits);
                return moved;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("couple housing", e);
                return false;
            }
        }

        private static bool FamilyProblem(Pawn a, Pawn b, WilsBetterBedsSettings s)
        {
            var bedA = Family.BedOf(a);
            var bedB = Family.BedOf(b);
            return (bedA != null && RoomComfort.FamilyPenalty(bedA, a, s) > 0f)
                || (bedB != null && RoomComfort.FamilyPenalty(bedB, b, s) > 0f);
        }

        private struct Plan
        {
            public Building_Bed bedA;
            public Building_Bed bedB;
            public float value;
        }

        private static bool TryHouse(Pawn a, Pawn b, WilsBetterBedsSettings s)
        {
            var oldA = Family.BedOf(a);
            var oldB = Family.BedOf(b);
            if (BedLocks.IsLocked(oldA, a) || BedLocks.IsLocked(oldB, b)) return false; // the player decided

            // The candidates below include the couple-room bonus, so what they have now has to include it
            // too when they already live in a room of their own, or they could "move up" to a worse room.
            float current = ValueNow(a, oldA, s) + ValueNow(b, oldB, s) + (Family.IsInCoupleRoom(a) ? 2f * s.coupleRoomBonus : 0f);

            Plan? best = null;
            foreach (var room in RoomsWithBeds(a.Map))
            {
                // Someone else lives here.
                if (room.ContainedBeds.Any(bed => bed.OwnersForReading.Any(o => o != a && o != b))) continue;

                var usable = room.ContainedBeds.Where(bed => UsableByBoth(bed, a, b)).ToList();
                if (usable.Sum(bed => bed.SleepingSlotsCount) < 2) continue;

                // Score each bed once per person, not once per pair of beds.
                var forA = usable.Select(bed => RoomComfort.Points(bed, a, s, company: false)).ToList();
                var forB = usable.Select(bed => RoomComfort.Points(bed, b, s, company: false)).ToList();
                for (int i = 0; i < usable.Count; i++)
                {
                    for (int j = 0; j < usable.Count; j++)
                    {
                        var bedA = usable[i];
                        var bedB = usable[j];
                        if (bedA == bedB && bedA.SleepingSlotsCount < 2) continue;
                        float value = forA[i] + forB[j] + 2f * s.coupleRoomBonus;
                        if (best == null || value > best.Value.value)
                            best = new Plan { bedA = bedA, bedB = bedB, value = value };
                    }
                }
            }

            if (best == null || best.Value.value < current + MinImprovement) return false;
            return Move(a, b, oldA, oldB, best.Value, s);
        }

        /// <summary>What the pawn has now, in the same units (their company is not counted: they are not yet a couple room).</summary>
        private static float ValueNow(Pawn p, Building_Bed bed, WilsBetterBedsSettings s) =>
            bed == null ? NoBedMood : RoomComfort.Points(bed, p, s, company: false);

        private static IEnumerable<Room> RoomsWithBeds(Map map)
        {
            return map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                .Where(bed => bed.Spawned && !bed.Medical && bed.ForColonists)
                .Select(bed => bed.GetRoom())
                .Where(room => room != null && !room.PsychologicallyOutdoors)
                .Distinct();
        }

        /// <summary>A bed that is free (and valid for both), or is already one of the couple's.</summary>
        private static bool UsableByBoth(Building_Bed bed, Pawn a, Pawn b)
        {
            if (!bed.Spawned || bed.Medical || !bed.ForColonists) return false;
            if (bed.OwnersForReading.Count > 0)
                return bed.OwnersForReading.All(o => o == a || o == b);
            return BedScorer.IsCandidate(bed, a, a) && BedScorer.IsCandidate(bed, b, b);
        }

        private static bool Move(Pawn a, Pawn b, Building_Bed oldA, Building_Bed oldB, Plan plan, WilsBetterBedsSettings s)
        {
            a.ownership.UnclaimBed();
            b.ownership.UnclaimBed();

            bool ok = a.ownership.ClaimBedIfNonMedical(plan.bedA) && b.ownership.ClaimBedIfNonMedical(plan.bedB);
            if (!ok)
            {
                // Put everyone back exactly as they were.
                a.ownership.UnclaimBed();
                b.ownership.UnclaimBed();
                if (oldA != null) a.ownership.ClaimBedIfNonMedical(oldA);
                if (oldB != null) b.ownership.ClaimBedIfNonMedical(oldB);
                return false;
            }

            var negotiation = GameComponent_BedNegotiation.Instance;
            negotiation?.NoteMoved(a);
            negotiation?.NoteMoved(b);

            if (s.verboseLogging)
                Log.Message($"[Wil's Better Beds] couple: {a.LabelShort} and {b.LabelShort} moved into a room of their own at {plan.bedA.Position}");

            if (s.negotiationMessages)
                BriefMessage.Show("WilsBetterBeds_CoupleMoved".Translate(a.LabelShort, b.LabelShort), new LookTargets(a, b), s.messageSeconds);
            return true;
        }
    }
}
