using RimWorld;
using UnityEngine;
using Verse;

namespace WilsBetterBeds
{
    public enum NegotiationApproach { Persuade, Intimidate }

    /// <summary>
    /// The contested roll behind a room negotiation. Each side rolls a d20 and adds a modifier; the
    /// asker needs to beat the defender (ties go to the defender, who is holding the room).
    ///
    /// Asker modifier  = best approach + title + time in the colony
    ///   Persuade   = Social skill + how much the defender likes them (opinion / 10, so -10..+10)
    ///   Intimidate = Melee skill x 0.75. Needs a pawn who can do violence, and is not something a
    ///                kind pawn, or someone talking to a friend, will do. Chosen only when it is
    ///                clearly stronger than persuading.
    /// Defender modifier = the matching skill (Social vs persuasion, Melee vs intimidation)
    ///                     + title + time in the colony + a bonus for already holding the room
    /// Title adds (seniority / 100) x the title weight: at 3, a Yeoman is +3 and a Baron +15.
    ///
    /// Tuned with a simulation over typical colonists (see PLAN.md): equal pawns win about 34%,
    /// one title step is worth roughly +13 points of win chance, three steps is a near lock,
    /// and a +/-50 opinion moves the odds from 17% to 57%.
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
        private const float TenureDaysPerPoint = 45f;
        private const float IntimidationMargin = 2f;     // must beat persuasion by this much to be chosen
        private const float FriendOpinion = 40f;         // won't bully someone they like this much

        private static float TitlePoints(Pawn p, WilsBetterBedsSettings s) =>
            Requirements.Rank(p) / 100f * s.titleRollWeight;

        // One point per 45 days in the colony, up to the cap.
        private static float TenurePoints(Pawn p, WilsBetterBedsSettings s)
        {
            float days = p.records.GetAsInt(RecordDefOf.TimeAsColonistOrColonyAnimal) / (float)GenDate.TicksPerDay;
            return Mathf.Min(s.tenureCap, days / TenureDaysPerPoint);
        }

        /// <summary>Social skill, or -1 for someone who cannot talk. Used to decide which of a couple negotiates.</summary>
        public static float SocialLevel(Pawn p)
        {
            var rec = p.skills?.GetSkill(SkillDefOf.Social);
            return rec == null || rec.TotallyDisabled ? -1f : rec.Level;
        }

        private static float Level(Pawn p, SkillDef skill)
        {
            var rec = p.skills?.GetSkill(skill);
            return rec == null || rec.TotallyDisabled ? 0f : rec.Level;
        }

        private static bool WillIntimidate(Pawn asker, Pawn defender)
        {
            if (asker.skills == null || asker.skills.GetSkill(SkillDefOf.Melee).TotallyDisabled) return false;
            if (asker.WorkTagIsDisabled(WorkTags.Violent)) return false;
            if (asker.story?.traits != null && asker.story.traits.HasTrait(TraitDefOf.Kind)) return false;
            if (asker.relations != null && asker.relations.OpinionOf(defender) >= FriendOpinion) return false;
            return true;
        }

        /// <param name="convince">
        /// One of a couple persuading the other to move. The partner is not holding a room against them, so
        /// there is no defender bonus, and leaning on them is only an option when they do not think well of the asker.
        /// </param>
        public static NegotiationContest Build(Pawn asker, Pawn defender, WilsBetterBedsSettings s, bool convince = false)
        {
            bool canTalk = asker.skills != null && !asker.skills.GetSkill(SkillDefOf.Social).TotallyDisabled;
            bool canBully = WillIntimidate(asker, defender);
            if (convince && canBully && defender.relations != null && defender.relations.OpinionOf(asker) > 0) canBully = false;

            float opinion = defender.relations != null ? defender.relations.OpinionOf(asker) / 10f : 0f;
            float persuade = canTalk ? Level(asker, SkillDefOf.Social) + opinion : float.MinValue;
            float intimidate = canBully ? Level(asker, SkillDefOf.Melee) * s.intimidationFactor : float.MinValue;

            var c = new NegotiationContest { possible = canTalk || canBully };
            if (!c.possible) return c;

            bool bully = intimidate > persuade + IntimidationMargin || !canTalk;
            c.approach = bully ? NegotiationApproach.Intimidate : NegotiationApproach.Persuade;
            float askerSkill = bully ? intimidate : persuade;
            float defence = Level(defender, bully ? SkillDefOf.Melee : SkillDefOf.Social);

            c.askerMod = Mathf.Round(askerSkill + TitlePoints(asker, s) + TenurePoints(asker, s));
            c.defenderMod = Mathf.Round(defence + TitlePoints(defender, s) + TenurePoints(defender, s) + (convince ? 0f : s.defenderBonus));
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
