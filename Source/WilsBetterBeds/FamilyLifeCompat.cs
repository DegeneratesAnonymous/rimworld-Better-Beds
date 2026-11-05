using System;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// Wil's Family Life, when it is loaded, shares out the beds in a family's zone (a baby in its parents' room, a child in a room of
    /// its own once grown, a couple together). Where it is doing that for a child, this mod leaves them be, so the two never move the same
    /// child back and forth. Looked up by name; without Family Life nothing here does anything.
    /// </summary>
    public static class FamilyLifeCompat
    {
        private static Func<Pawn, bool> check;
        private static bool looked;

        public static bool IsHousedByFamilyLife(Pawn pawn)
        {
            if (!looked)
            {
                looked = true;
                try
                {
                    var type = GenTypes.GetTypeInAnyAssembly("FamilyLife.BedHousingApi");
                    var method = type?.GetMethod("IsManaged", new[] { typeof(Pawn) });
                    if (method != null) check = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), method);
                }
                catch (Exception) { check = null; }
            }
            try { return check != null && check(pawn); }
            catch (Exception) { return false; }
        }
    }
}
