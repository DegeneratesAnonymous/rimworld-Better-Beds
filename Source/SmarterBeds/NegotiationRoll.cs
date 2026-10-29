using RimWorld;
using UnityEngine;
using Verse;

namespace SmarterBeds
{
    public enum NegotiationApproach { Persuade, Intimidate }

    /// <summary>
    /// The contested roll behind a room negotiation. Each side rolls a d20 and adds a modifier; the
    /// asker needs to beat the defender (ties go to the defender, who is holding the room).
    ///
    /// Asker modifier  = best approach + title + time in the colony
    ///   Persuade   = Social skill + how much the defender likes them (opinion / 10)
    ///   Intimidate = Melee skill x 0.75 (needs a pawn who can do violence)
    /// Defender modifier = the matching skill (Social vs persuasion, Melee vs intimidation)
    ///                     + title + time in the colony + a small bonus for already holding the room
    /// Title adds (seniority / 100) x the title weight setting, so a Yeoman is +3 and an Emperor +30
    /// at the default weight of 3.
    /// </summary>
    public struct NegotiationContest
    {
        public NegotiationApproach approach;
        public float askerMod;
        public float defenderMod;
        public float winChance;
        public bool possible;
    }

    public static class NegotiationRoll
    {
        private const float IntimidationFactor = 0.75f;
        private const float DefenderBonus = 3f;
        private const float MaxTenurePoints = 5f;

        private static float TitlePoints(Pawn p, SmarterBedsSettings s) =>
            Requirements.Rank(p) / 100f * s.titleRollWeight;

        // One point per 30 days in the colony, capped.
        private static float TenurePoints(Pawn p)
        {
            float days = p.records.GetAsInt(RecordDefOf.TimeAsColonistOrColonyAnimal) / (float)GenDate.TicksPerDay;
            return Mathf.Min(MaxTenurePoints, days / 30f);
        }

        private static float Level(Pawn p, SkillDef skill)
        {
            var rec = p.skills?.GetSkill(skill);
            return rec == null || rec.TotallyDisabled ? 0f : rec.Level;
        }

        public static NegotiationContest Build(Pawn asker, Pawn defender, SmarterBedsSettings s)
        {
            bool canTalk = asker.skills != null && !asker.skills.GetSkill(SkillDefOf.Social).TotallyDisabled;
            bool canFight = asker.skills != null
                && !asker.skills.GetSkill(SkillDefOf.Melee).TotallyDisabled
                && !asker.WorkTagIsDisabled(WorkTags.Violent);

            float opinion = defender.relations != null ? defender.relations.OpinionOf(asker) / 10f : 0f;
            float persuade = canTalk ? Level(asker, SkillDefOf.Social) + opinion : float.MinValue;
            float intimidate = canFight ? Level(asker, SkillDefOf.Melee) * IntimidationFactor : float.MinValue;

            var c = new NegotiationContest { possible = canTalk || canFight };
            if (!c.possible) return c;

            c.approach = intimidate > persuade ? NegotiationApproach.Intimidate : NegotiationApproach.Persuade;
            float askerSkill = c.approach == NegotiationApproach.Intimidate ? intimidate : persuade;
            float defence = Level(defender, c.approach == NegotiationApproach.Intimidate ? SkillDefOf.Melee : SkillDefOf.Social);

            c.askerMod = Mathf.Round(askerSkill + TitlePoints(asker, s) + TenurePoints(asker));
            c.defenderMod = Mathf.Round(defence + TitlePoints(defender, s) + TenurePoints(defender) + DefenderBonus);
            c.winChance = WinChance(c.askerMod, c.defenderMod);
            return c;
        }

        /// <summary>Exact probability that d20 + askerMod beats d20 + defenderMod.</summary>
        public static float WinChance(float askerMod, float defenderMod)
        {
            int wins = 0;
            for (int a = 1; a <= 20; a++)
                for (int d = 1; d <= 20; d++)
                    if (a + askerMod > d + defenderMod) wins++;
            return wins / 400f;
        }

        /// <summary>Rolls the contest. Returns whether the asker won; totals are for display.</summary>
        public static bool Roll(NegotiationContest c, out int askerTotal, out int defenderTotal)
        {
            askerTotal = Rand.RangeInclusive(1, 20) + (int)c.askerMod;
            defenderTotal = Rand.RangeInclusive(1, 20) + (int)c.defenderMod;
            return askerTotal > defenderTotal;
        }
    }
}
