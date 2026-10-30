using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// A pawn's bedroom requirements, read from the game. Royalty titles only: Ideology roles carry
    /// skill and age requirements in the game's data, but no bedroom requirements.
    /// Goes through the game's own methods, so mods that change title requirements
    /// (Vanilla Factions Expanded - Empire, for one) are respected automatically.
    /// </summary>
    public static class Requirements
    {
        /// <summary>The title that sets this pawn's bedroom requirements, or null.</summary>
        public static RoyalTitle TitleOf(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null) return null;
            // CanRequireBedroom respects the player's per-pawn "allow room requirements" toggle.
            if (!pawn.royalty.CanRequireBedroom()) return null;
            return pawn.royalty.HighestTitleWithBedroomRequirements();
        }

        /// <summary>Higher outranks lower. 0 for a pawn with no bedroom requirements.</summary>
        public static int Rank(Pawn pawn) => TitleOf(pawn)?.def.seniority ?? 0;

        public static IEnumerable<RoomRequirement> For(Pawn pawn)
        {
            var title = TitleOf(pawn);
            if (title == null) return Enumerable.Empty<RoomRequirement>();
            return title.def.GetBedroomRequirements(pawn) ?? Enumerable.Empty<RoomRequirement>();
        }

        /// <summary>How many of the pawn's requirements this room fails. A null room fails all of them.</summary>
        public static int Unmet(Pawn pawn, Room room)
        {
            int count = 0;
            foreach (var req in For(pawn))
            {
                if (room == null || room.PsychologicallyOutdoors || !req.MetOrDisabled(room, pawn)) count++;
            }
            return count;
        }
    }
}
