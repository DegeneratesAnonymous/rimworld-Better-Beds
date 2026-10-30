using System.Reflection;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// All tunables. Scoring numbers are mood points (see RoomComfort); defaults are explained in
    /// the settings window and in PLAN.md.
    /// </summary>
    public class SmarterBedsSettings : ModSettings
    {
        // General
        public bool enabled = true;
        public bool verboseLogging = false;

        // Bed scoring, in mood points
        public float roomMoodWeight = 1f;            // 1 = trust the game's room mood numbers as they are
        public float restEffectivenessWeight = 10f;  // mood points per 1.0 of bed rest effectiveness
        public float distanceWeight = 0.01f;         // mood points lost per cell walked
        public float overflowPenalty = 20f;          // overflow beds are used only when nothing else is free
        public float unmetRequirementPenalty = 6f;   // the game's own title bedroom penalty

        // Negotiation
        public bool negotiationEnabled = true;
        public bool qualityNegotiationEnabled = true;
        public float qualityNegotiationThreshold = 3f; // mood points gained, for a negotiation with no other reason
        public float negotiationCooldownDays = 3f;     // between the same two colonists
        public float rematchDays = 15f;                // a loser may not challenge the same winner this soon
        public float minWinChance = 0.10f;             // below this a pawn does not even try
        public float negotiationJoyWeight = 1f;        // multiplier on how often it is picked as recreation

        // The roll
        public float titleRollWeight = 3f;             // roll points per title step (seniority / 100)
        public float defenderBonus = 3f;               // for already holding the room
        public float intimidationFactor = 0.75f;       // Melee level x this, against Social x 1
        public float tenureCap = 3f;                   // most roll points time in the colony can give
        public float socialXpOnWin = 60f;              // half of it on a fail

        // Feedback
        public bool negotiationMoodEffects = true;
        public bool negotiationMessages = true;
        public bool showInPlayLog = true;              // also what Interaction Bubbles displays

        // Player control
        public bool autoLockManualAssignments = true;

        // Sleeping rough
        public bool chairSleepEnabled = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);

            Scribe_Values.Look(ref roomMoodWeight, "roomMoodWeight", 1f);
            Scribe_Values.Look(ref restEffectivenessWeight, "restEffectivenessWeight", 10f);
            Scribe_Values.Look(ref distanceWeight, "distanceWeight", 0.01f);
            Scribe_Values.Look(ref overflowPenalty, "overflowPenalty", 20f);
            Scribe_Values.Look(ref unmetRequirementPenalty, "unmetRequirementPenalty", 6f);

            Scribe_Values.Look(ref negotiationEnabled, "negotiationEnabled", true);
            Scribe_Values.Look(ref qualityNegotiationEnabled, "qualityNegotiationEnabled", true);
            Scribe_Values.Look(ref qualityNegotiationThreshold, "qualityNegotiationThreshold", 3f);
            Scribe_Values.Look(ref negotiationCooldownDays, "negotiationCooldownDays", 3f);
            Scribe_Values.Look(ref rematchDays, "rematchDays", 15f);
            Scribe_Values.Look(ref minWinChance, "minWinChance", 0.10f);
            Scribe_Values.Look(ref negotiationJoyWeight, "negotiationJoyWeight", 1f);

            Scribe_Values.Look(ref titleRollWeight, "titleRollWeight", 3f);
            Scribe_Values.Look(ref defenderBonus, "defenderBonus", 3f);
            Scribe_Values.Look(ref intimidationFactor, "intimidationFactor", 0.75f);
            Scribe_Values.Look(ref tenureCap, "tenureCap", 3f);
            Scribe_Values.Look(ref socialXpOnWin, "socialXpOnWin", 60f);

            Scribe_Values.Look(ref negotiationMoodEffects, "negotiationMoodEffects", true);
            Scribe_Values.Look(ref negotiationMessages, "negotiationMessages", true);
            Scribe_Values.Look(ref showInPlayLog, "showInPlayLog", true);

            Scribe_Values.Look(ref autoLockManualAssignments, "autoLockManualAssignments", true);
            Scribe_Values.Look(ref chairSleepEnabled, "chairSleepEnabled", true);
        }

        /// <summary>Puts every setting back to the value in its field initializer.</summary>
        public void ResetToDefaults()
        {
            var fresh = new SmarterBedsSettings();
            foreach (var field in typeof(SmarterBedsSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                field.SetValue(this, field.GetValue(fresh));
            }
        }
    }
}
