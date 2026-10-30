using System.Reflection;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// All tunables. Scoring numbers are mood points (see RoomComfort); defaults are explained in
    /// the settings window and in PLAN.md.
    /// </summary>
    public class WilsBetterBedsSettings : ModSettings
    {
        // General
        public bool enabled = true;
        public bool verboseLogging = false;

        // Bed scoring, in mood points
        public float roomMoodWeight = 1f;            // 1 = trust the game's room mood numbers as they are
        public float restEffectivenessWeight = 10f;  // mood points per 1.0 of bed rest effectiveness
        public float unmetRequirementPenalty = 6f;   // the game's own title bedroom penalty
        public bool titleRulesEnabled = true;        // Royalty: a title-holder's bedroom rules count in the score
        public bool jealousyEnabled = true;          // the Jealous trait counts in the score
        public bool temperatureEnabled = true;       // a bed that is too cold or too hot for the sleeper counts against it
        public float temperaturePenalty = 4f;        // the game's own 'slept in the cold / heat' is -4

        // Family and couples
        public bool familyEnabled = true;
        public float familyRadius = 35f;               // tiles between beds
        public float familyHostileOpinion = -20f;      // opinion at or below this counts as negative relations
        public float familyShareBonus = 2f;            // mood points for sharing a room with family you get on with
        public float familyHostilePenalty = 2f;        // mirrors the "too close to a relative" thought (less than for a stranger)
        public float familyFarPenalty = 4f;            // mirrors the "room far from family" thought
        // Neighbours: who sleeps near you, within the same family distance
        public bool neighborsEnabled = true;
        public float neighborHostilePenalty = 4f;      // each disliked colonist (up to two) whose bed is near
        public float ideoNeighborBonus = 2f;           // someone of your ideoligion sleeps near
        public float geneNeighborBonus = 2f;           // someone of your xenotype sleeps near
        public float prisonerNeighborPenalty = 3f;     // an occupied prisoner bed within the family distance
        public bool freeBedMoveEnabled = true;         // a colonist with a sleep problem moves to a better free bed
        public bool coupleHousingEnabled = true;       // move a new couple into a room of their own
        public bool coupleNegotiationEnabled = true;   // a couple negotiates together, the better talker asking
        public float coupleRoomBonus = 3f;             // a room for just the two of them
        public float coupleSharePenalty = 3f;          // a couple sharing a room with anyone else

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
        public float messageSeconds = 4f;              // how long the top-left notification stays before it fades
        public bool showInPlayLog = true;              // also what Interaction Bubbles displays


        // Sleeping rough
        public bool chairSleepEnabled = true;
        public float chairSleepPenalty = 3f;           // mood for a night in a chair (the floor is 4)

        // Guests (Hospitality)
        public bool visitorsUseGuestBeds = true;       // visitors who are not Hospitality guests rent a guest bed

        // Interface
        public bool showRoomsButton = true;            // the Assign rooms button on the Assign tab

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);

            Scribe_Values.Look(ref roomMoodWeight, "roomMoodWeight", 1f);
            Scribe_Values.Look(ref restEffectivenessWeight, "restEffectivenessWeight", 10f);
            Scribe_Values.Look(ref unmetRequirementPenalty, "unmetRequirementPenalty", 6f);
            Scribe_Values.Look(ref titleRulesEnabled, "titleRulesEnabled", true);
            Scribe_Values.Look(ref jealousyEnabled, "jealousyEnabled", true);
            Scribe_Values.Look(ref temperatureEnabled, "temperatureEnabled", true);
            Scribe_Values.Look(ref temperaturePenalty, "temperaturePenalty", 4f);

            Scribe_Values.Look(ref familyEnabled, "familyEnabled", true);
            Scribe_Values.Look(ref familyRadius, "familyRadius", 35f);
            Scribe_Values.Look(ref familyHostileOpinion, "familyHostileOpinion", -20f);
            Scribe_Values.Look(ref familyShareBonus, "familyShareBonus", 2f);
            Scribe_Values.Look(ref familyHostilePenalty, "familyHostilePenaltyV2", 2f);
            Scribe_Values.Look(ref familyFarPenalty, "familyFarPenalty", 4f);
            Scribe_Values.Look(ref neighborsEnabled, "neighborsEnabled", true);
            Scribe_Values.Look(ref neighborHostilePenalty, "neighborHostilePenalty", 4f);
            Scribe_Values.Look(ref ideoNeighborBonus, "ideoNeighborBonus", 2f);
            Scribe_Values.Look(ref geneNeighborBonus, "geneNeighborBonus", 2f);
            Scribe_Values.Look(ref prisonerNeighborPenalty, "prisonerNeighborPenalty", 3f);
            Scribe_Values.Look(ref freeBedMoveEnabled, "freeBedMoveEnabled", true);
            Scribe_Values.Look(ref coupleHousingEnabled, "coupleHousingEnabled", true);
            Scribe_Values.Look(ref coupleNegotiationEnabled, "coupleNegotiationEnabled", true);
            Scribe_Values.Look(ref coupleRoomBonus, "coupleRoomBonus", 3f);
            Scribe_Values.Look(ref coupleSharePenalty, "coupleSharePenalty", 3f);

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
            Scribe_Values.Look(ref messageSeconds, "messageDuration", 4f);
            Scribe_Values.Look(ref showInPlayLog, "showInPlayLog", true);

            Scribe_Values.Look(ref chairSleepEnabled, "chairSleepEnabled", true);
            Scribe_Values.Look(ref chairSleepPenalty, "chairSleepPenalty", 3f);
            Scribe_Values.Look(ref showRoomsButton, "showRoomsButton", true);
            Scribe_Values.Look(ref visitorsUseGuestBeds, "visitorsUseGuestBeds", true);
        }

        /// <summary>Puts every setting back to the value in its field initializer.</summary>
        public void ResetToDefaults()
        {
            var fresh = new WilsBetterBedsSettings();
            foreach (var field in typeof(WilsBetterBedsSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                field.SetValue(this, field.GetValue(fresh));
            }
        }
    }
}
