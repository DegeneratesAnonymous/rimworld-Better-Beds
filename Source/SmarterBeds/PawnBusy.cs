using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// A colonist in the middle of something personal is never pulled into a negotiation: using the
    /// toilet, washing, bathing or showering (Dubs Bad Hygiene), or eating, and anything else the game
    /// marks as not to be casually interrupted.
    /// </summary>
    public static class PawnBusy
    {
        // Dubs Bad Hygiene's personal jobs, by name, in case a game version hides the mod of origin.
        private static readonly HashSet<string> PersonalJobs = new HashSet<string>
        {
            "UseToilet", "haveWildPoo", "useWashBucket", "washAtCell", "takeShower", "takeBath", "washHands",
            "washPatient", "DBHDrinkFromBasin", "DBHDrinkFromGround", "cleanBedpan"
        };

        public static bool Is(Pawn p)
        {
            var def = p?.CurJob?.def;
            if (def == null) return false;
            if (PersonalJobs.Contains(def.defName)) return true;

            // Any job that comes from a hygiene mod (Dubs Bad Hygiene and its patches).
            string pack = def.modContentPack?.PackageId;
            if (pack != null && pack.IndexOf("badhygiene", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (def == JobDefOf.Ingest) return true;
            return !def.casualInterruptible;
        }
    }
}
