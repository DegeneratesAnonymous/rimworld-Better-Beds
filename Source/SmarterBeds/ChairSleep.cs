using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Last resort for a colonist with no bed to use: doze off in a chair instead of on the floor.
    /// The game's own fallback is "lie down where you stand" (and the "slept on the ground" thought,
    /// -4). We only ever swap that fallback for a chair; a colonist who has a bed is never touched.
    /// </summary>
    public static class ChairSleep
    {
        public const string JobDefName = "SmarterBeds_SleepInChair";
        public const string ThoughtDefName = "SmarterBeds_SleptInChair";

        public static JobDef JobDef => DefDatabase<JobDef>.GetNamedSilentFail(JobDefName);

        public static bool Eligible(Pawn pawn)
        {
            if (!SmarterBedsMod.Settings.enabled || !SmarterBedsMod.Settings.chairSleepEnabled) return false;
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead) return false;
            if (!pawn.RaceProps.Humanlike || !pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave) return false;
            if (pawn.HostFaction != null) return false;
            if ((pawn.DevelopmentalStage & DevelopmentalStage.Adult) == 0) return false;
            return true;
        }

        /// <summary>The most comfortable free chair indoors that this pawn can get to, or null.</summary>
        public static Building FindChair(Pawn pawn)
        {
            Building best = null;
            float bestScore = float.MinValue;

            foreach (var b in pawn.Map.listerBuildings.allBuildingsColonist)
            {
                if (b.def.building == null || !b.def.building.isSittable) continue;
                if (!b.Spawned || b.IsForbidden(pawn) || b.IsBurning()) continue;

                Room room = b.GetRoom();
                if (room == null || room.PsychologicallyOutdoors) continue; // that would be sleeping outside

                Pawn standing = b.Position.GetFirstPawn(b.Map);
                if (standing != null && standing != pawn) continue;
                if (!pawn.CanReserveAndReach(b, PathEndMode.OnCell, Danger.None)) continue;

                float score = b.GetStatValue(StatDefOf.Comfort) * 10f - (b.Position - pawn.Position).LengthHorizontal * 0.1f;
                if (b.Position == pawn.Position) score += 100f; // already dozing in this one: stay put
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best;
        }
    }

    /// <summary>Sleep in a chair. Reuses the game's lie-down job, so rest, comfort and waking all work as usual.</summary>
    public class JobDriver_SleepInChair : JobDriver_LayDown
    {
        private Thing Chair => job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Hold the chair itself so nobody else sits down on a sleeping colonist.
            return pawn.Reserve(Chair, job, 1, -1, null, errorOnFailed) && base.TryMakePreToilReservations(errorOnFailed);
        }

        public override Toil LayDownToil(bool hasBed)
        {
            // No sitting posture exists in the game, so an upright sleeper is the closest to dozing in a chair.
            var toil = Toils_LayDown.LayDown(TargetIndex.A, false, LookForOtherJobs, CanSleep, CanRest, PawnPosture.Standing);
            toil.FailOn(() => Chair == null || !Chair.Spawned);
            return toil;
        }

        public override string GetReport()
        {
            return asleep ? "ReportSleeping".Translate() : "SmarterBeds_ReportDozing".Translate();
        }
    }

    /// <summary>
    /// The game's fallback for a pawn with no bed is a lie-down job at a cell. If a chair is free,
    /// make it a chair instead. Runs last so every other mod's bed logic has had its say.
    /// </summary>
    [HarmonyPatch]
    public static class GetRest_ChairPatch
    {
        static bool Prepare() => AccessTools.Method(typeof(JobGiver_GetRest), "TryGiveJob") != null;
        static MethodBase TargetMethod() => AccessTools.Method(typeof(JobGiver_GetRest), "TryGiveJob");

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (__result == null || __result.def != JobDefOf.LayDown) return;
                if (__result.targetA.Thing != null) return;                    // a real bed: not our business
                if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.LayDown) return; // already resting where they are
                if (!ChairSleep.Eligible(pawn)) return;

                var chairJob = ChairSleep.JobDef;
                if (chairJob == null) return;
                var chair = ChairSleep.FindChair(pawn);
                if (chair == null) return;

                __result = JobMaker.MakeJob(chairJob, chair.Position, chair);
            }
            catch (System.Exception e) { SafeLog.Once("sleeping in a chair", e); }
        }
    }

    /// <summary>
    /// When the game works out how someone slept, a chair sleeper would be told "slept on the ground".
    /// Swap that for the milder chair thought.
    /// </summary>
    [HarmonyPatch]
    public static class ApplyBedThoughts_ChairPatch
    {
        static bool Prepare() => AccessTools.Method(typeof(Toils_LayDown), "ApplyBedThoughts") != null;
        static MethodBase TargetMethod() => AccessTools.Method(typeof(Toils_LayDown), "ApplyBedThoughts");

        static void Postfix(Pawn actor)
        {
            try
            {
                if (!(actor?.jobs?.curDriver is JobDriver_SleepInChair)) return;
                var memories = actor.needs?.mood?.thoughts?.memories;
                if (memories == null) return;

                memories.RemoveMemoriesOfDef(ThoughtDefOf.SleptOnGround);
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(ChairSleep.ThoughtDefName);
                if (def != null) memories.TryGainMemory(def);
            }
            catch (System.Exception e) { SafeLog.Once("chair sleep thought", e); }
        }
    }
}
