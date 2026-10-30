using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SmarterBeds
{
    /// <summary>
    /// Room negotiation. A pawn who owns a bed and has a sleep-related mood debuff (or an unmet title
    /// requirement) can pick "negotiate for a better room" as a recreation activity (see
    /// JoyGiver_NegotiateRoom). They walk over, talk, and a contested roll (see NegotiationRoll)
    /// decides it. A winner takes the loser's bed; the loser is left bedless to find a room at
    /// their own bedtime.
    ///
    /// The player always has the last word: a bed whose owner the player set is never
    /// negotiated for, and its owner does not negotiate either.
    /// </summary>
    public class GameComponent_BedNegotiation : GameComponent
    {
        public static GameComponent_BedNegotiation Instance => Current.Game?.GetComponent<GameComponent_BedNegotiation>();

        // Saved: pair of pawn ids -> tick when these two may negotiate with each other again.
        // (Keys are two ids packed into one number: no strings are built while searching.)
        private Dictionary<long, int> pairCooldownUntil = new Dictionary<long, int>();

        // Saved: same, for the housing check on a couple.
        private Dictionary<long, int> coupleCooldownUntil = new Dictionary<long, int>();

        // Saved: loser/winner ids -> tick until which the loser may not challenge that winner again.
        private Dictionary<long, int> rematchUntil = new Dictionary<long, int>();

        // Saved: bed id -> id of the owner the player set with the Set owner button (see BedLocks).
        private Dictionary<int, int> lockedBeds = new Dictionary<int, int>();

        // Mood effect of a negotiation = 0.25 x relationship + 0.25 x room quality, in mood points.
        // Relationship is the loser's opinion of the winner / 10 (-10..+10). Room quality is the
        // mood gained by the move, capped. Never less than 1 and never more than the cap.
        private const float MoodWeight = 0.25f;
        private const float RoomQualityCap = 12f;
        private const float MoodFloor = 1f;
        private const float MoodCap = 8f;

        // Not saved: stops a pawn from re-running the search every time the joy system asks.
        private readonly Dictionary<int, int> lastChecked = new Dictionary<int, int>();
        private const int RecheckTicks = GenDate.TicksPerHour;

        public GameComponent_BedNegotiation(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % GenDate.TicksPerHour != 0) return;
            PruneLocks();
            PruneCooldowns();
            var s = SmarterBedsMod.Settings;
            if (!s.enabled || !s.familyEnabled || !s.coupleHousingEnabled) return;

            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                try { CoupleHousing.Run(map, this); }
                catch (System.Exception e) { SafeLog.Once("couple housing", e); }
            }
        }

        // ---- the player's locks

        public bool IsLocked(Building_Bed bed, Pawn owner) =>
            bed != null && owner != null
            && lockedBeds.TryGetValue(bed.thingIDNumber, out int id) && id == owner.thingIDNumber
            && bed.OwnersForReading.Contains(owner);

        public bool IsLockedForOwner(Building_Bed bed)
        {
            if (bed == null || !lockedBeds.TryGetValue(bed.thingIDNumber, out int id)) return false;
            foreach (var owner in bed.OwnersForReading)
            {
                if (owner.thingIDNumber == id) return true;
            }
            return false;
        }

        public void Lock(Building_Bed bed, Pawn owner) => lockedBeds[bed.thingIDNumber] = owner.thingIDNumber;

        public void Release(Building_Bed bed, Pawn owner)
        {
            if (lockedBeds.TryGetValue(bed.thingIDNumber, out int id) && id == owner.thingIDNumber)
                lockedBeds.Remove(bed.thingIDNumber);
        }

        /// <summary>Forget locks on beds that no longer exist anywhere.</summary>
        private void PruneLocks()
        {
            if (lockedBeds.Count == 0) return;
            var live = new HashSet<int>();
            foreach (var map in Find.Maps)
            {
                foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()) live.Add(bed.thingIDNumber);
            }
            foreach (var id in lockedBeds.Keys.Where(k => !live.Contains(k)).ToList()) lockedBeds.Remove(id);
        }

        public bool CoupleOnCooldown(Pawn a, Pawn b) =>
            coupleCooldownUntil.TryGetValue(PairKey(a, b), out int until) && Now < until;

        public void StartCoupleCooldown(Pawn a, Pawn b, float days) =>
            coupleCooldownUntil[PairKey(a, b)] = Now + (int)(days * GenDate.TicksPerDay);

        // Caches in this file are per game; the shared ones are cleared whenever a game loads, because
        // pawn ids are reused between saves.
        public override void LoadedGame() { ResetCaches(); }
        public override void StartedNewGame() { ResetCaches(); }

        private void ResetCaches()
        {
            Family.ClearCache();
            BedScorer.ClearCache();
            movedAt.Clear();
            lastChecked.Clear();
            wantsCache.Clear();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pairCooldownUntil, "pairCooldowns", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref coupleCooldownUntil, "coupleCooldowns", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref rematchUntil, "rematches", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lockedBeds, "lockedBeds", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (lockedBeds == null) lockedBeds = new Dictionary<int, int>();
                if (pairCooldownUntil == null) pairCooldownUntil = new Dictionary<long, int>();
                if (coupleCooldownUntil == null) coupleCooldownUntil = new Dictionary<long, int>();
                if (rematchUntil == null) rematchUntil = new Dictionary<long, int>();
            }
        }

        private static int Now => Find.TickManager.TicksGame;

        private static long Pack(int first, int second) => ((long)first << 32) | (uint)second;

        /// <summary>The same number whichever of the two is given first.</summary>
        private static long PairKey(Pawn a, Pawn b) =>
            a.thingIDNumber < b.thingIDNumber ? Pack(a.thingIDNumber, b.thingIDNumber) : Pack(b.thingIDNumber, a.thingIDNumber);

        /// <summary>Order matters: this is "loser may not challenge winner".</summary>
        private static long RematchKey(Pawn loser, Pawn winner) => Pack(loser.thingIDNumber, winner.thingIDNumber);

        private bool PairOnCooldown(Pawn a, Pawn b) =>
            pairCooldownUntil.TryGetValue(PairKey(a, b), out int until) && Now < until;

        private void StartPairCooldown(Pawn a, Pawn b) =>
            pairCooldownUntil[PairKey(a, b)] = Now + (int)(SmarterBedsMod.Settings.negotiationCooldownDays * GenDate.TicksPerDay);

        // Memory thoughts that mean "the place I slept was bad". SleepDisturbed is noise, not the room.
        // Built on first use rather than when the class loads, so it never reads the game's DefOf
        // fields before they have been filled in. A def missing from this game version is just skipped.
        private static List<ThoughtDef> sleepDebuffMemories;

        private static List<ThoughtDef> SleepDebuffMemories =>
            sleepDebuffMemories ?? (sleepDebuffMemories = new List<ThoughtDef>
            {
                ThoughtDefOf.SleptInBarracks, ThoughtDefOf.SleptOutside, ThoughtDefOf.SleptOnGround,
                ThoughtDefOf.SleptInCold, ThoughtDefOf.SleptInHeat, ThoughtDefOf.SleptInRoomWithSlave
            }.Where(d => d != null).ToList());

        // Situational thoughts about a bedroom: the game's (a title without a personal bedroom or with
        // unmet requirements, the Jealous trait envying someone's room) and this mod's two family ones.
        private static readonly System.Type[] BedroomThoughtWorkers =
        {
            typeof(ThoughtWorker_BedroomRequirementsNotMet),
            typeof(ThoughtWorker_NoPersonalBedroom),
            typeof(ThoughtWorker_BedroomJealous),
            typeof(ThoughtWorker_RoomFarFromFamily),
            typeof(ThoughtWorker_RoomNearDislikedFamily),
            typeof(ThoughtWorker_RoomNearDislikedNeighbor)
        };

        private static readonly List<Thought> tmpThoughts = new List<Thought>();

        /// <summary>
        /// True when the pawn is currently suffering a mood penalty about where they sleep.
        /// <paramref name="includeMemories"/> is off for someone who has just moved: last night's
        /// "slept in barracks" memory is about the room they left, and says nothing about the new one.
        /// </summary>
        public static bool HasSleepDebuff(Pawn p, bool includeMemories = true)
        {
            var handler = p.needs?.mood?.thoughts;
            if (handler == null) return false;

            if (includeMemories)
            {
                var memories = handler.memories;
                foreach (var def in SleepDebuffMemories)
                {
                    if (memories.GetFirstMemoryOfDef(def) != null) return true;
                }
            }

            tmpThoughts.Clear();
            handler.GetAllMoodThoughts(tmpThoughts);
            try
            {
                foreach (var t in tmpThoughts)
                {
                    var worker = t.def.workerClass;
                    if (worker == null) continue;
                    for (int i = 0; i < BedroomThoughtWorkers.Length; i++)
                    {
                        if (BedroomThoughtWorkers[i].IsAssignableFrom(worker) && t.MoodOffset() < 0f) return true;
                    }
                }
            }
            finally { tmpThoughts.Clear(); }
            return false;
        }

        /// <summary>Common checks for both sides: a settled adult colonist who is the only owner of a bed the player has not locked.</summary>
        private static bool Eligible(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || !p.IsColonist || p.IsPrisoner || p.IsSlave) return false;
            if (p.HostFaction != null) return false;
            if ((p.DevelopmentalStage & DevelopmentalStage.Adult) == 0) return false;
            if (HealthAIUtility.ShouldSeekMedicalRest(p)) return false;
            var bed = p.ownership?.OwnedBed;
            if (bed == null || bed.Medical || !bed.Spawned) return false;
            // Shared beds belong to two people (usually a couple); leave those to vanilla.
            // A double bed with a single owner is fine.
            if (bed.OwnersForReading.Count != 1) return false;
            // The player decided; that is final.
            if (BedLocks.IsLocked(bed, p)) return false;
            return true;
        }

        private static bool AreLovePartners(Pawn a, Pawn b) => Family.Lovers(a).Contains(b);

        /// <summary>Cheap check: is this pawn in a position where negotiating makes sense at all?</summary>
        // Not saved: when each pawn last moved to a new room, so old sleep memories do not count against the new one.
        private readonly Dictionary<int, int> movedAt = new Dictionary<int, int>();
        private const int MemoryIgnoreTicks = GenDate.TicksPerDay;

        public void NoteMoved(Pawn p) => movedAt[p.thingIDNumber] = Now;

        private bool RecentlyMoved(Pawn p) =>
            movedAt.TryGetValue(p.thingIDNumber, out int at) && Now - at < MemoryIgnoreTicks;

        // Not saved. The joy system asks every colonist, many times a minute, whether this is on offer;
        // the honest answer only changes over hours, so it is worked out at most every 250 ticks.
        private readonly Dictionary<int, KeyValuePair<int, bool>> wantsCache = new Dictionary<int, KeyValuePair<int, bool>>();
        private const int WantsCacheTicks = 250;

        public bool WantsToNegotiate(Pawn a)
        {
            var s = SmarterBedsMod.Settings;
            if (!s.enabled || !s.negotiationEnabled) return false;

            int now = Now;
            if (wantsCache.TryGetValue(a.thingIDNumber, out var cached) && now >= cached.Key && now - cached.Key < WantsCacheTicks)
                return cached.Value;

            bool wants = ComputeWants(a);
            wantsCache[a.thingIDNumber] = new KeyValuePair<int, bool>(now, wants);
            return wants;
        }

        /// <summary>The real answer, never cached: used when it must be right (settling a negotiation).</summary>
        private bool ComputeWants(Pawn a)
        {
            if (!Eligible(a)) return false;
            // A couple who have a room of their own are not going to break it up.
            if (Family.IsInCoupleRoom(a)) return false;
            // Only negotiate when something is actually wrong with how they sleep.
            return HasSleepDebuff(a, includeMemories: !RecentlyMoved(a));
        }

        // What a negotiation job is for. Stored in the job's count (the game leaves it at -1).
        public const int ModeForced = 1;        // dev tool: one colonist, ignoring the usual reasons
        public const int ModeConvince = 2;      // one of a couple talking their partner into moving
        public const int ModeCouple = 3;        // a couple asking for someone's room, for both of them
        public const int ModeCoupleForced = 4;  // dev tool version of the above

        private static JobDef NegotiateJobDef => DefDatabase<JobDef>.GetNamedSilentFail("SmarterBeds_NegotiateRoom");

        /// <summary>
        /// The partner of a colonist who is half of a couple that can act together: each is the other's only
        /// love partner and both are settled adults who own a bed the player has not locked.
        /// </summary>
        private static Pawn CoupleOf(Pawn a)
        {
            if (!SmarterBedsMod.Settings.coupleNegotiationEnabled) return null;
            var lover = Family.SoleLover(a);
            if (lover == null || Family.SoleLover(lover) != a) return null;
            if (!Family.IsAdultColonist(lover) || lover.Map != a.Map || !Eligible(lover)) return null;
            return lover;
        }

        /// <summary>Of a couple, whoever talks better. Equal skill: the lower id, so everyone agrees who it is.</summary>
        private static Pawn BetterTalker(Pawn a, Pawn b)
        {
            float x = NegotiationRoll.SocialLevel(a), y = NegotiationRoll.SocialLevel(b);
            if (x != y) return x > y ? a : b;
            return a.thingIDNumber <= b.thingIDNumber ? a : b;
        }

        /// <summary>
        /// What this pawn should do about their room, as a job, or null. Throttled to once an hour per pawn.
        /// - On their own: ask the owner of a better room.
        /// - In a couple where both want to move: the better talker asks, and the room is for both of them.
        /// - In a couple where only this one wants to move: first talk the partner round, and then the
        ///   better talker of the two asks for the room.
        /// </summary>
        public Job MakeNegotiationJob(Pawn a)
        {
            if (!WantsToNegotiate(a)) return null;
            if (lastChecked.TryGetValue(a.thingIDNumber, out int last) && Now >= last && Now - last < RecheckTicks) return null;
            lastChecked[a.thingIDNumber] = Now;

            try
            {
                using (PerfWatch.Measure("negotiation search"))
                    return PlanJob(a, SmarterBedsMod.Settings);
            }
            catch (System.Exception e)
            {
                SafeLog.Once("finding someone to negotiate with", e);
                return null;
            }
        }

        private Job PlanJob(Pawn a, SmarterBedsSettings s)
        {
            var jobDef = NegotiateJobDef;
            if (jobDef == null) return null;

            var partner = CoupleOf(a);
            if (partner == null)
            {
                var loser = FindLoser(a, a.ownership.OwnedBed, s, out _, out _);
                return loser == null ? null : JobMaker.MakeJob(jobDef, loser);
            }

            var negotiator = BetterTalker(a, partner);
            if (ComputeWants(partner))
            {
                // Both want out. The better talker goes; the other leaves it to them.
                if (negotiator != a) return null;
                var target = FindCoupleTarget(a, partner, s, false, out _, out _, out _, out _);
                return target == null ? null : MakeNegotiationJob(jobDef, target, ModeCouple);
            }

            // Only this one wants out: the partner has to be talked round first.
            if (PairOnCooldown(a, partner)) return null;
            var convince = NegotiationRoll.Build(a, partner, s, convince: true);
            if (!convince.possible || convince.winChance < s.minWinChance) return null;
            var other = negotiator == a ? partner : a;
            // No point persuading them if there is no room to ask for afterwards.
            if (FindCoupleTarget(negotiator, other, s, false, out _, out _, out _, out _) == null) return null;
            return MakeNegotiationJob(jobDef, partner, ModeConvince);
        }

        private static Job MakeNegotiationJob(JobDef def, Pawn target, int mode)
        {
            var job = JobMaker.MakeJob(def, target);
            job.count = mode;
            return job;
        }

        /// <summary>
        /// Dev tool: send this pawn to negotiate for a better room right now, skipping the usual
        /// reasons, cooldowns and odds checks. They still walk over and roll like anyone else.
        /// </summary>
        public void ForceNegotiation(Pawn a)
        {
            var s = SmarterBedsMod.Settings;
            string problem = null;
            Pawn target = null;

            int mode = ModeForced;
            if (!Eligible(a)) problem = "SmarterBeds_Force_NotEligible";
            else
            {
                var partner = CoupleOf(a);
                if (partner != null)
                {
                    mode = ModeCoupleForced;
                    target = FindCoupleTarget(a, partner, s, true, out _, out _, out _, out _);
                }
                else target = FindLoser(a, a.ownership.OwnedBed, s, out _, out _, force: true);
                if (target == null) problem = "SmarterBeds_Force_NoTarget";
            }

            if (problem != null)
            {
                Messages.Message(problem.Translate(a.LabelShort), a, MessageTypeDefOf.RejectInput, false);
                return;
            }

            var jobDef = NegotiateJobDef;
            if (jobDef == null) return;
            a.jobs.TryTakeOrderedJob(MakeNegotiationJob(jobDef, target, mode), JobTag.Misc);
        }

        /// <summary>The asker has walked over: they make their pitch (a bubble over their head).</summary>
        public void BeginConversation(Pawn a, Pawn b, int mode = 0)
        {
            try
            {
                bool convince = mode == ModeConvince;
                var contest = NegotiationRoll.Build(a, b, SmarterBedsMod.Settings, convince);
                bool bully = contest.approach == NegotiationApproach.Intimidate;
                string line = convince ? (bully ? "SmarterBeds_PressureToMove" : "SmarterBeds_ConvinceToMove")
                                       : (bully ? "SmarterBeds_IntimidateForRoom" : "SmarterBeds_AskForRoom");
                NegotiationLog.Say(line, a, b);
            }
            catch (System.Exception e) { SafeLog.Once("starting a conversation", e); }
        }

        /// <summary>
        /// End of the conversation: re-checks everything (the world moved while they walked over),
        /// rolls, and swaps on a win. The two are on cooldown with each other afterwards either way.
        /// </summary>
        public void ResolveNegotiation(Pawn a, Pawn b, int mode = 0)
        {
            if (mode == ModeConvince) { ResolveConvince(a, b); return; }
            if (mode == ModeCouple || mode == ModeCoupleForced) { ResolveCouple(a, b, mode == ModeCoupleForced); return; }

            bool forced = mode == ModeForced;
            var s = SmarterBedsMod.Settings;
            try
            {
                if ((forced ? Eligible(a) : ComputeWants(a)) && Eligible(b))
                {
                    var target = FindLoser(a, a.ownership.OwnedBed, s, out float gain, out var contest, only: b, force: forced);
                    if (target == b)
                    {
                        bool won = NegotiationRoll.Roll(contest, out int askerTotal, out int defenderTotal);
                        GiveSocialExperience(a, won, s);
                        if (contest.approach == NegotiationApproach.Intimidate) Resent(b, a);
                        if (won) Swap(a, b, gain, s, contest, askerTotal, defenderTotal);
                        else Refuse(a, b, contest, askerTotal, defenderTotal, s);
                    }
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Once("settling a negotiation", e);
            }
            finally
            {
                StartPairCooldown(a, b);
            }
        }

        private static void GiveSocialExperience(Pawn asker, bool won, SmarterBedsSettings s)
        {
            // Practice either way; half as much when turned down.
            asker.skills?.Learn(SkillDefOf.Social, won ? s.socialXpOnWin : s.socialXpOnWin / 2f);
        }

        /// <summary>Someone who was leaned on remembers it, win or lose.</summary>
        private static void Resent(Pawn victim, Pawn bully)
        {
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("SmarterBeds_PushedAround");
            if (def != null) victim.needs?.mood?.thoughts?.memories?.TryGainMemory(def, bully);
        }

        private static float Relationship(Pawn loser, Pawn winner) =>
            loser.relations != null ? Mathf.Clamp(loser.relations.OpinionOf(winner) / 10f, -10f, 10f) : 0f;

        private static void GainMood(Pawn pawn, string thoughtDefName, float points, Pawn other = null)
        {
            var memories = pawn.needs?.mood?.thoughts?.memories;
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            if (memories == null || def == null) return;
            var thought = (Thought_Memory)ThoughtMaker.MakeThought(def);
            // The stage is worth exactly +/-1 mood; the factor sets how many points this one is.
            thought.moodPowerFactor = points;
            if (other != null) memories.TryGainMemory(thought, other);
            else memories.TryGainMemory(thought);
        }

        /// <summary>
        /// How people feel after a successful negotiation, in mood points: 0.25 x relationship + 0.25 x room quality.
        /// - The loser is bullied out of the room (debuff), or, if they like the winner and were
        ///   talked round, gave it up for a friend (buff). A persuaded loser who does not like the
        ///   winner has no particular feeling either way.
        /// - The winner is pleased with the new room. Only the winner moves to a new room, so only
        ///   the winner gets this; someone who keeps their room is not rewarded for it.
        /// </summary>
        private static void ApplyMoodEffects(Pawn winner, Pawn loser, NegotiationContest c, float gain, SmarterBedsSettings s, Pawn partner = null)
        {
            if (!s.negotiationMoodEffects) return;

            float relationship = Relationship(loser, winner);
            float quality = Mathf.Clamp(gain, 0f, RoomQualityCap);

            if (c.approach == NegotiationApproach.Intimidate)
            {
                float points = Mathf.Clamp(MoodWeight * Mathf.Abs(relationship) + MoodWeight * quality, MoodFloor, MoodCap);
                GainMood(loser, "SmarterBeds_BulliedOutOfRoom", points);
            }
            else if (relationship > 0f)
            {
                float points = Mathf.Clamp(MoodWeight * relationship + MoodWeight * quality, MoodFloor, MoodCap);
                GainMood(loser, "SmarterBeds_GaveUpRoomHappily", points);
            }

            float winnerPoints = Mathf.Clamp(MoodWeight * Mathf.Max(0f, relationship) + MoodWeight * quality, MoodFloor, MoodCap);
            GainMood(winner, "SmarterBeds_GotBetterRoom", winnerPoints);
            if (partner != null) GainMood(partner, "SmarterBeds_GotBetterRoom", winnerPoints); // they moved too

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] mood: {loser.LabelShort} relationship {relationship:0.0}, quality {quality:0.0}; winner +{winnerPoints:0.0}");
        }

        private static void Refuse(Pawn asker, Pawn defender, NegotiationContest c, int askerTotal, int defenderTotal, SmarterBedsSettings s)
        {
            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation failed: {asker.LabelShort} vs {defender.LabelShort} ({c.approach}) {askerTotal} vs {defenderTotal}");

            NegotiationLog.Say("SmarterBeds_RefuseRoom", defender, asker);

            if (s.negotiationMessages)
                BriefMessage.Show("SmarterBeds_RoomFailed".Translate(asker.LabelShort, defender.LabelShort, askerTotal, defenderTotal),
                    new LookTargets(asker, defender), s.messageSeconds);
        }

        private Pawn FindLoser(Pawn a, Building_Bed bedA, SmarterBedsSettings s, out float bestGain, out NegotiationContest bestContest, Pawn only = null, bool force = false)
        {
            bestGain = 0f;
            bestContest = default;
            float bestValue = 0f;
            Pawn best = null;

            float currentA = BedScorer.Quality(bedA, a, s);
            float debuffNow = RoomComfort.DebuffPenalty(bedA, a, s);
            // A free room that is at least as good for A makes taking someone's room pointless.
            float bestFree = BedScorer.BestFreeQuality(a, s);

            // A copy: scoring a bed reads the same game list again, which can change it under this loop.
            foreach (var b in new List<Pawn>(a.Map.mapPawns.FreeColonistsSpawned))
            {
                if (b == a || (only != null && b != only) || !Eligible(b)) continue;
                // Someone asleep, drafted, downed or in a mental break cannot be talked to.
                if (!b.Awake() || b.Downed || b.Drafted || b.InMentalState || PawnBusy.Is(b)) continue;
                var bedB = b.ownership.OwnedBed;
                if (bedA.GetRoom() == bedB.GetRoom()) continue;
                if (!force && PairOnCooldown(a, b)) continue;
                if (!force && rematchUntil.TryGetValue(RematchKey(a, b), out int rematch) && Now < rematch) continue;
                if (AreLovePartners(a, b)) continue; // never split a couple's rooms
                if (Family.IsInCoupleRoom(b)) continue; // nor take a bed out of one

                float gain = BedScorer.Quality(bedB, a, s, leaving: b) - currentA;
                if (gain <= 0f) continue;
                if (!force && bestFree >= currentA + gain) continue;

                // A reason to ask: their room is what causes a title or jealousy penalty and this one would not,
                // or (optional) the move is a big enough upgrade on its own.
                bool fixesAPenalty = RoomComfort.DebuffPenalty(bedB, a, s, leaving: b) < debuffNow;
                bool bigUpgrade = s.qualityNegotiationEnabled && gain >= s.qualityNegotiationThreshold;
                if (!force && !fixesAPenalty && !bigUpgrade) continue;

                // The owner must not be using it right now. Checked last: it asks the pathfinder.
                if (!a.CanReserveAndReach(bedB, PathEndMode.OnCell, Danger.Some, bedB.SleepingSlotsCount)) continue;

                // Anyone may try; rank only matters as a number in the roll.
                var contest = NegotiationRoll.Build(a, b, s);
                // Don't spend a conversation on something with almost no chance.
                if (!contest.possible || contest.winChance < (force ? 0.01f : s.minWinChance)) continue;

                float value = gain * contest.winChance;
                if (value > bestValue)
                {
                    bestValue = value;
                    bestGain = gain;
                    bestContest = contest;
                    best = b;
                }
            }
            return best;
        }

        private bool Swap(Pawn winner, Pawn loser, float gain, SmarterBedsSettings s, NegotiationContest contest, int winnerTotal, int loserTotal)
        {
            var wonBed = loser.ownership.OwnedBed;
            var oldBed = winner.ownership.OwnedBed;

            loser.ownership.UnclaimBed();
            winner.ownership.UnclaimBed();
            if (!winner.ownership.ClaimBedIfNonMedical(wonBed))
            {
                // Could not claim: put everyone back exactly as they were.
                winner.ownership.ClaimBedIfNonMedical(oldBed);
                loser.ownership.ClaimBedIfNonMedical(wonBed);
                return false;
            }

            NoteMoved(winner);
            // The loser can't just turn around and take it back.
            rematchUntil[RematchKey(loser, winner)] = Now + (int)(s.rematchDays * GenDate.TicksPerDay);
            PruneCooldowns();

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] negotiation: {winner.LabelShort} took {loser.LabelShort}'s bed at {wonBed.Position} (gain {gain:0.0})");

            NegotiationLog.Say(contest.approach == NegotiationApproach.Intimidate ? "SmarterBeds_YieldRoom" : "SmarterBeds_AgreeToRoom", loser, winner);
            ApplyMoodEffects(winner, loser, contest, gain, s);

            if (s.negotiationMessages)
            {
                string key = contest.approach == NegotiationApproach.Intimidate ? "SmarterBeds_RoomWonIntimidate" : "SmarterBeds_RoomWonPersuade";
                BriefMessage.Show(key.Translate(winner.LabelShort, loser.LabelShort, winnerTotal, loserTotal),
                    new LookTargets(winner, loser), s.messageSeconds);
            }
            return true;
        }


        // ---- couples

        /// <summary>
        /// The first half of a couple talking their partner into moving. If it works, whichever of the two
        /// talks better goes and asks for the room.
        /// </summary>
        private void ResolveConvince(Pawn wanter, Pawn partner)
        {
            var s = SmarterBedsMod.Settings;
            try
            {
                if (Eligible(wanter) && CoupleOf(wanter) == partner)
                {
                    var contest = NegotiationRoll.Build(wanter, partner, s, convince: true);
                    if (contest.possible)
                    {
                        bool won = NegotiationRoll.Roll(contest, out int askerTotal, out int partnerTotal);
                        GiveSocialExperience(wanter, won, s);
                        bool bully = contest.approach == NegotiationApproach.Intimidate;
                        if (bully) Resent(partner, wanter);

                        if (s.negotiationMessages)
                        {
                            string key = !won ? "SmarterBeds_ConvinceFailed" : bully ? "SmarterBeds_ConvinceWonIntimidate" : "SmarterBeds_ConvinceWon";
                            BriefMessage.Show(key.Translate(wanter.LabelShort, partner.LabelShort, askerTotal, partnerTotal),
                                new LookTargets(wanter, partner), s.messageSeconds);
                        }
                        NegotiationLog.Say(won ? "SmarterBeds_AgreeToMove" : "SmarterBeds_RefuseToMove", partner, wanter);
                        if (won) SendBetterTalker(wanter, partner, s);
                    }
                }
            }
            catch (System.Exception e) { SafeLog.Once("talking a partner into moving", e); }
            finally { StartPairCooldown(wanter, partner); }
        }

        /// <summary>The partner is on board: whoever talks better of the two goes to ask for the room.</summary>
        private void SendBetterTalker(Pawn wanter, Pawn partner, SmarterBedsSettings s)
        {
            var negotiator = BetterTalker(wanter, partner);
            var other = negotiator == wanter ? partner : wanter;
            var target = FindCoupleTarget(negotiator, other, s, false, out _, out _, out _, out _);
            var jobDef = NegotiateJobDef;
            if (target == null || jobDef == null) return;

            var job = MakeNegotiationJob(jobDef, target, ModeCouple);
            if (negotiator == wanter)
            {
                // Still finishing the conversation; this follows straight after it.
                negotiator.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
            }
            else if (!negotiator.Downed && negotiator.Awake() && !PawnBusy.Is(negotiator))
            {
                negotiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }

        /// <summary>The couple asks for someone's room. A win moves both of them in; the owner is left to find another bed.</summary>
        private void ResolveCouple(Pawn asker, Pawn owner, bool forced)
        {
            var s = SmarterBedsMod.Settings;
            try
            {
                var partner = CoupleOf(asker);
                if (partner != null && Eligible(asker) && Eligible(owner) && (forced || ComputeWants(asker) || ComputeWants(partner)))
                {
                    var target = FindCoupleTarget(asker, partner, s, forced, out float gain, out var contest,
                        out var bedAsker, out var bedPartner, only: owner);
                    if (target == owner)
                    {
                        bool won = NegotiationRoll.Roll(contest, out int askerTotal, out int ownerTotal);
                        GiveSocialExperience(asker, won, s);
                        if (contest.approach == NegotiationApproach.Intimidate) Resent(owner, asker);
                        if (won) MoveCouple(asker, partner, owner, bedAsker, bedPartner, gain, s, contest, askerTotal, ownerTotal);
                        else RefuseCouple(asker, partner, owner, contest, askerTotal, ownerTotal, s);
                    }
                }
            }
            catch (System.Exception e) { SafeLog.Once("settling a couple's negotiation", e); }
            finally { StartPairCooldown(asker, owner); }
        }

        private void RefuseCouple(Pawn asker, Pawn partner, Pawn owner, NegotiationContest c, int askerTotal, int ownerTotal, SmarterBedsSettings s)
        {
            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] couple negotiation failed: {asker.LabelShort} (for {partner.LabelShort}) vs {owner.LabelShort} ({c.approach}) {askerTotal} vs {ownerTotal}");
            NegotiationLog.Say("SmarterBeds_RefuseRoom", owner, asker);
            if (s.negotiationMessages)
                BriefMessage.Show("SmarterBeds_CoupleRoomFailed".Translate(asker.LabelShort, owner.LabelShort, askerTotal, ownerTotal, partner.LabelShort),
                    new LookTargets(asker, partner, owner), s.messageSeconds);
        }

        private bool MoveCouple(Pawn asker, Pawn partner, Pawn owner, Building_Bed bedAsker, Building_Bed bedPartner, float gain,
            SmarterBedsSettings s, NegotiationContest contest, int askerTotal, int ownerTotal)
        {
            var ownerBed = owner.ownership.OwnedBed;
            var oldAsker = asker.ownership.OwnedBed;
            var oldPartner = partner.ownership.OwnedBed;

            owner.ownership.UnclaimBed();
            asker.ownership.UnclaimBed();
            partner.ownership.UnclaimBed();
            if (!(asker.ownership.ClaimBedIfNonMedical(bedAsker) && partner.ownership.ClaimBedIfNonMedical(bedPartner)))
            {
                // Put everyone back exactly as they were.
                asker.ownership.UnclaimBed();
                partner.ownership.UnclaimBed();
                if (oldAsker != null) asker.ownership.ClaimBedIfNonMedical(oldAsker);
                if (oldPartner != null) partner.ownership.ClaimBedIfNonMedical(oldPartner);
                if (ownerBed != null) owner.ownership.ClaimBedIfNonMedical(ownerBed);
                return false;
            }

            NoteMoved(asker);
            NoteMoved(partner);
            int until = Now + (int)(s.rematchDays * GenDate.TicksPerDay);
            rematchUntil[RematchKey(owner, asker)] = until;
            rematchUntil[RematchKey(owner, partner)] = until;
            StartCoupleCooldown(asker, partner, s.negotiationCooldownDays);
            PruneCooldowns();

            if (s.verboseLogging)
                Log.Message($"[Smarter Beds] couple negotiation: {asker.LabelShort} and {partner.LabelShort} took {owner.LabelShort}'s room (gain {gain:0.0})");

            NegotiationLog.Say(contest.approach == NegotiationApproach.Intimidate ? "SmarterBeds_YieldRoom" : "SmarterBeds_AgreeToRoom", owner, asker);
            ApplyMoodEffects(asker, owner, contest, gain, s, partner);

            if (s.negotiationMessages)
            {
                string key = contest.approach == NegotiationApproach.Intimidate ? "SmarterBeds_CoupleRoomWonIntimidate" : "SmarterBeds_CoupleRoomWonPersuade";
                BriefMessage.Show(key.Translate(asker.LabelShort, owner.LabelShort, askerTotal, ownerTotal, partner.LabelShort),
                    new LookTargets(asker, partner, owner), s.messageSeconds);
            }
            return true;
        }

        /// <summary>
        /// Someone whose room the couple could take over: nobody else owns a bed in it, it has room for two,
        /// it would be better for them (a penalty fixed, or a big enough upgrade), and the owner is free to be asked.
        /// Returns the owner and the two beds the couple would take, or null.
        /// </summary>
        private Pawn FindCoupleTarget(Pawn n, Pawn m, SmarterBedsSettings s, bool force, out float bestGain, out NegotiationContest bestContest,
            out Building_Bed bestBedN, out Building_Bed bestBedM, Pawn only = null)
        {
            bestGain = 0f;
            bestContest = default;
            bestBedN = null;
            bestBedM = null;
            Pawn best = null;
            float bestValue = 0f;

            var oldN = n.ownership?.OwnedBed;
            var oldM = m.ownership?.OwnedBed;
            if (oldN == null || oldM == null) return null;

            float current = RoomComfort.Points(oldN, n, s, company: false) + RoomComfort.Points(oldM, m, s, company: false);
            float debuffNow = RoomComfort.DebuffPenalty(oldN, n, s) + RoomComfort.DebuffPenalty(oldM, m, s);
            Room roomN = oldN.GetRoom(), roomM = oldM.GetRoom();

            foreach (var c in new List<Pawn>(n.Map.mapPawns.FreeColonistsSpawned))
            {
                if (c == n || c == m || (only != null && c != only) || !Eligible(c)) continue;
                if (!c.Awake() || c.Downed || c.Drafted || c.InMentalState || PawnBusy.Is(c)) continue;
                if (AreLovePartners(n, c) || AreLovePartners(m, c)) continue;
                if (Family.IsInCoupleRoom(c)) continue;
                if (!force && (PairOnCooldown(n, c) || PairOnCooldown(m, c))) continue;
                if (!force && ((rematchUntil.TryGetValue(RematchKey(n, c), out int r1) && Now < r1)
                            || (rematchUntil.TryGetValue(RematchKey(m, c), out int r2) && Now < r2))) continue;

                var bedC = c.ownership.OwnedBed;
                Room room = bedC.GetRoom();
                if (room == null || room.PsychologicallyOutdoors || room == roomN || room == roomM) continue;

                // Nobody else may live there, and there has to be space for two.
                if (room.ContainedBeds.Any(bed => bed.OwnersForReading.Any(o => o != c))) continue;
                var usable = room.ContainedBeds.Where(bed => bed.Spawned && !bed.Medical && bed.ForColonists
                    && RestUtility.CanUseBedEver(n, bed.def) && RestUtility.CanUseBedEver(m, bed.def)
                    && (bed.OwnersForReading.Count > 0 || (BedScorer.IsCandidate(bed, n, n) && BedScorer.IsCandidate(bed, m, m)))).ToList();
                if (usable.Sum(bed => bed.SleepingSlotsCount) < 2) continue;

                // Each bed scored once per person, as the owner is about to leave.
                var forN = usable.Select(bed => RoomComfort.Points(bed, n, s, company: false, leaving: c)).ToList();
                var forM = usable.Select(bed => RoomComfort.Points(bed, m, s, company: false, leaving: c)).ToList();
                float value = float.MinValue;
                int bi = -1, bj = -1;
                for (int i = 0; i < usable.Count; i++)
                {
                    for (int j = 0; j < usable.Count; j++)
                    {
                        if (i == j && usable[i].SleepingSlotsCount < 2) continue;
                        float v = forN[i] + forM[j] + 2f * s.coupleRoomBonus;
                        if (v > value) { value = v; bi = i; bj = j; }
                    }
                }
                if (bi < 0) continue;

                float gain = value - current;
                if (gain <= 0f) continue;

                float debuffNew = RoomComfort.DebuffPenalty(usable[bi], n, s, leaving: c) + RoomComfort.DebuffPenalty(usable[bj], m, s, leaving: c);
                bool fixesAPenalty = debuffNew < debuffNow;
                bool bigUpgrade = s.qualityNegotiationEnabled && gain >= s.qualityNegotiationThreshold;
                if (!force && !fixesAPenalty && !bigUpgrade) continue;

                // The owner must not be using it right now. Checked last: it asks the pathfinder.
                if (!n.CanReserveAndReach(bedC, PathEndMode.OnCell, Danger.Some, bedC.SleepingSlotsCount)) continue;

                var contest = NegotiationRoll.Build(n, c, s);
                if (!contest.possible || contest.winChance < (force ? 0.01f : s.minWinChance)) continue;

                float weighted = gain * contest.winChance;
                if (weighted > bestValue)
                {
                    bestValue = weighted;
                    bestGain = gain;
                    bestContest = contest;
                    bestBedN = usable[bi];
                    bestBedM = usable[bj];
                    best = c;
                }
            }
            return best;
        }

        private void PruneCooldowns()
        {
            int now = Now;
            foreach (var key in pairCooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                pairCooldownUntil.Remove(key);
            foreach (var key in coupleCooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                coupleCooldownUntil.Remove(key);
            foreach (var key in rematchUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                rematchUntil.Remove(key);
        }
    }
}
