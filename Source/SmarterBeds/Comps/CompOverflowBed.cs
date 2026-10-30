using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SmarterBeds
{
    public class CompProperties_OverflowBed : CompProperties
    {
        public CompProperties_OverflowBed() => compClass = typeof(CompOverflowBed);
    }

    /// <summary>
    /// Per-bed state added to every bed: the "overflow" flag, and the player's lock.
    ///
    /// A locked bed is one the player has decided about. Nobody negotiates for it and its owner
    /// does not negotiate either. The lock belongs to a particular owner: if the bed changes hands
    /// (the owner dies, the player reassigns it) the lock simply stops applying.
    /// </summary>
    public class CompOverflowBed : ThingComp
    {
        public bool isOverflow;
        public int lockedOwnerId = -1;

        private Building_Bed Bed => parent as Building_Bed;

        /// <summary>True when the player has locked this bed for its current (single) owner.</summary>
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
            var comp = bed?.GetComp<CompOverflowBed>();
            return comp != null && owner != null && comp.lockedOwnerId == owner.thingIDNumber;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isOverflow, "smarterBeds_isOverflow", false);
            Scribe_Values.Look(ref lockedOwnerId, "smarterBeds_lockedOwnerId", -1);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;

            if (parent.Faction != Faction.OfPlayer) yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "SmarterBeds_OverflowLabel".Translate(),
                defaultDesc = "SmarterBeds_OverflowDesc".Translate(),
                icon = Textures.OverflowIcon,
                isActive = () => isOverflow,
                toggleAction = () => isOverflow = !isOverflow
            };

            var bed = Bed;
            if (bed == null || bed.Medical) yield break;

            var lockGizmo = new Command_Toggle
            {
                defaultLabel = "SmarterBeds_LockLabel".Translate(),
                defaultDesc = "SmarterBeds_LockDesc".Translate(),
                icon = TexCommand.ForbidOn,
                isActive = () => IsLockedForOwner,
                toggleAction = () =>
                {
                    if (IsLockedForOwner) lockedOwnerId = -1;
                    else if (bed.OwnersForReading.Count == 1) lockedOwnerId = bed.OwnersForReading[0].thingIDNumber;
                }
            };
            if (bed.OwnersForReading.Count != 1 && !IsLockedForOwner)
                lockGizmo.Disable("SmarterBeds_LockNeedsOneOwner".Translate());
            yield return lockGizmo;
        }

        public override string CompInspectStringExtra()
        {
            return IsLockedForOwner ? (string)"SmarterBeds_LockedInspect".Translate() : null;
        }
    }

    [StaticConstructorOnStartup]
    public static class Textures
    {
        public static readonly Texture2D OverflowIcon =
            ContentFinder<Texture2D>.Get("UI/OverflowBed", false) ?? BaseContent.BadTex;
    }

    /// <summary>
    /// Adds the comp to every bed def at startup, including modded beds and the guest bed defs
    /// Hospitality creates, without needing an XML patch per bed.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class OverflowCompInjector
    {
        static OverflowCompInjector()
        {
            int count = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsBed))
            {
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (def.comps.Any(c => c is CompProperties_OverflowBed)) continue;
                def.comps.Add(new CompProperties_OverflowBed());
                count++;
            }
            Log.Message($"[Smarter Beds] Overflow and lock toggles added to {count} bed types.");
        }
    }
}
