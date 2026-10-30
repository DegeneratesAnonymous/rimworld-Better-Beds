using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace SmarterBeds
{
    /// <summary>
    /// Visitors: people who are on the map without belonging to the colony, and what they sleep in.
    ///
    /// Hospitality guests have their own bed system (rent, paying, guest beds), which is left alone.
    /// Everyone else who visits, such as spaceport visitors and trade caravans, is known to the game as
    /// an ordinary neutral pawn, and Hospitality's guest beds refuse them, so they lie on the floor
    /// however much silver they carry. These helpers recognise them so they can rent a guest bed too.
    /// </summary>
    public static class Visitors
    {
        private static readonly Func<Pawn, bool> HospitalityIsGuest = MakeIsGuest();
        private static readonly System.Reflection.MethodInfo GetRentalFee = AccessTools.Method("Hospitality.Building_GuestBed:GetRentalFee");
        private static readonly Dictionary<Type, bool> guestBedTypes = new Dictionary<Type, bool>();

        private static Func<Pawn, bool> MakeIsGuest()
        {
            var method = AccessTools.Method("Hospitality.Utilities.GuestUtility:IsGuest");
            if (method == null) return null;
            try { return (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), method); }
            catch { return null; }
        }

        /// <summary>A guest of the colony in Hospitality's sense. Their beds, rent and rules are Hospitality's.</summary>
        public static bool IsHospitalityGuest(Pawn p)
        {
            try { return HospitalityIsGuest != null && HospitalityIsGuest(p); }
            catch { return false; }
        }

        /// <summary>A friendly or neutral visitor on a colony map who is not a Hospitality guest: spaceport visitors, caravans.</summary>
        public static bool IsPlainVisitor(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null || !p.Map.IsPlayerHome || p.Dead) return false;
            if (!p.RaceProps.Humanlike || p.IsPrisoner || p.IsSlave || p.HostFaction != null) return false;
            var faction = p.Faction;
            if (faction == null || faction == Faction.OfPlayer || faction.HostileTo(Faction.OfPlayer)) return false;
            if (p.GetLord() == null) return false;
            return !IsHospitalityGuest(p);
        }

        public static bool IsGuestBed(Building_Bed bed)
        {
            if (bed == null) return false;
            var type = bed.GetType();
            if (!guestBedTypes.TryGetValue(type, out bool isGuestBed))
                guestBedTypes[type] = isGuestBed = type.Name.IndexOf("GuestBed", StringComparison.OrdinalIgnoreCase) >= 0;
            return isGuestBed;
        }

        public static int Silver(Pawn p) =>
            p?.inventory?.innerContainer?.FirstOrDefault(t => t.def == ThingDefOf.Silver)?.stackCount ?? 0;

        public static int Rent(Building_Bed bed)
        {
            try { return GetRentalFee == null ? 0 : Convert.ToInt32(GetRentalFee.Invoke(bed, null)); }
            catch { return 0; }
        }

        /// <summary>Whether this visitor may sleep in this guest bed: it has a free space and they can pay the rent.</summary>
        public static bool CanRent(Building_Bed bed, Pawn visitor)
        {
            if (bed == null || !bed.Spawned || bed.Destroyed || bed.IsBurning() || bed.Medical || bed.ForPrisoners) return false;
            if (!RestUtility.CanUseBedEver(visitor, bed.def)) return false;
            var owners = bed.OwnersForReading;
            if (owners.Contains(visitor)) return true; // already paid for
            if (owners.Count >= bed.SleepingSlotsCount) return false;
            return Rent(bed) <= Silver(visitor);
        }

        /// <summary>The visitor has just claimed the bed: they leave the rent beside it, as a Hospitality guest does.</summary>
        public static void PayRent(Building_Bed bed, Pawn visitor)
        {
            int fee = Rent(bed);
            if (fee <= 0) return;
            var silver = visitor.inventory.innerContainer.FirstOrDefault(t => t.def == ThingDefOf.Silver);
            if (silver == null || silver.stackCount < fee) return;
            visitor.inventory.innerContainer.TryDrop(silver, bed.Position, bed.Map, ThingPlaceMode.Near, fee, out Thing _);
        }
    }
}
