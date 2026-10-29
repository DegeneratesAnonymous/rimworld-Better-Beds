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
        }
    }
}
