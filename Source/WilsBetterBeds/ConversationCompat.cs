using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// RimTalk (cj.rimtalk). That mod has colonists talk with AI-written dialogue and has a public API for other mods to add to
    /// what the model is told about a pawn. A pawn with a bed gets one more line there ("Sleeping arrangement: ..."), so what
    /// they say can reflect where they sleep and who with. The room negotiations this mod writes to the play log already reach
    /// RimTalk through its own play log patch. Add-ons for RimTalk use the same API and need nothing from us.
    /// Nothing from the other mod is referenced at compile time: it is found by reflection, and if anything about it looks
    /// different the feature is simply not offered. Read from the 1.6 build of that mod; not run in game.
    /// </summary>
    public static class RimTalkCompat
    {
        private const string PackageId = "cj.rimtalk";
        private const string ModId = "SpacemanWil.WilsBetterBeds";
        private static bool registered;

        public static bool Installed => ModLister.GetActiveModWithIdentifier(PackageId, true) != null;

        public static void Register()
        {
            if (registered || !Installed) return;
            registered = true;
            try
            {
                var api = GenTypes.GetTypeInAnyAssembly("RimTalk.API.RimTalkPromptAPI");
                var categories = GenTypes.GetTypeInAnyAssembly("RimTalk.API.ContextCategories")?.GetNestedType("Pawn");
                var anchor = categories?.GetField("Social")?.GetValue(null);
                var position = GenTypes.GetTypeInAnyAssembly("RimTalk.API.ContextHookRegistry")?.GetNestedType("InjectPosition");
                var inject = api?.GetMethod("InjectPawnSection", BindingFlags.Public | BindingFlags.Static);
                if (inject == null || anchor == null || position == null || inject.GetParameters().Length != 6)
                {
                    Log.Warning("[Wil's Better Beds] RimTalk is installed but its API was not as expected; its conversations are not told about beds.");
                    return;
                }
                inject.Invoke(null, new object[] { ModId, "Sleeping arrangement", anchor, Enum.Parse(position, "After"), (Func<Pawn, string>)SleepText, 110 });
                Log.Message("[Wil's Better Beds] RimTalk detected: conversations are told where a pawn sleeps and with whom.");
            }
            catch (Exception e) { SafeLog.Once("telling RimTalk about beds", e); }
        }

        /// <summary>One short line for the model: the pawn's bed, who shares the room, or that they have none. Empty for anyone who doesn't sleep in a bed.</summary>
        private static string SleepText(Pawn pawn)
        {
            try
            {
                var s = WilsBetterBedsMod.Settings;
                if (s == null || !s.enabled || !s.rimTalkContext || pawn == null || !pawn.IsColonist || pawn.ownership == null || pawn.Dead) return string.Empty;
                var bed = pawn.ownership.OwnedBed;
                if (bed == null) return "Sleeping arrangement: has no bed of their own";

                var mates = bed.OwnersForReading.Where(o => o != null && o != pawn).Select(o => o.LabelShort).ToList();
                var room = bed.GetRoom();
                var roommates = new List<string>();
                if (room != null && !room.PsychologicallyOutdoors)
                    foreach (var other in room.ContainedBeds.SelectMany(b => b.OwnersForReading))
                        if (other != null && other != pawn && !roommates.Contains(other.LabelShort)) roommates.Add(other.LabelShort);

                string where = roommates.Count > 0 ? "a bedroom shared with " + string.Join(", ", roommates.Take(4)) : "a bedroom of their own";
                if (room == null || room.PsychologicallyOutdoors) where = "a bed outdoors";
                string bedText = mates.Count > 0 ? $"shares a {bed.def.label} with {string.Join(", ", mates)}" : $"sleeps in a {bed.def.label}";
                return $"Sleeping arrangement: {bedText} in {where}";      // RimTalk adds the text as it is, so it carries its own label
            }
            catch (Exception e) { SafeLog.Once("describing a pawn's bed for RimTalk", e); return string.Empty; }
        }
    }

    /// <summary>
    /// Interaction Bubbles Expanded (Vengie.BubblesExpanded). It shows play log entries as bubbles exactly as the original
    /// Interaction Bubbles does (so negotiation lines need nothing), and tints a bubble green or red by the mood memories the
    /// two pawns gained during the interaction. It only measures that for interactions the game runs itself, so a successful
    /// negotiation (whose mood effects this mod applies) asks it to measure through its own entry point; without that mod the
    /// action simply runs.
    /// Nothing from the other mod is referenced at compile time. Read from the 1.7 build of that mod; not run in game.
    /// </summary>
    public static class BubblesExpandedCompat
    {
        private static bool looked;
        private static MethodInfo runTracked;

        public static void Track(Pawn initiator, Pawn recipient, Action action)
        {
            if (!looked)
            {
                looked = true;
                try
                {
                    if (ModLister.GetActiveModWithIdentifier("Vengie.BubblesExpanded", true) != null)
                        runTracked = GenTypes.GetTypeInAnyAssembly("Bubbles.Access.InteractionMood")?
                            .GetMethod("RunTracked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (runTracked != null && runTracked.GetParameters().Length == 3)
                        Log.Message("[Wil's Better Beds] Interaction Bubbles Expanded detected: negotiation bubbles are tinted by their mood effect.");
                    else runTracked = null;
                }
                catch (Exception e) { SafeLog.Once("looking for Interaction Bubbles Expanded", e); runTracked = null; }
            }
            if (runTracked == null) { action(); return; }
            try { runTracked.Invoke(null, new object[] { initiator, recipient, action }); }
            catch (Exception e) { SafeLog.Once("tracking an interaction's mood for bubbles", e); }
        }
    }
}
