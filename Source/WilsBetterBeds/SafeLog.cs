using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// A problem in one of this mod's own features should never break the game or fill the log. Code
    /// that runs from the UI, a patch or a thought catches its exceptions, skips the feature for that
    /// call, and reports it here once.
    /// </summary>
    public static class SafeLog
    {
        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static void Once(string what, Exception e)
        {
            if (!Seen.Add(what)) return;
            Log.Warning($"[Wil's Better Beds] {what} failed and was skipped (reported once): {e.GetType().Name}: {e.Message}");
        }

        /// <summary>A plain message, written once per key.</summary>
        public static void Note(string key, string text)
        {
            if (!Seen.Add(key)) return;
            Log.Warning("[Wil's Better Beds] " + text);
        }
    }

    /// <summary>
    /// Applies each Harmony patch on its own. If a game update or another mod changes something one
    /// patch depends on, that one feature is switched off and recorded, and everything else carries on.
    /// The result is shown at the bottom of the mod options so a bug report can say what was active.
    /// </summary>
    public static class SafePatcher
    {
        public static readonly List<string> Applied = new List<string>();
        public static readonly List<string> Failed = new List<string>();
        public static int Hooks;   // methods actually patched
        public static int Classes; // patch classes this mod has

        /// <summary>Mods this one knows how to work alongside, found by package id.</summary>
        private static readonly string[] KnownMods =
        {
            "Orion.Hospitality", "Jaxe.Bubbles", "Dubs.MintMenus", "Lucifer.RealisticRooms",
            "OskarPotocki.VanillaFactionsExpanded.Core", "OskarPotocki.VanillaFactionsExpanded.Empire",
            "Ludeon.RimWorld.Royalty", "Ludeon.RimWorld.Ideology", "Ludeon.RimWorld.Biotech"
        };

        public static readonly List<string> DetectedMods = new List<string>();

        public static void PatchAll(Harmony harmony)
        {
            foreach (var type in typeof(SafePatcher).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                Classes++;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    Applied.Add(type.Name);
                }
                catch (Exception e)
                {
                    Failed.Add(type.Name + ": " + e.GetType().Name + ": " + FirstLine(e.Message));
                    Log.Warning($"[Wil's Better Beds] Patch {type.Name} could not be applied and was skipped: {FirstLine(e.Message)}");
                }
            }

            // A patch class can also be skipped quietly (its Prepare says the target is not there),
            // so count the methods that really ended up patched.
            Hooks = harmony.GetPatchedMethods().Count();

            foreach (var id in KnownMods)
            {
                try
                {
                    if (ModLister.GetActiveModWithIdentifier(id, true) != null) DetectedMods.Add(id);
                }
                catch { /* detection is only for the report */ }
            }

            Log.Message($"[Wil's Better Beds] {Hooks} of {Classes} hooks active" +
                        (Failed.Count > 0 ? $", {Failed.Count} failed" : "") +
                        (DetectedMods.Count > 0 ? "; works alongside: " + string.Join(", ", DetectedMods) : "") + ".");
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        /// <summary>One line for the options window.</summary>
        public static string Summary() =>
            "WilsBetterBeds_Diag_Summary".Translate(Hooks, Classes).ToString();

        public static string Details()
        {
            var lines = new List<string>();
            if (Failed.Count > 0) lines.Add("Skipped: " + string.Join("; ", Failed));
            if (DetectedMods.Count > 0) lines.Add("Alongside: " + string.Join(", ", DetectedMods));
            lines.Add("Active: " + string.Join(", ", Applied));
            string perf = PerfWatch.Report();
            if (perf.Length > 0) lines.Add(perf);
            return string.Join("\n", lines);
        }
    }
}
