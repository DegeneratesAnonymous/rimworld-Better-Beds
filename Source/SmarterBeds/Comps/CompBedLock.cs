using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SmarterBeds
{
    public class CompProperties_BedLock : CompProperties
    {
        public CompProperties_BedLock() => compClass = typeof(CompBedLock);
    }

    /// <summary>
    /// Added to every bed. Remembers when the player chose the bed's owner with the "Set owner"
    /// button, which locks the assignment: nobody negotiates for that room and its owner does not
    /// negotiate for a different one. The lock belongs to that particular owner, so it ends when the
    /// player unassigns them (see ManualAssign_Patch) or the bed changes hands some other way.
    /// </summary>
    public class CompBedLock : ThingComp
    {
        public int lockedOwnerId = -1;

        private Building_Bed Bed => parent as Building_Bed;

        /// <summary>True when the player has set the current owner of this bed.</summary>
        public bool IsLockedForOwner
        {
            get
            {
                var owners = Bed?.OwnersForReading;
                return lockedOwnerId >= 0 && owners != null && owners.Any(p => p.thingIDNumber == lockedOwnerId);
            }
        }

        public static bool IsLocked(Building_Bed bed, Pawn owner)
        {
            var comp = bed?.GetComp<CompBedLock>();
            return comp != null && owner != null && comp.lockedOwnerId == owner.thingIDNumber && comp.IsLockedForOwner;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lockedOwnerId, "smarterBeds_lockedOwnerId", -1);
        }

        public override string CompInspectStringExtra()
        {
            return IsLockedForOwner ? (string)"SmarterBeds_LockedInspect".Translate() : null;
        }
    }

    /// <summary>
    /// Adds the comp to every bed def at startup, including modded beds and the guest bed defs
    /// Hospitality creates, without needing an XML patch per bed.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BedLockInjector
    {
        static BedLockInjector()
        {
            int count = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsBed))
            {
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (def.comps.Any(c => c is CompProperties_BedLock)) continue;
                def.comps.Add(new CompProperties_BedLock());
                count++;
            }
            Log.Message($"[Smarter Beds] Assignment lock added to {count} bed types.");
        }
    }
}
