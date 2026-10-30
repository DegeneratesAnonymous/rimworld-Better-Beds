using HarmonyLib;
using RimWorld;
using Verse;

namespace WilsBetterBeds.Patches
{
    /// <summary>
    /// A line in a bed's inspect pane when its owner was set by the player and is therefore locked
    /// against negotiation. A postfix on the text, so it adds nothing to the bed itself.
    /// </summary>
    [HarmonyPatch(typeof(Building_Bed), nameof(Building_Bed.GetInspectString))]
    public static class BedInspect_Patch
    {
        static void Postfix(Building_Bed __instance, ref string __result)
        {
            try
            {
                if (!BedLocks.IsLockedForOwner(__instance)) return;
                string line = "WilsBetterBeds_LockedInspect".Translate();
                __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
            }
            catch (System.Exception e) { SafeLog.Once("bed inspect line", e); }
        }
    }
}
