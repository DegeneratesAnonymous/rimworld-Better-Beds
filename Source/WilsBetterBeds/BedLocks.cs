using Verse;
using RimWorld;

namespace WilsBetterBeds
{
    /// <summary>
    /// The player's decisions about who owns which bed. When you set a bed's owner with the
    /// "Set owner" button the assignment is locked: nobody negotiates for that room and its owner
    /// does not negotiate for a different one.
    ///
    /// The locks live in the saved game (GameComponent_BedNegotiation), keyed by the bed's and the
    /// owner's ids, rather than on the beds themselves. That works for every bed from every mod with
    /// nothing added to any bed definition, and a lock stops applying on its own the moment the owner
    /// changes.
    /// </summary>
    public static class BedLocks
    {
        public static bool IsLocked(Building_Bed bed, Pawn owner) =>
            GameComponent_BedNegotiation.Instance?.IsLocked(bed, owner) ?? false;

        /// <summary>True when the player has set the bed's current owner.</summary>
        public static bool IsLockedForOwner(Building_Bed bed) =>
            GameComponent_BedNegotiation.Instance?.IsLockedForOwner(bed) ?? false;

        public static void Lock(Building_Bed bed, Pawn owner) =>
            GameComponent_BedNegotiation.Instance?.Lock(bed, owner);

        public static void Release(Building_Bed bed, Pawn owner) =>
            GameComponent_BedNegotiation.Instance?.Release(bed, owner);
    }
}
