using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WilsBetterBeds
{
    /// <summary>
    /// RimMarket (forti.rimmarket) lets any bed be an inn bed: caravan merchants, their guards and shoppers pay for the night.
    /// An inn bed is the market's, not a colonist's, so:
    ///  - no colonist is ever given one (bed choice, free-bed moves, couple housing, negotiation, the room list);
    ///  - a visitor who may lodge there but is about to lie down on the floor or in a chair is first offered an inn bed, by asking
    ///    RimMarket's own lodging job giver (which does the paying, the rating and the rest); a guest bed (Hospitality) is still
    ///    tried first by the code that already handles those, and an inn bed is tried before a chair.
    /// Nothing from the other mod is referenced at compile time: it is found by reflection, and if anything about it looks
    /// different the feature is simply off (colonists would then be kept out of inn beds by RimMarket's own check alone).
    /// Read from the 1.7 build of that mod; not run in game.
    /// </summary>
    public static class InnBedCompat
    {
        private const string PackageId = "forti.rimmarket";
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static bool looked, installed;
        private static MethodInfo isInn, tryGiveJob, mayLodge;
        private static object lodgingGiver;

        public static bool Installed { get { Look(); return installed; } }

        private static void Look()
        {
            if (looked) return;
            looked = true;
            try
            {
                if (ModLister.GetActiveModWithIdentifier(PackageId, true) == null) return;
                isInn = GenTypes.GetTypeInAnyAssembly("RimMarket.Inn.InnBeds")?.GetMethod("IsInn", Any, null, new[] { typeof(Thing) }, null);
                var giver = GenTypes.GetTypeInAnyAssembly("RimMarket.Inn.JobGiver_SleepAtInn");
                tryGiveJob = giver?.GetMethod("TryGiveJob", Any, null, new[] { typeof(Pawn) }, null);
                mayLodge = GenTypes.GetTypeInAnyAssembly("RimMarket.Inn.InnUtility")?.GetMethod("MayLodge", Any, null, new[] { typeof(Pawn) }, null);
                if (giver != null) lodgingGiver = Activator.CreateInstance(giver);
                installed = isInn != null;
                if (installed) Log.Message("[Wil's Better Beds] RimMarket detected: inn beds are kept out of colonists' hands, and visitors who may lodge are offered one before a chair.");
                else Log.Warning("[Wil's Better Beds] RimMarket is installed but its inn bed API was not as expected; inn beds are not recognised.");
            }
            catch (Exception e) { SafeLog.Once("looking for RimMarket", e); installed = false; }
        }

        /// <summary>True when RimMarket has made this bed an inn bed.</summary>
        public static bool IsInn(Building_Bed bed)
        {
            if (bed == null || !Installed) return false;
            try { return (bool)isInn.Invoke(null, new object[] { bed }); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// A job taking the pawn to a free inn bed they can pay for, or null. This is RimMarket's own lodging job giver, so it applies
        /// all its rules (who may lodge, rest and the time of day, silver, price tiers, who may be bumped) and does the charging.
        /// </summary>
        public static Job LodgingJob(Pawn pawn)
        {
            if (!Installed || tryGiveJob == null || lodgingGiver == null || pawn == null || pawn.IsColonist) return null;
            try
            {
                if (mayLodge != null && !(bool)mayLodge.Invoke(null, new object[] { pawn })) return null;
                return tryGiveJob.Invoke(lodgingGiver, new object[] { pawn }) as Job;
            }
            catch (Exception e) { SafeLog.Once("offering a visitor an inn bed", e); return null; }
        }
    }
}
