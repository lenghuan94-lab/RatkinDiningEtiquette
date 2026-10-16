using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    public sealed class MapComponent_RatToddlerDining : MapComponent
    {
        private const int FailureCooldownTicks = 600;
        private const int TimeoutCooldownTicks = 2500;
        private const int MaxHelperRetries = 3;
        private List<RatToddlerDiningRequest> requests = new List<RatToddlerDiningRequest>();
        private int nextRequestId = 1;
        private readonly Dictionary<int, RatToddlerDiningRequest> byPawn = new Dictionary<int, RatToddlerDiningRequest>();
        private readonly Dictionary<int, RatToddlerDiningRequest> claimedChairs = new Dictionary<int, RatToddlerDiningRequest>();
        private readonly Dictionary<int, RatToddlerDiningRequest> recoveringByPawn = new Dictionary<int, RatToddlerDiningRequest>();
        private Dictionary<int, int> cooldownUntilByPawn = new Dictionary<int, int>();
        private List<RatDiningVanillaMeal> vanillaMeals = new List<RatDiningVanillaMeal>();
        private readonly Dictionary<int, RatDiningVanillaMeal> vanillaMealsByPawn = new Dictionary<int, RatDiningVanillaMeal>();

        public MapComponent_RatToddlerDining(Map map) : base(map) { }
        public IReadOnlyList<RatToddlerDiningRequest> Requests => requests;

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // One pass after loading catches an old saved vanilla meal. This is
            // a pawn lister, never a per-tick furniture/map scan.
            if (!RatDiningMod.Settings.enableChairTable && !RatDiningMod.Settings.enableToddlerHelp) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (RatDiningCustody.CanDine(pawn) && pawn.CurJobDef == JobDefOf.Ingest) TrackVanillaMeal(pawn, pawn.CurJob);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref requests, "requests", LookMode.Deep);
            Scribe_Values.Look(ref nextRequestId, "nextRequestId", 1);
            Scribe_Collections.Look(ref cooldownUntilByPawn, "diningCooldowns", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref vanillaMeals, "rdeVanillaMeals", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) RebuildIndexes();
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0) return;
            for (int i = vanillaMeals.Count - 1; i >= 0; i--)
            {
                RatDiningVanillaMeal meal = vanillaMeals[i];
                if (meal?.pawn != null && meal.pawn.MapHeld == map && meal.pawn.CurJobDef == JobDefOf.Ingest
                    && meal.pawn.CurJob.loadID == meal.jobId) continue;
                if (meal?.pawn != null && GetVanillaMeal(meal.pawn) == meal) vanillaMealsByPawn.Remove(meal.pawn.thingIDNumber);
                vanillaMeals.RemoveAt(i);
            }
            for (int i = requests.Count - 1; i >= 0; i--)
            {
                RatToddlerDiningRequest request = requests[i];
                if (request == null) { requests.RemoveAt(i); continue; }
                if (IsFinalizedMeal(request))
                {
                    Complete(request);
                    Release(request);
                    requests.RemoveAt(i);
                    continue;
                }
                // A loaded care job never runs through StartJob's prefix.
                // Reconcile care ownership before any reopen or local drop.
                if (request.toddler != null && request.helper?.carryTracker?.CarriedThing == request.toddler
                    && RatDiningCarryRecovery.IsCareOrder(request.helper.CurJob, request.toddler))
                    HandOffCare(request);
                if (!request.IsActive)
                {
                    // Recovery runs after job cleanup, never inside a driver's finish action.
                    if (request.recoveryPending && !TryRecoverMeal(request)) continue;
                    if (request.toddler != null && GetRecoveringForPawn(request.toddler) == request)
                        recoveringByPawn.Remove(request.toddler.thingIDNumber);
                    Release(request);
                    requests.RemoveAt(i);
                    continue;
                }
                if (!ValidCore(request) || !RatToddlerMealCoordinator.SafeForChairMeal(request.toddler))
                { FailToFloor(request, false); continue; }
                if (request.state == RatToddlerDiningState.CollectingFood)
                {
                    if (!ValidHelper(request) || !RatToddlerMealCoordinator.SafeForChairMeal(request.toddler)
                        || Find.TickManager.TicksGame - request.stateTick >= RatDiningMod.Settings.normalWaitTicks)
                        FailToFloor(request, false);
                    continue;
                }
                if (request.state == RatToddlerDiningState.BeingCarried)
                {
                    if (!ValidHelper(request) || request.helper.carryTracker?.CarriedThing != request.toddler)
                    { Reopen(request); continue; }
                    if (Find.TickManager.TicksGame - request.stateTick >= RatDiningMod.Settings.normalWaitTicks)
                        FailToFloor(request, false);
                    continue;
                }
                if (!request.toddler.Spawned)
                { FailToFloor(request, false); continue; }
                if (request.resumeWaitingPending)
                {
                    JobDef current = request.toddler.CurJobDef;
                    if (!RatToddlerDiningUtility.IsWaitingJob(current))
                    {
                        if (current != null && current != RDE_DefOf.RDE_ToddlerWaitForMeal
                            && current != JobDefOf.Carried && current != JobDefOf.Wait
                            && current != JobDefOf.Wait_MaintainPosture)
                        { Cancel(request, false); continue; }
                        Job waitJob = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForDiningHelp,
                            request.food, request.chair, request.stagingCell);
                        waitJob.count = request.foodCount;
                        request.toddler.jobs.StartJob(waitJob, JobCondition.InterruptForced,
                            keepCarryingThingOverride: true, preToilReservationsCanFail: true);
                        if (!request.IsActive) continue;
                    }
                    request.resumeWaitingPending = false;
                }
                if (request.state == RatToddlerDiningState.EatingAtTable || request.state == RatToddlerDiningState.EatingOnChair
                    || request.state == RatToddlerDiningState.PlacedAtChair)
                {
                    if (request.toddler.CurJobDef != RDE_DefOf.RDE_ToddlerEatAtChair) FailToFloor(request, false);
                    continue;
                }
                if (!RatToddlerDiningUtility.IsWaitingJob(request.toddler.CurJobDef))
                { Cancel(request, false); continue; }
                if (request.state == RatToddlerDiningState.Claimed && !ValidHelper(request))
                { Reopen(request); continue; }
                int wait = request.toddler.needs.food.CurLevelPercentage < 0.25f
                    ? RatDiningMod.Settings.hungryWaitTicks : RatDiningMod.Settings.normalWaitTicks;
                if (request.state == RatToddlerDiningState.Claimed
                    && Find.TickManager.TicksGame - request.claimTick >= RatDiningMod.Settings.normalWaitTicks)
                    FailToFloor(request, false);
                else if (request.state == RatToddlerDiningState.WaitingForHelp
                    && Find.TickManager.TicksGame - request.waitStartedTick >= wait)
                    FailToFloor(request, true);
                else if (request.state == RatToddlerDiningState.MovingFoodToDiningSpot
                    && Find.TickManager.TicksGame - request.createdTick >= RatDiningMod.Settings.normalWaitTicks)
                    FailToFloor(request, false);
            }
        }

        public bool IsCoolingDown(Pawn toddler)
        {
            if (toddler == null || !cooldownUntilByPawn.TryGetValue(toddler.thingIDNumber, out int until)) return false;
            if (Find.TickManager.TicksGame < until) return true;
            cooldownUntilByPawn.Remove(toddler.thingIDNumber);
            return false;
        }

        internal void TrackVanillaMeal(Pawn pawn, Job job)
        {
            if (pawn == null || !RatDiningApi.IsRatkin(pawn) || pawn.MapHeld != map || job?.def != JobDefOf.Ingest) return;
            RatDiningVanillaMeal old = GetVanillaMeal(pawn);
            if (old?.jobId == job.loadID) return;
            if (old != null) vanillaMeals.Remove(old);
            RatDiningVanillaMeal meal = new RatDiningVanillaMeal { pawn = pawn, jobId = job.loadID };
            meal.custody.Bind(pawn, pawn.PositionHeld);
            vanillaMeals.Add(meal);
            vanillaMealsByPawn[pawn.thingIDNumber] = meal;
        }

        internal RatDiningVanillaMeal GetVanillaMeal(Pawn pawn)
        {
            if (pawn == null) return null;
            vanillaMealsByPawn.TryGetValue(pawn.thingIDNumber, out RatDiningVanillaMeal meal);
            return meal;
        }

        public RatToddlerDiningRequest Create(Pawn toddler, Thing food, Building chair, Building table, IntVec3 staging)
        {
            if (!RatDiningApi.IsEligibleToddler(toddler) || !RatToddlerMealCoordinator.SafeForChairMeal(toddler)
                || toddler.Map != map || IsCoolingDown(toddler) || IsRecovering(toddler)
                || !RatToddlerDiningUtility.CanEatMeal(toddler, food) || !RatDiningMod.Settings.enableToddlerHelp
                || table != null || !RatDiningSpotFinder.IsUsableChair(toddler, chair)
                || !RatDiningCustody.IsLegalCell(toddler, staging)) return null;
            Pawn feeder = map.reservationManager.FirstRespectedReserver(toddler, toddler);
            if (toddler.CurJobDef == JobDefOf.BabySuckle
                || RatToddlerMealCoordinator.FeedingTarget(feeder?.CurJob) == toddler && feeder != null) return null;
            RatToddlerDiningRequest old = GetForPawn(toddler);
            if (old?.IsActive == true) return old;
            if (IsChairClaimed(chair)) return null;
            RatToddlerDiningRequest request = new RatToddlerDiningRequest
            {
                id = nextRequestId++, toddler = toddler, food = food, chair = chair, table = table,
                stagingCell = staging, state = RatToddlerDiningState.MovingFoodToDiningSpot,
                createdTick = Find.TickManager.TicksGame, stateTick = Find.TickManager.TicksGame
            };
            request.custody.Bind(toddler, toddler.Position);
            requests.Add(request);
            byPawn[toddler.thingIDNumber] = request;
            claimedChairs[chair.thingIDNumber] = request;
            RatDiningApi.Debug($"Created request #{request.id} for {toddler}.");
            return request;
        }

        public RatToddlerDiningRequest GetForPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            byPawn.TryGetValue(pawn.thingIDNumber, out RatToddlerDiningRequest result);
            return result;
        }

        internal RatToddlerDiningRequest GetRecoveringForPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            recoveringByPawn.TryGetValue(pawn.thingIDNumber, out RatToddlerDiningRequest request);
            return request;
        }

        internal bool IsRecovering(Pawn pawn)
        { return GetRecoveringForPawn(pawn)?.recoveryPending == true; }

        internal bool CanResumeRequest(RatToddlerDiningRequest request)
        {
            // An active request already owns its indexed destination. Its helper
            // may legitimately reserve that chair, so do not rerun new-chair availability.
            return Owns(request) && request.IsActive && ValidCore(request)
                && RatToddlerMealCoordinator.SafeForChairMeal(request.toddler);
        }

        internal void BeginProvision(RatToddlerDiningRequest request, Pawn helper, Job provide)
        {
            if (!Owns(request) || !request.IsActive || request.helper != null) return;
            request.helper = helper;
            request.helperJobId = provide.loadID;
            request.claimTick = Find.TickManager.TicksGame;
            SetState(request, RatToddlerDiningState.CollectingFood);
        }

        public bool TryClaim(RatToddlerDiningRequest request, Pawn helper)
        {
            if (!Owns(request) || helper == null || !request.IsAvailable || !RatToddlerDiningUtility.HoldsFood(request)
                || !ValidCore(request) || RatDiningApi.IsCriticalHunger(request.toddler)
                || !RatToddlerMealCoordinator.CanHelp(helper, request.toddler, true)
                || !helper.Spawned || helper.Map != map || helper.Dead || helper.Downed || helper.InMentalState || helper.Drafted
                || !RatToddlerDiningUtility.IsHelperJob(request, helper)) return false;
            for (int i = 0; i < requests.Count; i++)
                if (requests[i].IsActive && requests[i].helper == helper) return false;
            request.helper = helper;
            request.helperJobId = helper.CurJob.loadID;
            request.claimTick = Find.TickManager.TicksGame;
            SetState(request, RatToddlerDiningState.Claimed);
            return true;
        }

        public void SetState(RatToddlerDiningRequest request, RatToddlerDiningState state)
        {
            if (!Owns(request) || !request.IsActive) return;
            if (state == RatToddlerDiningState.WaitingForHelp && request.waitStartedTick < 0)
                request.waitStartedTick = Find.TickManager.TicksGame;
            request.state = state;
            request.stateTick = Find.TickManager.TicksGame;
            RatDiningApi.Debug($"Request #{request.id}: {state}.");
        }

        public void Reopen(RatToddlerDiningRequest request, bool cooldown = true)
        {
            if (!Owns(request) || !request.IsActive) return;
            if (!TryDropToddler(request) || !request.toddler.Spawned || !ValidCore(request)
                || !RatToddlerDiningUtility.HoldsFood(request) || ++request.retryCount > MaxHelperRetries)
            { FailToFloor(request, false); return; }
            if (RatToddlerDiningUtility.IsHelperJob(request, request.helper))
                request.helper.ClearReservationsForJob(request.helper.CurJob);
            request.helper = null;
            request.helperJobId = -1;
            request.claimTick = 0;
            if (cooldown) request.cooldownUntilTick = Find.TickManager.TicksGame + 120;
            request.resumeWaitingPending = true;
            request.waitStartedTick = Find.TickManager.TicksGame;
            SetState(request, RatToddlerDiningState.WaitingForHelp);
        }

        public void Complete(RatToddlerDiningRequest request)
        {
            if (!Owns(request) || !request.IsActive) return;
            SetState(request, RatToddlerDiningState.Completed);
            Release(request);
            request.helper = null;
        }

        internal bool IsFinalizedMeal(RatToddlerDiningRequest request, Job job = null)
            => request?.mealFinalized == true && request.mealJobId >= 0
                && (job == null || job.def == RDE_DefOf.RDE_ToddlerEatAtChair && job.loadID == request.mealJobId
                    && job.targetA.Thing == request.food && job.targetB.Thing == request.chair);

        internal void MarkMealFinalized(RatToddlerDiningRequest request, Job job)
        {
            if (!Owns(request) || !request.IsActive || job?.def != RDE_DefOf.RDE_ToddlerEatAtChair
                || request.toddler?.CurJob != job || job.targetA.Thing != request.food || job.targetB.Thing != request.chair) return;
            request.mealFinalized = true;
            request.mealJobId = job.loadID;
            request.recoverMeal = false;
            request.recoveryPending = false;
        }

        public bool IsChairClaimed(Building chair)
        { return chair != null && claimedChairs.ContainsKey(chair.thingIDNumber); }

        internal bool TryReassignChair(RatToddlerDiningRequest request, Building chair, IntVec3 stagingCell)
        {
            if (!Owns(request) || !request.IsAvailable || !RatDiningCustody.ValidRequest(request)
                || !RatDiningCustody.IsLegalCell(request.toddler, stagingCell, request.custody.originCell)
                || !RatDiningSpotFinder.IsUsableChair(request.toddler, chair)) return false;
            if (request.chair != chair && IsChairClaimed(chair)) return false;
            ReleaseChair(request);
            request.chair = chair;
            request.table = null;
            request.stagingCell = stagingCell;
            if (RatToddlerDiningUtility.IsWaitingJob(request.toddler.CurJobDef))
            {
                request.toddler.CurJob.SetTarget(TargetIndex.B, chair);
                request.toddler.CurJob.SetTarget(TargetIndex.C, stagingCell);
            }
            claimedChairs[chair.thingIDNumber] = request;
            return true;
        }

        internal void NotifyToddlerJobEnded(RatToddlerDiningRequest request, JobCondition condition)
        {
            if (!Owns(request) || !request.IsActive) return;
            if (IsFinalizedMeal(request)) { Complete(request); return; }
            RatDiningApi.Debug($"Toddler job ended: request=#{request.id}, condition={condition}, state={request.state}.");
            // Despawning into the caregiver's carry tracker ends the toddler's job normally.
            if (request.state == RatToddlerDiningState.BeingCarried && ValidHelper(request)) return;
            // The provider replaces the child's own preparation with waiting for
            // this same meal, after acquiring its reservations successfully.
            if (request.state == RatToddlerDiningState.CollectingFood && ValidHelper(request)
                && RatToddlerDiningUtility.IsWaitingJob(request.toddler.CurJobDef)) return;
            if (request.state == RatToddlerDiningState.WaitingForHelp && request.resumeWaitingPending
                && request.toddler.CurJobDef == RDE_DefOf.RDE_ToddlerWaitForMeal
                && condition == JobCondition.InterruptForced && RatToddlerDiningUtility.HoldsFood(request)) return;
            bool recover = condition == JobCondition.Incompletable || condition == JobCondition.Errored
                || condition == JobCondition.ErroredPather;
            Cancel(request, recover);
        }

        internal void DebugCancel(Pawn toddler) { Cancel(GetForPawn(toddler), true); }
        internal void CancelForFeeding(RatToddlerDiningRequest request)
        {
            if (!Owns(request) || !request.IsActive) return;
            Pawn helper = request.helper;
            if (RatToddlerDiningUtility.IsHelperJob(request, helper)) helper.ClearReservationsForJob(helper.CurJob);
            FailToFloor(request, false, false);
        }
        internal void StartFloorFallback(RatToddlerDiningRequest request, bool addThought) { FailToFloor(request, addThought); }

        internal void HandOffCare(RatToddlerDiningRequest request)
        {
            if (request?.toddler == null) return;
            request.careHandoff = true;
            if (request.IsActive) FailToFloor(request, false, false);
            request.recoveryPending = false;
            request.recoverMeal = false;
            // Keep the retired record until the next map tick so the old
            // driver's cleanup sees the explicit transfer of care ownership.
            recoveringByPawn[request.toddler.thingIDNumber] = request;
        }

        internal void DebugTimeout(Pawn toddler)
        {
            RatToddlerDiningRequest request = GetForPawn(toddler);
            if (request != null) FailToFloor(request, true);
        }

        private bool Owns(RatToddlerDiningRequest request)
        { return request?.toddler != null && GetForPawn(request.toddler) == request; }

        private bool ValidCore(RatToddlerDiningRequest request)
        {
            if (!RatDiningMod.Settings.enableToddlerHelp || !RatDiningApi.CanParticipateInToddlerDining(request.toddler)
                || !RatDiningCustody.ValidRequest(request)
                || request.toddler.MapHeld != map || !RatToddlerDiningUtility.CanEatMeal(request.toddler, request.food, request.helper)
                || !RatDiningFurniture.AllowedFor(request.toddler, request.chair)
                || request.chair == null || !request.chair.Spawned || request.chair.Destroyed || request.chair.Map != map || request.chair.IsBurning()
                || request.chair.IsForbidden(request.toddler) || !request.chair.Position.InAllowedArea(request.toddler)
                || request.table != null || RatDiningSpotFinder.FindAdjacentTable(request.chair) != null
                || request.chair.Position.GetDangerFor(request.toddler, map) == Danger.Deadly) return false;
            return request.state == RatToddlerDiningState.MovingFoodToDiningSpot
                || request.state == RatToddlerDiningState.CollectingFood || RatToddlerDiningUtility.HoldsFood(request);
        }

        private bool ValidHelper(RatToddlerDiningRequest request)
        {
            Pawn helper = request.helper;
            return helper != null && helper.Spawned && helper.Map == map && !helper.Dead && !helper.Downed
                && !helper.InMentalState && !helper.Drafted && RatToddlerDiningUtility.IsHelperJob(request, helper)
                && RatToddlerMealCoordinator.CanHelp(helper, request.toddler, true);
        }

        internal static string DescribeFailure(RatToddlerDiningRequest request)
        {
            Pawn child = request?.toddler;
            Thing food = request?.food;
            RatDiningSettings settings = RatDiningMod.Settings;
            if (child == null) return $"Toddler meal fallback: request=#{request?.id}, reason=missing-child.";
            Map heldMap = child.MapHeld;
            bool cellValid = heldMap != null && child.PositionHeld.InBounds(heldMap);
            return $"Toddler meal fallback: request=#{request.id}, state={request.state}, "
                + $"job={child.CurJobDef?.defName}, jobId={child.CurJob?.loadID}, toil={child.jobs?.curDriver?.CurToilIndex}, finalized={request.mealFinalized}, "
                + $"mealJobId={request.mealJobId}, ratkin={RatDiningApi.IsRatkin(child)}, "
                + $"toddler={Toddlers.ToddlerUtility.IsToddler(child)}, age={child.ageTracker?.AgeBiologicalYearsFloat}, "
                + $"canFeedSelf={Toddlers.ToddlerLearningUtility.CanFeedSelf(child)}, spawned={child.Spawned}, "
                + $"dead={child.Dead}, downed={child.Downed}, drafted={child.Drafted}, mental={child.InMentalState}, "
                + $"colonist={child.IsColonist}, prisoner={child.IsPrisonerOfColony}, slave={child.IsSlaveOfColony}, "
                + $"canDine={RatDiningCustody.CanDine(child)}, custodyMatches={request.custody.Matches(child)}, "
                + $"helpMaster={settings.enableToddlerHelp}, helpCategories="
                + $"{settings.enableColonistToddlerHelp}/{settings.enableVisitorToddlerHelp}/{settings.enablePrisonerToddlerHelp}/{settings.enableSlaveToddlerHelp}, "
                + $"seatAllowed={RatDiningFurniture.AllowedFor(child, request.chair)}, "
                + $"foodPct={child.needs?.food?.CurLevelPercentage}, critical={RatDiningApi.IsCriticalHunger(child)}, criticalLimit={settings.criticalFoodLevel}, "
                + $"restPct={child.needs?.rest?.CurLevelPercentage}, medical={HealthAIUtility.ShouldSeekMedicalRest(child)}, "
                + $"cellValid={cellValid}, danger={(cellValid ? child.PositionHeld.GetDangerFor(child, heldMap).ToString() : "n/a")}, "
                + $"safeTemperature={(cellValid ? GenTemperature.SafeTemperatureAtCell(child, child.PositionHeld, heldMap).ToString() : "n/a")}, "
                + $"food={food}, destroyed={food?.Destroyed}, ingestible={food?.IngestibleNow}, "
                + $"foodAllowed={RatToddlerDiningUtility.CanEatMeal(child, food, request.helper)}, "
                + $"holdingFood={child.carryTracker?.CarriedThing == food && food != null}, "
                + $"chairSpawned={request.chair?.Spawned}, chairDestroyed={request.chair?.Destroyed}, "
                + $"chairForbidden={request.chair?.IsForbidden(child)}, chairBurning={request.chair?.IsBurning()}, "
                + $"hasAdjacentTable={RatDiningSpotFinder.FindAdjacentTable(request.chair) != null}, "
                + $"legalStaging={RatDiningCustody.IsLegalCell(child, request.stagingCell, request.custody.originCell)}.";
        }

        private void Cancel(RatToddlerDiningRequest request, bool recover)
        { FailToFloor(request, false, recover); }

        private void FailToFloor(RatToddlerDiningRequest request, bool addThought, bool recover = true)
        {
            if (!Owns(request) || !request.IsActive) return;
            if (IsFinalizedMeal(request)) { Complete(request); return; }
            if (RatDiningMod.Settings.debugLogging) RatDiningApi.Debug(DescribeFailure(request));
            Pawn toddler = request.toddler;
            bool sameCustody = !request.custody.initialized || request.custody.Matches(toddler);
            addThought &= sameCustody;
            if (addThought && !request.failureThoughtApplied && toddler.needs?.mood?.thoughts?.memories != null)
            {
                RatDiningThoughts.ApplyHelpTimeout(toddler);
                request.failureThoughtApplied = true;
            }
            SetState(request, addThought ? RatToddlerDiningState.TimedOut : RatToddlerDiningState.FloorFallback);
            cooldownUntilByPawn[toddler.thingIDNumber] = Find.TickManager.TicksGame
                + (addThought ? TimeoutCooldownTicks : FailureCooldownTicks);
            request.recoverMeal = recover && sameCustody && RatDiningCustody.CanDine(toddler);
            request.recoveryStartedTick = Find.TickManager.TicksGame;
            request.recoveryPending = !request.careHandoff && (recover || request.helper?.carryTracker?.CarriedThing == toddler);
            if (request.recoveryPending) recoveringByPawn[toddler.thingIDNumber] = request;
            if (!request.careHandoff) TryDropToddler(request);
            ReleaseProvisionFood(request);
            if (RatToddlerDiningUtility.IsHelperJob(request, request.helper))
                request.helper.ClearReservationsForJob(request.helper.CurJob);
            if (RatToddlerDiningUtility.IsToddlerJob(toddler.CurJobDef)
                && toddler.CurJob.targetB.Thing == request.chair)
                toddler.ClearReservationsForJob(toddler.CurJob);
            Release(request);
        }

        private bool ReleaseProvisionFood(RatToddlerDiningRequest request)
        {
            Pawn helper = request.helper;
            Thing food = request.food;
            if (food == null || helper == null) return true;
            if (helper.carryTracker?.CarriedThing == food)
            {
                if (helper.MapHeld != map) return true; // Food left this map and is no longer recoverable here.
                if (!helper.carryTracker.TryDropCarriedThing(helper.PositionHeld,
                    ThingPlaceMode.Near, out Thing carriedDrop)) return false;
                request.food = carriedDrop;
                return true;
            }
            if (helper.inventory?.Contains(food) != true) return true;
            if (helper.MapHeld != map) return true;
            if (!helper.inventory.innerContainer.TryDrop(food, helper.PositionHeld, map, ThingPlaceMode.Near,
                Math.Min(food.stackCount, Math.Max(1, request.foodCount)), out Thing dropped)) return false;
            request.food = dropped;
            request.foodCount = Math.Min(dropped.stackCount, request.foodCount);
            return true;
        }

        internal bool TryDropToddler(RatToddlerDiningRequest request)
        {
            Pawn helper = request.helper;
            if (helper?.carryTracker?.CarriedThing != request.toddler) return true;
            if (helper.MapHeld != map) return false;
            return RatDiningCustody.TryDropChild(helper, request.toddler, request.custody.originCell);
        }

        private bool TryRecoverMeal(RatToddlerDiningRequest request)
        {
            Pawn toddler = request.toddler;
            if (toddler == null || toddler.Dead || toddler.MapHeld != map)
            { request.recoveryPending = false; return true; }
            if (request.recoveryStartedTick < 0) request.recoveryStartedTick = Find.TickManager.TicksGame;
            if (!TryDropToddler(request))
            {
                Pawn helper = request.helper;
                Job currentCare = helper?.CurJob;
                if (RatDiningCarryRecovery.IsCareOrder(currentCare, toddler))
                { HandOffCare(request); return true; }
                if (currentCare?.def == RDE_DefOf.RDE_ReturnDiningChild && currentCare.targetA.Pawn == toddler
                    && RatDiningCarryRecovery.CanContinueReturn(request)
                    && Find.TickManager.TicksGame - request.recoveryStartedTick < RatDiningCarryRecovery.RecoveryTimeoutTicks)
                    return false;
                if (RatDiningCarryRecovery.TryMakeReturnJob(request, out Job returning))
                {
                    helper.jobs.StartJob(returning, JobCondition.InterruptForced, keepCarryingThingOverride: true,
                        preToilReservationsCanFail: true);
                    return false;
                }
                HandOffCare(request);
                if (helper?.Spawned == true && !helper.Dead && !helper.Downed && !helper.Drafted && !helper.InMentalState)
                    helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BringBabyToSafety, toddler), JobCondition.InterruptForced,
                        keepCarryingThingOverride: true, preToilReservationsCanFail: true);
                RatDiningApi.Debug($"Request #{request.id}: no bounded dining return remains; released to vanilla/Toddlers care.");
                return true;
            }
            if (!ReleaseProvisionFood(request))
            {
                if (Find.TickManager.TicksGame - request.recoveryStartedTick < RatDiningCarryRecovery.RecoveryTimeoutTicks) return false;
                // A failed engine/container drop leaves the original stack with
                // its holder. Do not consume, delete, or manufacture replacement food.
                Log.Warning($"[RDE] Request #{request.id}: meal could not leave its holder after bounded recovery; dining claims released, original food retained.");
                request.recoveryPending = false;
                return true;
            }
            request.recoveryPending = false;
            if (!request.recoverMeal || !RatDiningApi.CanSafelySelfFeed(toddler) || !toddler.Spawned || GetForPawn(toddler)?.IsActive == true) return true;
            JobDef current = toddler.CurJobDef;
            // A rescue, drafted order or unrelated need takes precedence over this failed request.
            if (toddler.CurJob?.playerForced == true || current == JobDefOf.BabySuckle) return true;
            if ((current == JobDefOf.Ingest || current == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor)
                && toddler.CurJob.targetA.Thing == request.food) return true;
            if (current != null && current != JobDefOf.Ingest && !RatToddlerDiningUtility.IsToddlerJob(current)
                && current != JobDefOf.Carried && current != JobDefOf.Wait && current != JobDefOf.Wait_MaintainPosture) return true;
            if (!TryMakeRecoveryJob(toddler, out Job eat)) return true;
            toddler.jobs.StartJob(eat, JobCondition.InterruptForced, keepCarryingThingOverride: toddler.carryTracker?.CarriedThing == eat.targetA.Thing,
                tag: JobTag.SatisfyingNeeds, preToilReservationsCanFail: true);
            return true;
        }

        internal bool TryMakeRecoveryJob(Pawn toddler, out Job eat)
        {
            eat = null;
            RatToddlerDiningRequest request = GetRecoveringForPawn(toddler);
            if (request == null || !request.recoverMeal || request.careHandoff
                || request.custody.initialized && !request.custody.Matches(toddler)
                || !RatDiningCustody.CanDine(toddler)
                || !RatDiningApi.CanSafelySelfFeed(toddler) || !toddler.Spawned
                || HealthAIUtility.ShouldSeekMedicalRest(toddler)
                || GetForPawn(toddler)?.IsActive == true) return false;
            Thing food = request.food;
            if (!RatToddlerDiningUtility.TryGetPickupCount(toddler, food, request.foodCount, out int count)) return false;
            bool held = toddler.carryTracker?.CarriedThing == food;
            eat = JobMaker.MakeJob(held ? RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor : JobDefOf.Ingest, food);
            eat.count = count;
            if (!held) TrackVanillaMeal(toddler, eat);
            return true;
        }

        internal Job MakeMealFinalizer(RatToddlerDiningRequest request, JobCondition condition)
        {
            if (IsFinalizedMeal(request)) { Complete(request); return null; }
            if (request == null || (condition != JobCondition.Incompletable && condition != JobCondition.Errored
                && condition != JobCondition.ErroredPather && condition != JobCondition.QueuedNoLongerValid)) return null;
            if (request.IsActive) FailToFloor(request, false);
            // RimWorld's finalizer mechanism preserves the carried item during
            // cleanup and starts this job afterwards, without a nested StartJob.
            return TryMakeRecoveryJob(request.toddler, out Job eat) ? eat : null;
        }

        private void ReleaseChair(RatToddlerDiningRequest request)
        {
            if (request?.chair != null && claimedChairs.TryGetValue(request.chair.thingIDNumber, out RatToddlerDiningRequest owner)
                && owner == request) claimedChairs.Remove(request.chair.thingIDNumber);
        }

        private void Release(RatToddlerDiningRequest request)
        {
            ReleaseChair(request);
            if (Owns(request)) byPawn.Remove(request.toddler.thingIDNumber);
        }

        private void RebuildIndexes()
        {
            if (requests == null) requests = new List<RatToddlerDiningRequest>();
            if (cooldownUntilByPawn == null) cooldownUntilByPawn = new Dictionary<int, int>();
            byPawn.Clear(); claimedChairs.Clear(); recoveringByPawn.Clear();
            if (vanillaMeals == null) vanillaMeals = new List<RatDiningVanillaMeal>();
            vanillaMealsByPawn.Clear();
            foreach (RatDiningVanillaMeal meal in vanillaMeals)
                if (meal?.pawn != null) vanillaMealsByPawn[meal.pawn.thingIDNumber] = meal;
            for (int i = 0; i < requests.Count; i++)
            {
                RatToddlerDiningRequest request = requests[i];
                if (request == null) continue;
                nextRequestId = Math.Max(nextRequestId, request.id + 1);
                if (IsFinalizedMeal(request))
                {
                    request.state = RatToddlerDiningState.Completed;
                    request.recoveryPending = false;
                    request.recoverMeal = false;
                    request.helper = null;
                    continue;
                }
                if (!request.IsActive || request.toddler == null)
                {
                    if (request.recoveryPending && request.toddler != null)
                        recoveringByPawn[request.toddler.thingIDNumber] = request;
                    continue;
                }
                if (byPawn.ContainsKey(request.toddler.thingIDNumber) || request.chair == null || IsChairClaimed(request.chair))
                {
                    request.state = RatToddlerDiningState.Failed;
                    request.recoveryPending = request.toddler != null;
                    if (request.toddler != null) recoveringByPawn[request.toddler.thingIDNumber] = request;
                    continue;
                }
                if (request.waitStartedTick < 0 && request.state != RatToddlerDiningState.MovingFoodToDiningSpot)
                    request.waitStartedTick = request.createdTick;
                byPawn[request.toddler.thingIDNumber] = request;
                claimedChairs[request.chair.thingIDNumber] = request;
                // Reconcile jobs on the first map tick, after every pawn has finished loading.
            }
        }
    }
}
