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

    public class CompOverflowBed : ThingComp
    {
        public bool isOverflow;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isOverflow, "smarterBeds_isOverflow", false);
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
        }
    }

    [StaticConstructorOnStartup]
    public static class Textures
    {
        public static readonly Texture2D OverflowIcon =
            ContentFinder<Texture2D>.Get("UI/OverflowBed", false) ?? BaseContent.BadTex;
    }

    /// <summary>
    /// Adds the overflow comp to every bed def at startup, including modded beds,
    /// without needing an XML patch per bed.
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
            Log.Message($"[Smarter Beds] Overflow toggle added to {count} bed types.");
        }
    }
}
