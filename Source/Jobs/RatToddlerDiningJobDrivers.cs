using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    public abstract class JobDriver_ToddlerDiningBase : JobDriver
    {
        private int requestId = -1;
        protected MapComponent_RatToddlerDining Manager => pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
        protected RatToddlerDiningRequest Request => RatToddlerDiningUtility.GetBoundRequest(Manager, pawn, ref requestId);

        protected JobDriver_ToddlerDiningBase()
        {
            SetFinalizerJob(condition => Manager?.MakeMealFinalizer(Request, condition));
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref requestId, "diningRequestId", -1);
            base.ExposeData();
        }

        protected bool ValidStagingTarget(RatToddlerDiningRequest request)
        {
            return request != null && job.targetC.Cell == request.stagingCell
                && RatDiningCustody.IsLegalCell(pawn, job.targetC.Cell, request.custody.originCell);
        }

        protected bool ValidateStartTargets(bool requireStaging)
        {
            RatToddlerDiningRequest request = Request;
            bool valid = request?.IsActive == true && RatDiningApi.IsEligibleToddler(pawn)
                && RatDiningCustody.ValidRequest(request) && request.chair == job.targetB.Thing
                && RatDiningFurniture.AllowedFor(pawn, request.chair)
                && request.food == job.targetA.Thing && RatToddlerDiningUtility.CanEatMeal(pawn, request.food, request.helper)
                && (!requireStaging || ValidStagingTarget(request));
            // At job startup, pre-reservations run before SetupToils/FailOn.
            // Resumed active jobs also have guards below; never give the grid an unchecked cell.
            if (!valid) Manager?.StartFloorFallback(request, false);
            return valid;
        }

        protected void AddRequestGuards()
        {
            AddFinishAction(condition => Manager?.NotifyToddlerJobEnded(Request, condition));
            this.FailOn(() => Request?.IsActive != true || !RatDiningApi.IsEligibleToddler(pawn));
            this.FailOn(() => !RatDiningCustody.ValidRequest(Request));
            this.FailOn(() => Request?.chair != job.targetB.Thing);
            this.FailOn(() => !RatDiningFurniture.AllowedFor(pawn, Request?.chair));
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            this.FailOn(() => RatDiningSpotFinder.FindAdjacentTable(job.targetB.Thing as Building) != null);
        }
    }

    public sealed class JobDriver_ToddlerPrepareDiningRequest : JobDriver_ToddlerDiningBase
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            RatToddlerDiningRequest request = Request;
            if (!ValidateStartTargets(true)) return false;
            if (!RatToddlerDiningUtility.TryGetPickupCount(pawn, job.targetA.Thing, job.count, out int pickupCount))
            {
                Manager.NotifyToddlerJobEnded(request, JobCondition.Incompletable);
                return false;
            }
            job.count = pickupCount;
            request.foodCount = pickupCount;
            bool heldOrInventory = pawn.carryTracker?.CarriedThing == job.targetA.Thing
                || pawn.inventory?.Contains(job.targetA.Thing) == true;
            bool reserved = (heldOrInventory || pawn.Reserve(job.targetA, job, 10,
                pickupCount, null, false))
                && pawn.ReserveSittableOrSpot(job.targetC.Cell, job, false);
            if (!reserved)
            {
                pawn.ClearReservationsForJob(job);
                Manager.NotifyToddlerJobEnded(request, JobCondition.Incompletable);
                RatDiningApi.Debug($"Request #{request.id}: food or staging reservation lost; restoring ordinary eating.");
            }
            return reserved;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddRequestGuards();
            this.FailOn(() => !ValidStagingTarget(Request));
            Toil verify = ToilMaker.MakeToil("RDE_VerifyToddlerHeldFood");
            verify.initAction = delegate
            {
                RatToddlerDiningRequest request = Request;
                Thing held = pawn.carryTracker?.CarriedThing;
                if (request == null || held == null || held.Destroyed || !held.def.IsNutritionGivingIngestible || !held.IngestibleNow)
                { EndJobWith(JobCondition.Incompletable); return; }
                // Pickup splits food stacks: update the request before walking or saving.
                request.food = held;
                request.foodCount = held.stackCount;
                job.SetTarget(TargetIndex.A, held);
            };
            verify.defaultCompleteMode = ToilCompleteMode.Instant;
            // Preserve the six original toil indices for existing 1.0.2 saves.
            Toil approach = ToilMaker.MakeToil("RDE_ApproachToddlerFood");
            approach.initAction = delegate
            {
                if (pawn.carryTracker?.CarriedThing == job.targetA.Thing || pawn.inventory?.Contains(job.targetA.Thing) == true)
                    ReadyForNextToil();
                else if (job.targetA.Thing?.Spawned == true)
                    pawn.pather.StartPath(job.targetA, PathEndMode.ClosestTouch);
                else EndJobWith(JobCondition.Incompletable);
            };
            approach.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return approach;
            Toil pickup = ToilMaker.MakeToil("RDE_PickupToddlerFood");
            pickup.initAction = delegate
            {
                if (pawn.carryTracker?.CarriedThing == job.targetA.Thing) return;
                Toil take = pawn.inventory?.Contains(job.targetA.Thing) == true
                    ? Toils_Misc.TakeItemFromInventoryToCarrier(pawn, TargetIndex.A)
                    : Toils_Haul.StartCarryThing(TargetIndex.A, reserve: true, canTakeFromInventory: true);
                take.actor = pawn;
                try { take.initAction(); }
                finally { take.ReturnToPool(); }
            };
            pickup.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickup;
            yield return verify;
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            Toil register = ToilMaker.MakeToil("RDE_ToddlerRegisterDiningRequest");
            register.initAction = delegate
            {
                RatToddlerDiningRequest request = Request;
                if (!RatToddlerDiningUtility.HoldsFood(request))
                { EndJobWith(JobCondition.Incompletable); return; }
                Manager.SetState(request, RatToddlerDiningState.WaitingForHelp);
                RatToddlerSpeech.ShowRequest(pawn);
            };
            register.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return register;
            yield return MakeWaitToil();
        }

        private Toil MakeWaitToil()
        {
            Toil wait = ToilMaker.MakeToil("RDE_ToddlerWaitForHelp");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.tickIntervalAction = delegate(int delta)
            {
                if (!RatToddlerDiningUtility.HoldsFood(Request) || RatDiningApi.IsCriticalHunger(pawn))
                    Manager?.StartFloorFallback(Request, false);
            };
            return wait;
        }
    }

    public sealed class JobDriver_ToddlerWaitForDiningHelp : JobDriver_ToddlerDiningBase
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!ValidateStartTargets(true) || !RatToddlerDiningUtility.HoldsFood(Request)) return false;
            bool reserved = pawn.ReserveSittableOrSpot(job.targetC.Cell, job, false);
            if (!reserved) Manager.NotifyToddlerJobEnded(Request, JobCondition.Incompletable);
            return reserved;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddRequestGuards();
            this.FailOn(() => !ValidStagingTarget(Request));
            Toil wait = ToilMaker.MakeToil("RDE_ToddlerWaitForHelp");
            wait.initAction = delegate { RatToddlerSpeech.ShowRequest(pawn); };
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.tickIntervalAction = delegate(int delta)
            {
                if (!RatToddlerDiningUtility.HoldsFood(Request) || RatDiningApi.IsCriticalHunger(pawn))
                    Manager?.StartFloorFallback(Request, false);
            };
            yield return wait;
        }
    }

    public sealed class JobDriver_AssistRatToddlerDining : JobDriver
    {
        private int requestId = -1;
        private Pawn Toddler => job.targetA.Pawn;
        private Building Chair => job.targetB.Thing as Building;
        private MapComponent_RatToddlerDining Manager => pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
        private RatToddlerDiningRequest Request => RatToddlerDiningUtility.GetBoundRequest(Manager, Toddler, ref requestId);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref requestId, "diningRequestId", -1);
            base.ExposeData();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            job.count = 1;
            if (Request?.IsAvailable != true || Request.chair != Chair
                || WorkGiver_AssistRatToddlerDining.GetSharedRejectionReason(pawn, Toddler) != null) return false;
            bool reserved = pawn.Reserve(job.targetA, job, 1, -1, null, false)
                && pawn.Reserve(job.targetB, job, 1, -1, null, false);
            if (!reserved) pawn.ClearReservationsForJob(job);
            return reserved;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(CleanupInterrupted);
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            this.FailOn(() => Request?.IsActive != true || pawn.Drafted || pawn.Downed || pawn.InMentalState
                || !RatToddlerDiningUtility.IsHelperJob(Request, pawn)
                || !RatDiningApi.CanParticipateInToddlerDining(Toddler) || !RatDiningCustody.ValidRequest(Request)
                || !RatToddlerMealCoordinator.CanHelp(pawn, Toddler, true));
            Toil claim = ToilMaker.MakeToil("RDE_ClaimToddlerDiningRequest");
            claim.initAction = delegate
            {
                if (Manager?.TryClaim(Request, pawn) != true) EndJobWith(JobCondition.Incompletable);
            };
            claim.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return claim;
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil validate = ToilMaker.MakeToil("RDE_ValidateToddlerDiningRequest");
            validate.initAction = delegate
            {
                RatToddlerDiningRequest request = Request;
                if (request?.helper != pawn || !RatToddlerDiningUtility.HoldsFood(request)
                    || !RatToddlerDiningUtility.IsWaitingJob(Toddler.CurJobDef)
                    || RatDiningApi.IsCriticalHunger(Toddler) || pawn.carryTracker?.CarriedThing != null)
                { EndJobWith(JobCondition.Incompletable); return; }
                // Set the handoff state BEFORE DeSpawn ends the toddler's waiting job.
                // That job's carryThingAfterJob and the Carried patch preserve its meal.
                Manager.SetState(request, RatToddlerDiningState.BeingCarried);
            };
            validate.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return validate;
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, reserve: false, canTakeFromInventory: false);
            Toil carried = ToilMaker.MakeToil("RDE_CheckToddlerBeingCarried");
            carried.initAction = delegate
            {
                if (pawn.carryTracker?.CarriedThing != Toddler || !RatToddlerDiningUtility.HoldsFood(Request))
                    EndJobWith(JobCondition.Incompletable);
            };
            carried.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return carried;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            Toil place = ToilMaker.MakeToil("RDE_PlaceToddlerOnChair");
            place.initAction = delegate
            {
                RatToddlerDiningRequest request = Request;
                if (request?.helper != pawn || pawn.carryTracker?.CarriedThing != Toddler
                    || !Chair.Position.Standable(pawn.Map) || RatDiningApi.IsCriticalHunger(Toddler)
                    || RatDiningSpotFinder.FindAdjacentTable(Chair) != null
                    || !pawn.carryTracker.TryDropCarriedThing(Chair.Position, ThingPlaceMode.Direct, out Thing placed)
                    || placed != Toddler || Toddler.Position != Chair.Position || !RatToddlerDiningUtility.HoldsFood(request))
                { EndJobWith(JobCondition.Incompletable); return; }
                request.table = null;
                Manager.SetState(request, RatToddlerDiningState.PlacedAtChair);
                pawn.Map.reservationManager.Release(Chair, pawn, job);
                Job eat = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, request.food, Chair, Chair);
                eat.count = request.foodCount;
                Toddler.jobs.StartJob(eat, JobCondition.InterruptForced, keepCarryingThingOverride: true,
                    preToilReservationsCanFail: true);
            };
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return place;
        }

        private void CleanupInterrupted(JobCondition condition)
        {
            // Always drop this job's child, even if its request has already been removed.
            RatToddlerDiningRequest request = Request;
            if (request?.careHandoff != true && pawn.carryTracker?.CarriedThing == Toddler && pawn.MapHeld != null)
                RatDiningCustody.TryDropChild(pawn, Toddler, request?.custody.originCell ?? IntVec3.Invalid);
            if (condition == JobCondition.Succeeded || request?.helper != pawn
                || request.state == RatToddlerDiningState.PlacedAtChair
                || request.state == RatToddlerDiningState.EatingAtTable || request.state == RatToddlerDiningState.EatingOnChair) return;
            Manager?.Reopen(request);
        }
    }

    public sealed class JobDriver_ToddlerEatAtChair : JobDriver_RatDiningIngestBase
    {
        private Building Table => job.targetC.Thing as Building;

        public JobDriver_ToddlerEatAtChair()
        {
            SetFinalizerJob(condition => DiningManager?.MakeMealFinalizer(DiningRequest, condition));
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            RatToddlerDiningRequest request = DiningRequest;
            if (MealFinalized && pawn.CurJob == job)
            { DiningManager?.MarkMealFinalized(request, job); return true; }
            if (DiningManager?.IsFinalizedMeal(request, job) == true) { MealFinalized = true; return true; }
            bool reserved = request?.IsActive == true && RatDiningCustody.ValidRequest(request)
                && request.chair == Chair && RatToddlerDiningUtility.HoldsFood(request)
                && request.table == null && (Table == null || Table == Chair) && RatDiningSpotFinder.FindAdjacentTable(Chair) == null
                && pawn.Position == Chair.Position && pawn.Reserve(job.targetB, job, 1, -1, null, false);
            if (!reserved) DiningManager?.StartFloorFallback(request, false);
            return reserved;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition => DiningManager?.NotifyToddlerJobEnded(DiningRequest, condition));
            if (DiningManager?.IsFinalizedMeal(DiningRequest, job) == true) MealFinalized = true;
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            this.FailOn(() => !MealFinalized && (DiningRequest?.IsActive != true || !RatDiningApi.IsEligibleToddler(pawn)
                || !RatDiningFurniture.AllowedFor(pawn, Chair)
                || !RatDiningCustody.ValidRequest(DiningRequest)
                || (!MealFinalized && !RatToddlerDiningUtility.HoldsFood(DiningRequest)) || pawn.Position != Chair.Position
                || (Table != null && Table != Chair) || RatDiningSpotFinder.FindAdjacentTable(Chair) != null));
            Toil state = ToilMaker.MakeToil("RDE_BeginToddlerChairMeal");
            state.initAction = delegate
            {
                if (MealFinalized || DiningManager?.IsFinalizedMeal(DiningRequest, job) == true)
                { MealFinalized = true; return; }
                if (!RatToddlerDiningUtility.HoldsFood(DiningRequest))
                { EndJobWith(JobCondition.Incompletable); return; }
                job.SetTarget(TargetIndex.A, DiningRequest.food);
                DiningManager.SetState(DiningRequest, RatToddlerDiningState.EatingOnChair);
                pawn.Rotation = Rot4.FromAngleFlat(((Table ?? Chair).Position - pawn.Position).AngleFlat);
            };
            state.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return state;
            yield return SnapshotTableThought();
            yield return MakeChewToil(TargetIndex.C, 10f);
            yield return FinalizeMeal();
            // Always include this toil so removing a table cannot change saved toil indices.
            yield return CorrectChairThought();
            Toil finish = ToilMaker.MakeToil("RDE_CompleteToddlerMeal");
            finish.initAction = delegate
            {
                if (MealFinalized) DiningManager?.Complete(DiningRequest);
                else EndJobWith(JobCondition.Incompletable);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public sealed class JobDriver_ToddlerEatHeldFoodOnFloor : JobDriver_RatDiningIngestBase
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return RatToddlerDiningUtility.CanEatMeal(pawn, Food) && pawn.carryTracker?.CarriedThing == Food;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !RatDiningApi.CanSafelySelfFeed(pawn) || !pawn.Spawned || pawn.Map == null
                || (!MealFinalized && (!RatToddlerDiningUtility.CanEatMeal(pawn, Food) || pawn.carryTracker?.CarriedThing != Food)));
            Toil begin = ToilMaker.MakeToil("RDE_BeginToddlerFloorMeal");
            begin.initAction = delegate
            {
                if (Food == null || pawn.carryTracker?.CarriedThing != Food || !Food.IngestibleNow)
                    EndJobWith(JobCondition.Incompletable);
            };
            begin.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return begin;
            yield return SnapshotTableThought();
            yield return MakeChewToil(TargetIndex.None, 10f);
            yield return FinalizeMeal();
            // Keep vanilla floor-eating thoughts. Only an actual help timeout adds NoDiningHelp.
            Toil finish = ToilMaker.MakeToil("RDE_CompleteToddlerFloorMeal");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }
}
