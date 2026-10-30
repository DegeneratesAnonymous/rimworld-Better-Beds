using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SmarterBeds.Patches
{
    /// <summary>
    /// Postfix on the full RestUtility.FindBedFor overload (the short one forwards to it).
    ///
    /// It is a postfix, running last, on purpose. Hospitality, Replace Stuff, Worksites Expanded and
    /// the gene frameworks all patch this method; letting them and vanilla decide first means their
    /// rules (guest beds, partner beds, genes that change where someone may sleep) are never skipped.
    /// We only step in when the answer is a free bed (or nothing) for a colonist with no bed, and
    /// swap it for the best one by mood. A bed somebody already owns, such as a partner's, is left alone.
    ///
    /// Uses __args instead of named parameters so a renamed parameter in a game update doesn't
    /// silently break the patch. Re-check the overload in ILSpy after updates.
    /// </summary>
    [HarmonyPatch]
    public static class FindBedFor_Patch
    {
        static MethodBase TargetMethod()
        {
            // The overload with the most parameters is the full one (the short ones forward to it).
            // If a game update removes them all there is nothing to patch, and the feature is skipped.
            return AccessTools.GetDeclaredMethods(typeof(RestUtility))
                .Where(m => m.Name == nameof(RestUtility.FindBedFor) && m.ReturnType == typeof(Building_Bed))
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();
        }

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPriority(Priority.Last)]
        static void Postfix(object[] __args, ref Building_Bed __result)
        {
            try
            {
                if (!SmarterBedsMod.Settings.enabled) return;

                var sleeper = __args.Length > 0 ? __args[0] as Pawn : null;
                var traveler = __args.Length > 1 ? __args[1] as Pawn : sleeper;

                if (!BedScorer.ShouldHandle(sleeper, traveler)) return;
                if (__result != null && __result.OwnersForReading.Count > 0) return; // a partner's bed, or similar

                Building_Bed bed;
                using (PerfWatch.Measure("bed choice")) bed = BedScorer.FindBest(sleeper, traveler);
                if (bed != null) __result = bed;
            }
            catch (System.Exception e)
            {
                // Whatever the game or another mod chose stands.
                SafeLog.Once("choosing a bed", e);
            }
        }
    }
}
