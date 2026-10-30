using HarmonyLib;
using UnityEngine;
using Verse;

namespace SmarterBeds
{
    public class SmarterBedsMod : Mod
    {
        public static SmarterBedsSettings Settings;

        private Vector2 scrollPosition;
        private float viewHeight = 1400f;

        public SmarterBedsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmarterBedsSettings>();
            SafePatcher.PatchAll(new Harmony("SpacemanWil.SmarterBeds"));
            Log.Message("[Smarter Beds] Loaded.");
        }

        public override string SettingsCategory() => "Smarter Beds";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var view = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scrollPosition, view);

            var list = new Listing_Standard { ColumnWidth = view.width };
            list.Begin(view);
            var s = Settings;

            list.Label("SmarterBeds_Intro".Translate());
            list.Gap(6f);
            if (list.ButtonText("SmarterBeds_ResetDefaults".Translate())) s.ResetToDefaults();

            Section(list, "SmarterBeds_Sec_General");
            Check(list, "SmarterBeds_S_Enabled", ref s.enabled);
            Check(list, "SmarterBeds_S_Verbose", ref s.verboseLogging);

            Section(list, "SmarterBeds_Sec_Scoring");
            Slide(list, "SmarterBeds_S_RoomMood", ref s.roomMoodWeight, 0f, 3f, "0.00");
            Slide(list, "SmarterBeds_S_Rest", ref s.restEffectivenessWeight, 0f, 30f, "0.0");
            Check(list, "SmarterBeds_S_Titles", ref s.titleRulesEnabled);
            if (s.titleRulesEnabled)
                Slide(list, "SmarterBeds_S_Unmet", ref s.unmetRequirementPenalty, 0f, 20f, "0.0");
            Check(list, "SmarterBeds_S_Jealousy", ref s.jealousyEnabled);

            // Used by both family and neighbours, so it is not hidden behind either switch.
            Section(list, "SmarterBeds_Sec_Near");
            Slide(list, "SmarterBeds_S_FamilyRadius", ref s.familyRadius, 5f, 100f, "0");
            Slide(list, "SmarterBeds_S_FamilyHostile", ref s.familyHostileOpinion, -100f, 0f, "0");

            Section(list, "SmarterBeds_Sec_Family");
            Check(list, "SmarterBeds_S_Family", ref s.familyEnabled);
            if (s.familyEnabled)
            {
                Slide(list, "SmarterBeds_S_FamilyShare", ref s.familyShareBonus, 0f, 6f, "0.0");
                Slide(list, "SmarterBeds_S_FamilyNear", ref s.familyHostilePenalty, 0f, 10f, "0.0");
                Slide(list, "SmarterBeds_S_FamilyFar", ref s.familyFarPenalty, 0f, 10f, "0.0");
                Check(list, "SmarterBeds_S_CoupleHousing", ref s.coupleHousingEnabled);
                Slide(list, "SmarterBeds_S_CoupleRoom", ref s.coupleRoomBonus, 0f, 8f, "0.0");
                Slide(list, "SmarterBeds_S_CoupleShare", ref s.coupleSharePenalty, 0f, 8f, "0.0");
            }

            Section(list, "SmarterBeds_Sec_Neighbors");
            Check(list, "SmarterBeds_S_Neighbors", ref s.neighborsEnabled);
            if (s.neighborsEnabled)
            {
                Slide(list, "SmarterBeds_S_NeighborHostile", ref s.neighborHostilePenalty, 0f, 10f, "0.0");
                Slide(list, "SmarterBeds_S_IdeoBonus", ref s.ideoNeighborBonus, 0f, 6f, "0.0");
                Slide(list, "SmarterBeds_S_GeneBonus", ref s.geneNeighborBonus, 0f, 6f, "0.0");
            }

            Section(list, "SmarterBeds_Sec_Negotiation");
            Check(list, "SmarterBeds_S_Negotiate", ref s.negotiationEnabled);
            if (s.negotiationEnabled)
            {
                Check(list, "SmarterBeds_S_Quality", ref s.qualityNegotiationEnabled);
                if (s.qualityNegotiationEnabled)
                    Slide(list, "SmarterBeds_S_QualityGap", ref s.qualityNegotiationThreshold, 1f, 12f, "0.0");
                Slide(list, "SmarterBeds_S_Cooldown", ref s.negotiationCooldownDays, 0.5f, 30f, "0.0");
                Slide(list, "SmarterBeds_S_Rematch", ref s.rematchDays, 0f, 60f, "0");
                Slide(list, "SmarterBeds_S_MinWin", ref s.minWinChance, 0f, 0.5f, "0%");
                Slide(list, "SmarterBeds_S_JoyWeight", ref s.negotiationJoyWeight, 0.1f, 5f, "0.0");

                Section(list, "SmarterBeds_Sec_Roll");
                Slide(list, "SmarterBeds_S_Title", ref s.titleRollWeight, 0f, 10f, "0.0");
                Slide(list, "SmarterBeds_S_Defender", ref s.defenderBonus, 0f, 10f, "0.0");
                Slide(list, "SmarterBeds_S_Intimidate", ref s.intimidationFactor, 0f, 1.5f, "0.00");
                Slide(list, "SmarterBeds_S_Tenure", ref s.tenureCap, 0f, 10f, "0.0");
                Slide(list, "SmarterBeds_S_Xp", ref s.socialXpOnWin, 0f, 300f, "0");
            }

            // Messages also announce couples moving in, so these stay available without negotiation.
            Section(list, "SmarterBeds_Sec_Feedback");
            if (s.negotiationEnabled) Check(list, "SmarterBeds_S_Moods", ref s.negotiationMoodEffects);
            Check(list, "SmarterBeds_S_Messages", ref s.negotiationMessages);
            if (s.negotiationMessages)
                Slide(list, "SmarterBeds_S_MessageSeconds", ref s.messageSeconds, 0.5f, 13f, "0.0");
            if (s.negotiationEnabled) Check(list, "SmarterBeds_S_Log", ref s.showInPlayLog);

            Section(list, "SmarterBeds_Sec_Rough");
            Check(list, "SmarterBeds_S_Chair", ref s.chairSleepEnabled);
            if (s.chairSleepEnabled)
                Slide(list, "SmarterBeds_S_ChairPenalty", ref s.chairSleepPenalty, 0f, 8f, "0.0");

            Section(list, "SmarterBeds_Sec_Interface");
            Check(list, "SmarterBeds_S_RoomsButton", ref s.showRoomsButton);

            Section(list, "SmarterBeds_Sec_Diag");
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

        private static void Slide(Listing_Standard list, string key, ref float value, float min, float max, string format)
        {
            string shown = format == "0%" ? (value * 100f).ToString("0") + "%" : value.ToString(format);
            list.Label(key.Translate(shown), -1f, (key + "_Tip").Translate());
            value = list.Slider(value, min, max);
        }
    }
}
