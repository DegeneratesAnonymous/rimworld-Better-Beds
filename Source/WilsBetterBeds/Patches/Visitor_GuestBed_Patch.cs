using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WilsBetterBeds.Patches
{
    /// <summary>
    /// Lets a visitor who is not a Hospitality guest (spaceport visitors, trade caravans) sleep in one of
    /// the colony's guest beds if they can pay for it. Hospitality marks guest beds as unusable for
    /// anyone who is not one of its own guests, so those visitors fell back to the floor. This runs last
    /// and only ever turns "no" into "yes" for that one case: a visitor, a guest bed with a free space,
    /// and enough silver for the rent. A visitor who cannot pay is left as before (and sleeps in a chair).
    /// </summary>
    [HarmonyPatch]
    public static class Visitor_IsValidBedFor_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(RestUtility))
                .Where(m => m.Name == "IsValidBedFor")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault();

        static bool Prepare() => TargetMethod() != null;

        [HarmonyPriority(Priority.Last)]
        static void Postfix(object[] __args, ref bool __result)
        {
            if (__result) return;
            try
            {
                var s = WilsBetterBedsMod.Settings;
                if (!s.enabled || !s.visitorsUseGuestBeds || __args.Length < 2) return;
                var bed = __args[0] as Building_Bed;
                var sleeper = __args[1] as Pawn;
                if (!Visitors.IsGuestBed(bed) || !Visitors.IsPlainVisitor(sleeper)) return;
                __result = Visitors.CanRent(bed, sleeper);
            }
            catch (System.Exception e) { SafeLog.Once("letting a visitor rent a guest bed", e); }
        }
    }

    /// <summary>When a visitor takes a guest bed for the first time they pay the rent, as a Hospitality guest does.</summary>
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.ClaimBedIfNonMedical))]
    public static class Visitor_ClaimBed_Patch
    {
        static void Prefix(Pawn_Ownership __instance, Building_Bed newBed, out bool __state)
        {
            __state = newBed != null && __instance.OwnedBed == newBed; // already theirs: nothing more to pay
        }

        static void Postfix(Pawn_Ownership __instance, Building_Bed newBed, bool __result, bool __state, Pawn ___pawn)
        {
            if (!__result || __state) return;
            try
            {
                var s = WilsBetterBedsMod.Settings;
                if (!s.enabled || !s.visitorsUseGuestBeds) return;
                if (Visitors.IsGuestBed(newBed) && Visitors.IsPlainVisitor(___pawn)) Visitors.PayRent(newBed, ___pawn);
            }
            catch (System.Exception e) { SafeLog.Once("a visitor paying rent", e); }
        }
    }
}
