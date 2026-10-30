using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Offers "negotiate for a better room" as a recreation choice, but only to a pawn who has a
    /// reason to (a sleep-related mood debuff) and someone to talk to.
    /// </summary>
    public class JoyGiver_NegotiateRoom : JoyGiver
    {
        public override float GetChance(Pawn pawn)
        {
            var comp = GameComponent_BedNegotiation.Instance;
            if (comp == null || !comp.WantsToNegotiate(pawn)) return 0f;
            return base.GetChance(pawn) * SmarterBedsMod.Settings.negotiationJoyWeight;
        }

        public override Job TryGiveJob(Pawn pawn)
        {
            var target = GameComponent_BedNegotiation.Instance?.FindNegotiationTarget(pawn);
            if (target == null) return null;
            return JobMaker.MakeJob(def.jobDef, target);
        }
    }

    /// <summary>Walk over, make the pitch, talk for a while, then settle the room question.</summary>
    public class JobDriver_NegotiateRoom : JobDriver
    {
        private Pawn Target => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Target.Awake() || Target.Downed);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // Arrived: this is the moment the asker speaks, so it is when the bubble appears.
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () => GameComponent_BedNegotiation.Instance?.BeginConversation(pawn, Target)
            };

            var talk = Toils_General.Wait(job.def.joyDuration, TargetIndex.A);
            talk.tickAction = () =>
            {
                pawn.rotationTracker.FaceTarget(Target);
                // Joy and Social experience come from the job definition (joyKind, joySkill, joyXpPerTick).
                JoyUtility.JoyTickCheckEnd(pawn, 1, JoyTickFullJoyAction.EndJob, 1f, null);
            };
            talk.socialMode = RandomSocialMode.Off;
            yield return talk;

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () => GameComponent_BedNegotiation.Instance?.ResolveNegotiation(pawn, Target)
            };
        }
    }
}
