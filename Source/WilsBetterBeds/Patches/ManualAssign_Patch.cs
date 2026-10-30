using HarmonyLib;
using RimWorld;
using Verse;

namespace WilsBetterBeds.Patches
{
    /// <summary>
    /// When the player picks a bed's owner with the "Set owner" button (vanilla's window, or one
    /// reskinned by a mod such as Dubs Mint Menus, which still ends up in this method), the
    /// assignment is locked to that pawn. The player has made a decision, so nobody negotiates for the
    /// room and its owner does not negotiate either. Vanilla's own automatic claiming (a pawn lying
    /// down in a free bed) and this mod's swaps use ClaimBedIfNonMedical directly and never come
    /// through here.
    ///
    /// CompAssignableToPawn_Bed overrides TryAssignPawn without calling the base method, so the
    /// patch has to be on the override itself.
    /// </summary>
    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.TryAssignPawn))]
    public static class ManualAssign_Patch
    {
        static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn)
        {
            try
            {
                if (pawn == null) return;
                var bed = __instance.parent as Building_Bed;
                if (bed == null || bed.Medical) return;
                BedLocks.Lock(bed, pawn);
            }
            catch (System.Exception e) { SafeLog.Once("lock on assign", e); }
        }
    }

    /// <summary>
    /// Taking the owner off a bed releases the lock, so a pawn who later claims the same bed by
    /// sleeping in it does not find it locked by a decision the player has since undone.
    /// </summary>
    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.TryUnassignPawn))]
    public static class ManualUnassign_Patch
    {
        static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn)
        {
            try
            {
                var bed = __instance.parent as Building_Bed;
                if (bed != null && pawn != null) BedLocks.Release(bed, pawn);
            }
            catch (System.Exception e) { SafeLog.Once("release on unassign", e); }
        }
    }
}
