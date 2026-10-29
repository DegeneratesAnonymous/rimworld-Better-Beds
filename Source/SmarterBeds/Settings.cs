using Verse;

namespace SmarterBeds
{
    public class SmarterBedsSettings : ModSettings
    {
        public bool enabled = true;
        public bool verboseLogging = false;

        // Score = impressiveness * w + private bonus + restEffectiveness * w
        //         - distance * w - overflow penalty
        public float impressivenessWeight = 1f;
        public float privateRoomBonus = 100f;
        public float restEffectivenessWeight = 100f;
        public float distanceWeight = 0.25f;
        public float overflowPenalty = 1000f;

        // Requirements and negotiation
        public float unmetRequirementPenalty = 2000f;
        public bool negotiationEnabled = true;
        public bool qualityNegotiationEnabled = true;
        public float qualityNegotiationThreshold = 250f;
        public float negotiationCooldownDays = 5f;
        public bool lostRoomThought = false;
        public bool negotiationMessages = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            Scribe_Values.Look(ref impressivenessWeight, "impressivenessWeight", 1f);
            Scribe_Values.Look(ref privateRoomBonus, "privateRoomBonus", 100f);
            Scribe_Values.Look(ref restEffectivenessWeight, "restEffectivenessWeight", 100f);
            Scribe_Values.Look(ref distanceWeight, "distanceWeight", 0.25f);
            Scribe_Values.Look(ref overflowPenalty, "overflowPenalty", 1000f);
            Scribe_Values.Look(ref unmetRequirementPenalty, "unmetRequirementPenalty", 2000f);
            Scribe_Values.Look(ref negotiationEnabled, "negotiationEnabled", true);
            Scribe_Values.Look(ref qualityNegotiationEnabled, "qualityNegotiationEnabled", true);
            Scribe_Values.Look(ref qualityNegotiationThreshold, "qualityNegotiationThreshold", 250f);
            Scribe_Values.Look(ref negotiationCooldownDays, "negotiationCooldownDays", 5f);
            Scribe_Values.Look(ref lostRoomThought, "lostRoomThought", false);
            Scribe_Values.Look(ref negotiationMessages, "negotiationMessages", true);
        }
    }
}
