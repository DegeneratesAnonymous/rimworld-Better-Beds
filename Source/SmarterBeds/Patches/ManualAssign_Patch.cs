using HarmonyLib;
using RimWorld;
using Verse;

namespace SmarterBeds.Patches
{
    /// <summary>
    /// When the player assigns a pawn to a bed through the assign window (vanilla's, or one reskinned
    /// by a mod such as Dubs Mint Menus, which still goes through this method), lock that bed for the
    /// pawn. The player has made a decision, so nobody negotiates for the room and its owner does not
    /// negotiate either. Vanilla's own automatic claiming (a pawn lying down in a free bed) and this
    /// mod's swaps use ClaimBedIfNonMedical directly and never come through here.
    ///
    /// CompAssignableToPawn_Bed overrides TryAssignPawn without calling the base method, so the patch
    /// has to be on the override itself.
    /// </summary>
    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.TryAssignPawn))]
    public static class ManualAssign_Patch
    {
        static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn)
        {
            if (!SmarterBedsMod.Settings.autoLockManualAssignments || pawn == null) return;
            var bed = __instance.parent as Building_Bed;
            if (bed == null || bed.Medical) return;

            var comp = bed.GetComp<CompOverflowBed>();
            if (comp != null) comp.lockedOwnerId = pawn.thingIDNumber;
        }
    }
}
