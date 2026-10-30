using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SmarterBeds.Patches
{
    /// <summary>
    /// Hospitality (Continued): a guest picks a guest bed by "value", which takes the price of the bed
    /// away from how nice it is (a bed that costs half of what they carry loses a lot of points), so a
    /// guest with some silver in their pocket will often take a cheap bed over a nice one they could
    /// easily pay for. This changes that to what a guest would expect: the nicest bed they can afford.
    ///
    /// Everything else stays Hospitality's: which beds exist for the guest, which they may use, how
    /// room, comfort, titles, ideology, temperature and who else sleeps there are valued, and the
    /// claiming and paying. This only re-ranks the beds Hospitality already considers available (the
    /// ones that are not more than the guest's silver), and leaves the price out of the ranking.
    /// Where two beds are equally nice the cheaper one wins.
    ///
    /// Done with reflection, so the mod does not need Hospitality to be installed, and if a
    /// Hospitality update renames what it looks for, the patch is skipped and recorded.
    /// </summary>
    [HarmonyPatch]
    public static class Hospitality_BedChoice_Patch
    {
        private static readonly MethodBase FindBedFor = AccessTools.Method("Hospitality.Utilities.BedUtility:FindBedFor");
        private static readonly MethodInfo FindAvailableBeds = AccessTools.Method("Hospitality.Utilities.BedUtility:FindAvailableBeds");
        private static readonly MethodInfo CalculateBedValue = AccessTools.Method("Hospitality.Utilities.BedUtility:CalculateBedValue");
        private static readonly MethodInfo GetRentalFee = AccessTools.Method("Hospitality.Building_GuestBed:GetRentalFee");

        // Passed in place of the guest's silver, so the price part of the value rounds to nothing.
        private const int NoBudgetLimit = 1000000;

        static bool Prepare() => FindBedFor != null && FindAvailableBeds != null && CalculateBedValue != null && GetRentalFee != null;
        static MethodBase TargetMethod() => FindBedFor;

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn guest, ref Building_Bed __result)
        {
            try
            {
                var s = SmarterBedsMod.Settings;
                if (!s.enabled || !s.guestBedsEnabled || guest?.inventory == null) return;

                int money = guest.inventory.innerContainer.FirstOrDefault(t => t.def == ThingDefOf.Silver)?.stackCount ?? 0;
                var available = FindAvailableBeds.Invoke(null, new object[] { guest, money }) as IEnumerable;
                if (available == null) return;

                Building_Bed best = null;
                float bestValue = float.MinValue;
                int bestFee = int.MaxValue;
                foreach (object o in available)
                {
                    var bed = o as Building_Bed;
                    if (bed == null) continue;
                    float value = Convert.ToSingle(CalculateBedValue.Invoke(null, new object[] { bed, guest, NoBudgetLimit }));
                    int fee = Convert.ToInt32(GetRentalFee.Invoke(bed, null));
                    if (value > bestValue || (value == bestValue && fee < bestFee))
                    {
                        best = bed;
                        bestValue = value;
                        bestFee = fee;
                    }
                }
                if (best != null) __result = best;
            }
            catch (Exception e)
            {
                // Whatever Hospitality chose stands.
                SafeLog.Once("choosing a guest bed", e);
            }
        }
    }
}
