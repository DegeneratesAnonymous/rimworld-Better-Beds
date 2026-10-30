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
            try
            {
                var comp = GameComponent_BedNegotiation.Instance;
                if (comp == null || !comp.WantsToNegotiate(pawn)) return 0f;
                return base.GetChance(pawn) * SmarterBedsMod.Settings.negotiationJoyWeight;
            }
            catch (System.Exception e)
            {
                SafeLog.Once("negotiation as recreation (chance)", e);
                return 0f;
            }
        }

        public override Job TryGiveJob(Pawn pawn)
        {
            try
            {
                var target = GameComponent_BedNegotiation.Instance?.FindNegotiationTarget(pawn);
                if (target == null) return null;
                return JobMaker.MakeJob(def.jobDef, target);
            }
            catch (System.Exception e)
            {
                SafeLog.Once("negotiation as recreation (job)", e);
                return null;
            }
        }
    }

    /// <summary>
    /// The colonist being asked stops what they are doing and listens, facing the asker, for as long
    /// as the conversation lasts; their previous job resumes afterwards. Without this the asker would
    /// walk up to someone who just carries on walking.
    /// </summary>
    public static class TalkHold
    {
        public const string JobDefName = "SmarterBeds_ListenToRoomPitch";

        public static void Start(Pawn listener, Pawn speaker, int ticks)
        {
            try
            {
                if (listener == null || speaker == null || !listener.Spawned) return;
                if (listener.Drafted || listener.InMentalState || listener.Downed || !listener.Awake()) return;

                var def = DefDatabase<JobDef>.GetNamedSilentFail(JobDefName);
                if (def == null || listener.CurJobDef == def) return;

                var job = JobMaker.MakeJob(def, speaker);
                job.expiryInterval = ticks;
                listener.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
            }
            catch (System.Exception e) { SafeLog.Once("making the other colonist stop to listen", e); }
        }

        /// <summary>Lets them get back to what they were doing as soon as the conversation is over.</summary>
        public static void Release(Pawn listener)
        {
            try
            {
                if (listener?.CurJobDef != null && listener.CurJobDef.defName == JobDefName)
                    listener.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
            catch (System.Exception e) { SafeLog.Once("letting the other colonist go", e); }
        }
    }

    /// <summary>Stand still, face the person talking, wait.</summary>
    public class JobDriver_ListenToPitch : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            // Wait(ticks, face) keeps them turned towards the speaker for the whole wait.
            yield return Toils_General.Wait(job.expiryInterval > 0 ? job.expiryInterval : 800, TargetIndex.A);
        }
    }

    /// <summary>Walk over, make the pitch, talk for a while (both of them standing still), then settle it.</summary>
    public class JobDriver_NegotiateRoom : JobDriver
    {
        private const int StopDistanceSquared = 36; // the other colonist stops once the asker is within 6 cells
        private bool listenerHeld;

        private Pawn Target => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        private int TalkTicks => job.def.joyDuration + 240;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Target == null || !Target.Awake() || Target.Downed);
            // However this ends, let the other colonist go.
            this.AddFinishAction(condition => TalkHold.Release(Target));

            var walk = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            walk.AddPreTickAction(() =>
            {
                // Once the asker is close, the other colonist stops rather than wandering off.
                if (!listenerHeld && Target != null && (pawn.Position - Target.Position).LengthHorizontalSquared <= StopDistanceSquared)
                {
                    listenerHeld = true;
                    TalkHold.Start(Target, pawn, TalkTicks);
                }
            });
            yield return walk;

            // Arrived: this is the moment the asker speaks, so it is when the bubble appears.
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    if (!listenerHeld) { listenerHeld = true; TalkHold.Start(Target, pawn, TalkTicks); }
                    GameComponent_BedNegotiation.Instance?.BeginConversation(pawn, Target);
                }
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
                initAction = () =>
                {
                    GameComponent_BedNegotiation.Instance?.ResolveNegotiation(pawn, Target, job.count == 1);
                    TalkHold.Release(Target);
                }
            };
        }
    }
}
