using HarmonyLib;
using UnityEngine;
using Verse;

namespace WilsBetterBeds
{
    public class WilsBetterBedsMod : Mod
    {
        public static WilsBetterBedsSettings Settings;

        private Vector2 scrollPosition;
        private float viewHeight = 1400f;

        public WilsBetterBedsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WilsBetterBedsSettings>();
            SafePatcher.PatchAll(new Harmony("SpacemanWil.WilsBetterBeds"));
            Log.Message("[Wil's Better Beds] Loaded.");
        }

        public override string SettingsCategory() => "Wil's Better Beds";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var view = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scrollPosition, view);

            // One column only: once the list is taller than the view, Listing_Standard would start a second
            // column off to the right, and the measured height (below) would collapse with it.
            var list = new Listing_Standard { ColumnWidth = view.width, maxOneColumn = true };
            list.Begin(view);
            var s = Settings;

            list.Label("WilsBetterBeds_Intro".Translate());
            list.Gap(6f);
            if (list.ButtonText("WilsBetterBeds_ResetDefaults".Translate())) s.ResetToDefaults();

            Section(list, "WilsBetterBeds_Sec_General");
            Check(list, "WilsBetterBeds_S_Enabled", ref s.enabled);
            Check(list, "WilsBetterBeds_S_Verbose", ref s.verboseLogging);

            Section(list, "WilsBetterBeds_Sec_Scoring");
            Slide(list, "WilsBetterBeds_S_RoomMood", ref s.roomMoodWeight, 0f, 3f, "0.00");
            Slide(list, "WilsBetterBeds_S_Rest", ref s.restEffectivenessWeight, 0f, 30f, "0.0",
                () => "WilsBetterBeds_Live_Rest".Translate((0.05f * s.restEffectivenessWeight).ToString("0.0#")));
            Check(list, "WilsBetterBeds_S_Titles", ref s.titleRulesEnabled);
            if (s.titleRulesEnabled)
                Slide(list, "WilsBetterBeds_S_Unmet", ref s.unmetRequirementPenalty, 0f, 20f, "0.0");
            Check(list, "WilsBetterBeds_S_Jealousy", ref s.jealousyEnabled);
            Check(list, "WilsBetterBeds_S_FreeBeds", ref s.freeBedMoveEnabled);

            // Used by both family and neighbours, so it is not hidden behind either switch.
            Section(list, "WilsBetterBeds_Sec_Near");
            Slide(list, "WilsBetterBeds_S_FamilyRadius", ref s.familyRadius, 5f, 100f, "0",
                () => "WilsBetterBeds_Live_Radius".Translate((s.familyRadius / 7f).ToString("0.#")));
            Slide(list, "WilsBetterBeds_S_FamilyHostile", ref s.familyHostileOpinion, -100f, 0f, "0");

            Section(list, "WilsBetterBeds_Sec_Family");
            Check(list, "WilsBetterBeds_S_Family", ref s.familyEnabled);
            if (s.familyEnabled)
            {
                Slide(list, "WilsBetterBeds_S_FamilyShare", ref s.familyShareBonus, 0f, 6f, "0.0");
                Slide(list, "WilsBetterBeds_S_FamilyNear", ref s.familyHostilePenalty, 0f, 10f, "0.0");
                Slide(list, "WilsBetterBeds_S_FamilyFar", ref s.familyFarPenalty, 0f, 10f, "0.0");
                Check(list, "WilsBetterBeds_S_CoupleHousing", ref s.coupleHousingEnabled);
                if (s.negotiationEnabled) Check(list, "WilsBetterBeds_S_CoupleNegotiate", ref s.coupleNegotiationEnabled);
                Slide(list, "WilsBetterBeds_S_CoupleRoom", ref s.coupleRoomBonus, 0f, 8f, "0.0");
                Slide(list, "WilsBetterBeds_S_CoupleShare", ref s.coupleSharePenalty, 0f, 8f, "0.0");
            }

            Section(list, "WilsBetterBeds_Sec_Neighbors");
            Check(list, "WilsBetterBeds_S_Neighbors", ref s.neighborsEnabled);
            if (s.neighborsEnabled)
            {
                Slide(list, "WilsBetterBeds_S_NeighborHostile", ref s.neighborHostilePenalty, 0f, 10f, "0.0");
                Slide(list, "WilsBetterBeds_S_IdeoBonus", ref s.ideoNeighborBonus, 0f, 6f, "0.0");
                Slide(list, "WilsBetterBeds_S_GeneBonus", ref s.geneNeighborBonus, 0f, 6f, "0.0");
                Slide(list, "WilsBetterBeds_S_PrisonerNear", ref s.prisonerNeighborPenalty, 0f, 10f, "0.0");
            }

            Section(list, "WilsBetterBeds_Sec_Negotiation");
            Check(list, "WilsBetterBeds_S_Negotiate", ref s.negotiationEnabled);
            if (s.negotiationEnabled)
            {
                Check(list, "WilsBetterBeds_S_Quality", ref s.qualityNegotiationEnabled);
                if (s.qualityNegotiationEnabled)
                    Slide(list, "WilsBetterBeds_S_QualityGap", ref s.qualityNegotiationThreshold, 1f, 12f, "0.0");
                Slide(list, "WilsBetterBeds_S_Cooldown", ref s.negotiationCooldownDays, 0.5f, 30f, "0.0");
                Slide(list, "WilsBetterBeds_S_Rematch", ref s.rematchDays, 0f, 60f, "0");
                Slide(list, "WilsBetterBeds_S_MinWin", ref s.minWinChance, 0f, 0.5f, "0%",
                    () => "WilsBetterBeds_Live_MinWin".Translate((s.minWinChance * 100f).ToString("0") + "%"));
                Slide(list, "WilsBetterBeds_S_JoyWeight", ref s.negotiationJoyWeight, 0.1f, 5f, "0.0");

                Section(list, "WilsBetterBeds_Sec_Roll");
                Slide(list, "WilsBetterBeds_S_Title", ref s.titleRollWeight, 0f, 10f, "0.0",
                    () => "WilsBetterBeds_Live_Title".Translate(s.titleRollWeight.ToString("0.0"),
                        Pct(NegotiationRoll.WinChance(0f, Mathf.Round(s.defenderBonus))),
                        Pct(NegotiationRoll.WinChance(Mathf.Round(s.titleRollWeight), Mathf.Round(s.defenderBonus)))));
                Slide(list, "WilsBetterBeds_S_Defender", ref s.defenderBonus, 0f, 10f, "0.0",
                    () => "WilsBetterBeds_Live_Defender".Translate(Pct(NegotiationRoll.WinChance(0f, Mathf.Round(s.defenderBonus)))));
                Slide(list, "WilsBetterBeds_S_Intimidate", ref s.intimidationFactor, 0f, 1.5f, "0.00",
                    () => "WilsBetterBeds_Live_Intimidate".Translate((12f * s.intimidationFactor).ToString("0.#"),
                        (12f * s.intimidationFactor > 12f + 2f ? "WilsBetterBeds_Live_IntimidatePick" : "WilsBetterBeds_Live_IntimidateNo").Translate()));
                Slide(list, "WilsBetterBeds_S_Tenure", ref s.tenureCap, 0f, 10f, "0.0",
                    () => "WilsBetterBeds_Live_Tenure".Translate((s.tenureCap * 45f).ToString("0")));
                Slide(list, "WilsBetterBeds_S_Xp", ref s.socialXpOnWin, 0f, 300f, "0",
                    () => "WilsBetterBeds_Live_Xp".Translate(s.socialXpOnWin.ToString("0"), (s.socialXpOnWin / 2f).ToString("0")));
            }

            // Messages also announce couples moving in, so these stay available without negotiation.
            Section(list, "WilsBetterBeds_Sec_Feedback");
            if (s.negotiationEnabled) Check(list, "WilsBetterBeds_S_Moods", ref s.negotiationMoodEffects);
            Check(list, "WilsBetterBeds_S_Messages", ref s.negotiationMessages);
            if (s.negotiationMessages)
                Slide(list, "WilsBetterBeds_S_MessageSeconds", ref s.messageSeconds, 0.5f, 13f, "0.0");
            if (s.negotiationEnabled) Check(list, "WilsBetterBeds_S_Log", ref s.showInPlayLog);

            Section(list, "WilsBetterBeds_Sec_Rough");
            Check(list, "WilsBetterBeds_S_Chair", ref s.chairSleepEnabled);
            if (s.chairSleepEnabled)
                Slide(list, "WilsBetterBeds_S_ChairPenalty", ref s.chairSleepPenalty, 0f, 8f, "0.0", ChairExample);

            Section(list, "WilsBetterBeds_Sec_Guests");
            Check(list, "WilsBetterBeds_S_VisitorBeds", ref s.visitorsUseGuestBeds);

            Section(list, "WilsBetterBeds_Sec_Interface");
            Check(list, "WilsBetterBeds_S_RoomsButton", ref s.showRoomsButton);

            Section(list, "WilsBetterBeds_Sec_Diag");
            list.Label(new TaggedString(SafePatcher.Summary()), -1f, SafePatcher.Details());

            viewHeight = list.CurHeight + 24f;
            list.End();
            Widgets.EndScrollView();
        }

        private static void Section(Listing_Standard list, string headingKey)
        {
            list.Gap(10f);
            Text.Font = GameFont.Medium;
            list.Label(headingKey.Translate());
            Text.Font = GameFont.Small;
            list.GapLine(4f);
        }

        private static void Check(Listing_Standard list, string key, ref bool value)
        {
            list.CheckboxLabeled(key.Translate(), ref value, (key + "_Tip").Translate());
        }

        private static string Pct(float chance) => (chance * 100f).ToString("0") + "%";

        private static string ChairExample()
        {
            float diff = 4f - Settings.chairSleepPenalty;
            string how = Mathf.Abs(diff) < 0.05f ? "WilsBetterBeds_Live_ChairSame".Translate().ToString()
                : diff > 0f ? "WilsBetterBeds_Live_ChairBetter".Translate(diff.ToString("0.#")).ToString()
                : "WilsBetterBeds_Live_ChairWorse".Translate((-diff).ToString("0.#")).ToString();
            return "WilsBetterBeds_Live_Chair".Translate(how);
        }

        /// <param name="live">An example worked out from the current value, shown under the tooltip text.</param>
        private static void Slide(Listing_Standard list, string key, ref float value, float min, float max, string format, System.Func<string> live = null)
        {
            string shown = format == "0%" ? (value * 100f).ToString("0") + "%" : value.ToString(format);
            string tip = (key + "_Tip").Translate();
            if (live != null)
            {
                try { tip += "\n\n" + "WilsBetterBeds_Live_Header".Translate() + " " + live(); }
                catch (System.Exception) { /* a tooltip example is never worth a broken settings window */ }
            }
            list.Label(key.Translate(shown), -1f, tip);
            value = list.Slider(value, min, max);
        }
    }
}
