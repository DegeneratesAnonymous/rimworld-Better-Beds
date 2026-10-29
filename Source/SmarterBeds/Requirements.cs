using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// A pawn's bedroom requirements, read from the game. Royalty titles only for now.
    /// </summary>
    public static class Requirements
    {
        private static RoyalTitle Title(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null) return null;
            // CanRequireBedroom respects the player's per-pawn "allow room requirements" toggle.
            if (!pawn.royalty.CanRequireBedroom()) return null;
            return pawn.royalty.HighestTitleWithBedroomRequirements();
        }

        /// <summary>Higher outranks lower. 0 for a pawn with no bedroom requirements.</summary>
        public static int Rank(Pawn pawn) => Title(pawn)?.def.seniority ?? 0;

        public static IEnumerable<RoomRequirement> For(Pawn pawn)
        {
            var title = Title(pawn);
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
