using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SmarterBeds.Patches
{
    /// <summary>
    /// Prefix on the full RestUtility.FindBedFor overload (the short one forwards to it).
    /// Uses __args instead of named parameters so a renamed parameter in a game update
    /// doesn't silently break the patch. Re-check the overload in ILSpy after updates.
    /// </summary>
    [HarmonyPatch]
    public static class FindBedFor_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(RestUtility))
                .Where(m => m.Name == nameof(RestUtility.FindBedFor) && m.ReturnType == typeof(Building_Bed))
                .OrderByDescending(m => m.GetParameters().Length)
                .First();
        }

        static bool Prefix(object[] __args, ref Building_Bed __result)
        {
            if (!SmarterBedsMod.Settings.enabled) return true;

            var sleeper = __args.Length > 0 ? __args[0] as Pawn : null;
            var traveler = __args.Length > 1 ? __args[1] as Pawn : sleeper;

            if (!BedScorer.ShouldHandle(sleeper, traveler)) return true;

            var bed = BedScorer.FindBest(sleeper, traveler);
            if (bed == null) return true; // nothing suitable: let vanilla decide

            __result = bed;
            return false;
        }

        /// <summary>
        /// A pawn who already owns a bed is going to sleep. If something is wrong with how they
        /// sleep, let them negotiate for a better room; on a swap they head straight to the new bed.
        /// </summary>
        static void Postfix(object[] __args, ref Building_Bed __result)
        {
            if (!SmarterBedsMod.Settings.enabled) return;

            var sleeper = __args.Length > 0 ? __args[0] as Pawn : null;
            var traveler = __args.Length > 1 ? __args[1] as Pawn : sleeper;
            if (sleeper == null || sleeper != traveler) return;               // only a pawn seeking its own rest
            if (sleeper.ownership?.OwnedBed == null || __result != sleeper.ownership.OwnedBed) return;

            var newBed = GameComponent_BedNegotiation.Instance?.TryNegotiateFor(sleeper);
            if (newBed != null) __result = newBed;
        }
    }
}
