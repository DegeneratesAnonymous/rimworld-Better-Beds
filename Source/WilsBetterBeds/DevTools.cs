using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// In god mode, a selected colonist gets a "Force negotiation" button. It sends them to ask for a
    /// better room right now, skipping the usual reasons, cooldowns and odds checks, so the whole thing
    /// (the walk, the pitch, the roll, the bubbles, the moods) can be watched on demand.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class ForceNegotiation_Gizmo_Patch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (!DebugSettings.godMode || !WilsBetterBedsMod.Settings.enabled) return;
                if (!__instance.IsColonistPlayerControlled) return;

                var pawn = __instance;
                __result = __result.Concat(new Gizmo[]
                {
                    new Command_Action
                    {
                        defaultLabel = "WilsBetterBeds_ForceNegotiation".Translate(),
                        defaultDesc = "WilsBetterBeds_ForceNegotiation_Desc".Translate(),
                        icon = TexButton.Add,
                        action = () => GameComponent_BedNegotiation.Instance?.ForceNegotiation(pawn)
                    }
                });
            }
            catch (System.Exception e) { SafeLog.Once("force negotiation button", e); }
        }
    }
}
