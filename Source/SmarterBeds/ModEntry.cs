using HarmonyLib;
using UnityEngine;
using Verse;

namespace SmarterBeds
{
    public class SmarterBedsMod : Mod
    {
        public static SmarterBedsSettings Settings;

        public SmarterBedsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmarterBedsSettings>();
            new Harmony("yourname.smarterbeds").PatchAll();
            Log.Message("[Smarter Beds] Loaded.");
        }

        public override string SettingsCategory() => "Smarter Beds";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.CheckboxLabeled("SmarterBeds_SettingsEnabled".Translate(), ref Settings.enabled);
            list.CheckboxLabeled("SmarterBeds_SettingsVerbose".Translate(), ref Settings.verboseLogging);
            list.GapLine();

            list.Label("SmarterBeds_WeightImpressiveness".Translate(Settings.impressivenessWeight.ToString("0.00")));
            Settings.impressivenessWeight = list.Slider(Settings.impressivenessWeight, 0f, 5f);

            list.Label("SmarterBeds_WeightPrivate".Translate(Settings.privateRoomBonus.ToString("0")));
            Settings.privateRoomBonus = list.Slider(Settings.privateRoomBonus, 0f, 500f);

            list.Label("SmarterBeds_WeightRest".Translate(Settings.restEffectivenessWeight.ToString("0")));
            Settings.restEffectivenessWeight = list.Slider(Settings.restEffectivenessWeight, 0f, 500f);

            list.Label("SmarterBeds_WeightDistance".Translate(Settings.distanceWeight.ToString("0.00")));
            Settings.distanceWeight = list.Slider(Settings.distanceWeight, 0f, 5f);

            list.Label("SmarterBeds_OverflowPenalty".Translate(Settings.overflowPenalty.ToString("0")));
            Settings.overflowPenalty = list.Slider(Settings.overflowPenalty, 0f, 5000f);

            list.GapLine();
            list.Label("SmarterBeds_UnmetPenalty".Translate(Settings.unmetRequirementPenalty.ToString("0")));
            Settings.unmetRequirementPenalty = list.Slider(Settings.unmetRequirementPenalty, 0f, 10000f);
            list.CheckboxLabeled("SmarterBeds_NegotiationEnabled".Translate(), ref Settings.negotiationEnabled);
            list.CheckboxLabeled("SmarterBeds_QualityNegotiation".Translate(), ref Settings.qualityNegotiationEnabled);
            list.Label("SmarterBeds_QualityThreshold".Translate(Settings.qualityNegotiationThreshold.ToString("0")));
            Settings.qualityNegotiationThreshold = list.Slider(Settings.qualityNegotiationThreshold, 50f, 1000f);
            list.Label("SmarterBeds_Cooldown".Translate(Settings.negotiationCooldownDays.ToString("0.0")));
            Settings.negotiationCooldownDays = list.Slider(Settings.negotiationCooldownDays, 0.5f, 30f);
            list.CheckboxLabeled("SmarterBeds_LostRoomThought".Translate(), ref Settings.lostRoomThought);
            list.CheckboxLabeled("SmarterBeds_Messages".Translate(), ref Settings.negotiationMessages);

            list.End();
        }
    }
}
