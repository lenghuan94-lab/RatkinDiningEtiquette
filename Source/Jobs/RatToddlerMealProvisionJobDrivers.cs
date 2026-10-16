using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    public sealed class JobDriver_ToddlerWaitForMeal : JobDriver_ToddlerDiningBase
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return ValidateStartTargets(false) && Request?.state == RatToddlerDiningState.CollectingFood
                && RatToddlerDiningUtility.IsHelperJob(Request, Request.helper);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddRequestGuards();
            this.FailOn(() => !(Request?.state == RatToddlerDiningState.CollectingFood
                    && RatToddlerDiningUtility.IsHelperJob(Request, Request.helper))
                && !(Request?.state == RatToddlerDiningState.WaitingForHelp && Request.resumeWaitingPending
                    && RatToddlerDiningUtility.HoldsFood(Request)));
            Toil wait = ToilMaker.MakeToil("RDE_WaitForProvidedMeal");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.tickIntervalAction = delegate(int delta)
            {
                if (!RatToddlerMealCoordinator.SafeForChairMeal(pawn)) Manager?.StartFloorFallback(Request, false);
            };
            yield return wait;
        }
    }

    public sealed class JobDriver_ProvideRatToddlerDiningMeal : JobDriver
    {
        private int requestId = -1;
        private Pawn Child => job.targetA.Pawn;
        private Building Chair => job.targetB.Thing as Building;
        private MapComponent_RatToddlerDining Manager => pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
        private RatToddlerDiningRequest Request => RatToddlerDiningUtility.GetBoundRequest(Manager, Child, ref requestId);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref requestId, "diningRequestId", -1);
            base.ExposeData();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            RatToddlerDiningRequest request = Request;
            if (request?.state != RatToddlerDiningState.CollectingFood || request.helper != pawn
                || request.helperJobId != job.loadID) return false;
            if (request.chair != Chair || request.food != job.targetC.Thing)
            {
                Manager.StartFloorFallback(request, false);
                return false;
            }
            bool held = RatToddlerDiningUtility.HoldsFood(request);
            int count = request.foodCount;
            bool reserved = RatToddlerMealCoordinator.SafeForChairMeal(Child) && RatDiningCustody.ValidRequest(request)
                && RatToddlerMealCoordinator.CanHelp(pawn, Child, true)
                && (held || RatToddlerDiningUtility.TryGetPickupCount(pawn, request.food, count, out count, Child))
                && pawn.Reserve(Child, job, 1, -1, null, false)
                && pawn.Reserve(Chair, job, 1, -1, null, false)
                && (held || pawn.inventory?.Contains(request.food) == true || pawn.carryTracker?.CarriedThing == request.food
                    || pawn.Reserve(request.food, job, 10, count, null, false));
            if (!reserved)
            {
                pawn.ClearReservationsForJob(job);
                Manager.StartFloorFallback(request, false);
                return false;
            }
            request.foodCount = count;
            job.SetTarget(TargetIndex.C, request.food);
            job.count = 1; // Only one child is carried; food has its own quantity.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(CleanupInterrupted);
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            this.FailOn(() => RatDiningSpotFinder.FindAdjacentTable(Chair) != null);
            this.FailOn(() => !RatToddlerDiningUtility.CanEatMeal(Child, Request?.food, pawn));
            this.FailOn(() => Request?.IsActive != true || Request.helper != pawn || pawn.Drafted || pawn.Downed
                || !RatToddlerDiningUtility.IsHelperJob(Request, pawn) || Request.food != job.targetC.Thing
                || pawn.InMentalState || !RatToddlerMealCoordinator.SafeForChairMeal(Child)
                || !RatDiningCustody.ValidRequest(Request) || !RatToddlerMealCoordinator.CanHelp(pawn, Child, true));

            Toil begin = ToilMaker.MakeToil("RDE_BeginProvidedMeal");
            begin.initAction = delegate
            {
                if (Child.CurJob?.playerForced == true || Child.CurJobDef == JobDefOf.BabySuckle)
                { EndJobWith(JobCondition.Incompletable); return; }
                Job wait = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForMeal, Request.food, Chair);
                Child.jobs.StartJob(wait, JobCondition.InterruptForced, keepCarryingThingOverride: true,
                    preToilReservationsCanFail: true);
            };
            begin.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return begin;

            Toil approach = ToilMaker.MakeToil("RDE_ApproachProvidedFood");
            approach.initAction = delegate
            {
                if (RatToddlerDiningUtility.HoldsFood(Request) || pawn.inventory.Contains(Request.food)
                    || pawn.carryTracker.CarriedThing == Request.food) ReadyForNextToil();
                else if (Request.food?.Spawned == true) pawn.pather.StartPath(Request.food, PathEndMode.ClosestTouch);
                else EndJobWith(JobCondition.Incompletable);
            };
            approach.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return approach;

            Toil pickup = ToilMaker.MakeToil("RDE_StoreProvidedFood");
            pickup.initAction = delegate
            {
                if (RatToddlerDiningUtility.HoldsFood(Request) || pawn.inventory.Contains(Request.food)) return;
                if (pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing != Request.food)
                { EndJobWith(JobCondition.Incompletable); return; }
                if (pawn.carryTracker.CarriedThing == null)
                {
                    if (!RatToddlerDiningUtility.TryGetPickupCount(pawn, Request.food, Request.foodCount, out int count, Child))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    job.count = count;
                    Toil take = Toils_Haul.StartCarryThing(TargetIndex.C, reserve: false);
                    take.actor = pawn;
                    try { take.initAction(); }
                    finally { take.ReturnToPool(); }
                    if (pawn.CurJob != job) return;
                    job.count = 1;
                }
                Thing carried = pawn.carryTracker.CarriedThing;
                if (Request?.IsActive != true || carried == null || carried != job.targetC.Thing)
                { EndJobWith(JobCondition.Incompletable); return; }
                Request.food = carried;
                Request.foodCount = Math.Min(carried.stackCount, Request.foodCount);
                // Keep this meal as a separate stack so its saved reference cannot
                // be destroyed by merging with unrelated inventory food.
                if (pawn.carryTracker.innerContainer.TryTransferToContainer(carried, pawn.inventory.innerContainer,
                    carried.stackCount, out Thing stored, false) != carried.stackCount)
                { EndJobWith(JobCondition.Incompletable); return; }
                Request.food = stored;
                job.SetTarget(TargetIndex.C, stored);
                Child.CurJob.SetTarget(TargetIndex.A, stored);
            };
            pickup.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickup;
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil handoff = ToilMaker.MakeToil("RDE_GiveChildProvidedMeal");
            handoff.initAction = delegate
            {
                RatToddlerDiningRequest request = Request;
                if (Child.CurJobDef != RDE_DefOf.RDE_ToddlerWaitForMeal || pawn.carryTracker.CarriedThing != null)
                { EndJobWith(JobCondition.Incompletable); return; }
                if (!RatToddlerDiningUtility.HoldsFood(request))
                {
                    if (Child.carryTracker.CarriedThing != null || !pawn.inventory.Contains(request.food))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    int moved = pawn.inventory.innerContainer.TryTransferToContainer(request.food, Child.carryTracker.innerContainer,
                        request.foodCount, out Thing transferred, false);
                    if (moved <= 0 || transferred == null)
                    { EndJobWith(JobCondition.Incompletable); return; }
                    request.food = transferred;
                    request.foodCount = transferred.stackCount;
                    Child.CurJob.SetTarget(TargetIndex.A, transferred);
                    job.SetTarget(TargetIndex.C, transferred);
                }
                Manager.SetState(request, RatToddlerDiningState.BeingCarried);
            };
            handoff.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return handoff;
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, reserve: false, canTakeFromInventory: false);

            Toil check = ToilMaker.MakeToil("RDE_CheckProvidedMealCarry");
            check.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != Child || !RatToddlerDiningUtility.HoldsFood(Request))
                    EndJobWith(JobCondition.Incompletable);
            };
            check.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return check;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            Toil place = ToilMaker.MakeToil("RDE_PlaceChildWithProvidedMeal");
            place.initAction = delegate
            {
                RatToddlerDiningRequest request = Request;
                if (pawn.carryTracker.CarriedThing != Child || !Chair.Position.Standable(pawn.Map)
                    || RatDiningSpotFinder.FindAdjacentTable(Chair) != null
                    || !pawn.carryTracker.TryDropCarriedThing(Chair.Position, ThingPlaceMode.Direct, out Thing placed)
                    || placed != Child || Child.Position != Chair.Position || !RatToddlerDiningUtility.HoldsFood(request))
                { EndJobWith(JobCondition.Incompletable); return; }
                request.table = null;
                Manager.SetState(request, RatToddlerDiningState.PlacedAtChair);
                pawn.Map.reservationManager.Release(Chair, pawn, job);
                Job eat = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, request.food, Chair, Chair);
                eat.count = request.foodCount;
                Child.jobs.StartJob(eat, JobCondition.InterruptForced, keepCarryingThingOverride: true,
                    preToilReservationsCanFail: true);
            };
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return place;
        }

        private void CleanupInterrupted(JobCondition condition)
        {
            RatToddlerDiningRequest request = Request;
            if (request?.careHandoff != true && pawn.carryTracker.CarriedThing == Child && pawn.MapHeld != null)
                RatDiningCustody.TryDropChild(pawn, Child, request?.custody.originCell ?? IntVec3.Invalid);
            if (request?.IsActive != true || request.helper != pawn || condition == JobCondition.Succeeded
                || request.state == RatToddlerDiningState.PlacedAtChair || request.state == RatToddlerDiningState.EatingAtTable
                || request.state == RatToddlerDiningState.EatingOnChair) return;
            if (RatToddlerDiningUtility.HoldsFood(request)) Manager.Reopen(request);
            else Manager.StartFloorFallback(request, false);
        }
    }
}
