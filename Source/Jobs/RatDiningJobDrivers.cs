using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    public abstract class JobDriver_RatDiningIngestBase : JobDriver_Ingest
    {
        private static readonly System.Reflection.FieldInfo ChewingField = AccessTools.Field(typeof(JobDriver_Ingest), "chewing");
        private int ateWithoutTableBefore;
        private int diningRequestId = -1;
        protected bool MealFinalized;

        protected Thing Food => job.targetA.Thing;
        protected Building Chair => job.targetB.Thing as Building;
        protected MapComponent_RatToddlerDining DiningManager => pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
        protected RatToddlerDiningRequest DiningRequest => RatToddlerDiningUtility.GetBoundRequest(DiningManager, pawn, ref diningRequestId);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref diningRequestId, "diningRequestId", -1);
            Scribe_Values.Look(ref ateWithoutTableBefore, "ateWithoutTableBefore");
            Scribe_Values.Look(ref MealFinalized, "mealFinalized");
            base.ExposeData();
        }

        protected Toil FinalizeMeal()
        {
            Toil finalize = Toils_Ingest.FinalizeIngest(pawn, TargetIndex.A);
            Action original = finalize.initAction;
            finalize.initAction = delegate
            {
                bool toddlerChairMeal = job.def == RDE_DefOf.RDE_ToddlerEatAtChair;
                RatToddlerDiningRequest request = toddlerChairMeal ? DiningRequest : null;
                if (toddlerChairMeal && (MealFinalized || DiningManager?.IsFinalizedMeal(request, job) == true))
                {
                    MealFinalized = true;
                    DiningManager?.MarkMealFinalized(request, job);
                    return; // A loaded completed finalizer must not consume a second portion.
                }
                RatDiningMealSafety.EnsureFacingInBounds(pawn);
                bool chairMeal = RatDiningApi.IsRatkin(pawn) && Chair != null
                    && RatDiningFurniture.AllowedFor(pawn, Chair)
                    && (job.def == RDE_DefOf.RDE_EatUsingChairAsTable || job.def == RDE_DefOf.RDE_ToddlerEatAtChair)
                    && RatDiningSpotFinder.FindAdjacentTable(Chair) == null;
                using (RatDiningMealSafety.IngestionObservation meal = RatDiningMealSafety.ObserveIngestion(pawn, Food, job))
                {
                    try
                    {
                        if (chairMeal) { using (RatDiningMealSafety.FinalizeChair(pawn)) original(); }
                        else original();
                    }
                    finally
                    {
                        // Preserve a real Ingested result even if a later native
                        // finalizer step throws; never replay already-consumed food.
                        MealFinalized |= meal.ate;
                        if (toddlerChairMeal && MealFinalized) DiningManager?.MarkMealFinalized(request, job);
                    }
                }
            };
            return finalize;
        }

        internal bool HasFinalizedMeal => MealFinalized;

        protected Toil MakeChewToil(TargetIndex eatSurface, float durationMultiplier)
        {
            float speed = Food?.def?.ingestible?.useEatingSpeedStat == false ? 1f : pawn.GetStatValue(StatDefOf.EatingSpeed);
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f) speed = 1f;
            Toil chew = Toils_Ingest.ChewIngestible(pawn, durationMultiplier / speed, TargetIndex.A, eatSurface);
            ChewingField?.SetValue(this, chew);
            return chew;
        }

        protected Toil SnapshotTableThought()
        {
            Toil toil = ToilMaker.MakeToil("RDE_SnapshotTableThought");
            toil.initAction = delegate { ateWithoutTableBefore = CountAteWithoutTable(); };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        protected Toil CorrectChairThought()
        {
            Toil toil = ToilMaker.MakeToil("RDE_CorrectDiningThought");
            toil.initAction = delegate
            {
                MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
                if (memories == null || !MealFinalized || !RatDiningApi.IsRatkin(pawn)
                    || Chair?.Spawned != true || Chair.Destroyed || Chair.Map != pawn.Map
                    || !RatDiningFurniture.AllowedFor(pawn, Chair)
                    || RatDiningSpotFinder.FindAdjacentTable(Chair) != null) return;
                RatDiningThoughts.ApplyChair(pawn, Chair);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private int CountAteWithoutTable()
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            return memories?.Memories?.Count(m => m.def == ThoughtDefOf.AteWithoutTable) ?? 0;
        }
    }

    public sealed class JobDriver_EatUsingChairAsTable : JobDriver_RatDiningIngestBase
    {
        private readonly RatDiningCustodySnapshot custody = new RatDiningCustodySnapshot();

        public override void ExposeData()
        {
            custody.ExposeData();
            base.ExposeData();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            custody.Bind(pawn, pawn.Position);
            int count = 0;
            bool valid = RatDiningApi.IsRatkin(pawn) && RatDiningMod.Settings.enableChairTable
                && RatDiningSpotFinder.IsUsableChair(pawn, Chair)
                && RatDiningCustody.IsLegalCell(pawn, job.targetC.Cell, custody.originCell)
                && RatToddlerDiningUtility.TryGetPickupCount(pawn, Food, job.count, out count);
            if (valid) job.count = count;
            bool foodReserved = valid && (pawn.carryTracker?.CarriedThing == Food || pawn.inventory?.Contains(Food) == true
                || pawn.Reserve(job.targetA, job, 10, job.count, null, false));
            bool chairReserved = foodReserved && pawn.Reserve(job.targetB, job, 1, -1, null, false);
            bool spotReserved = chairReserved && pawn.ReserveSittableOrSpot(job.targetC.Cell, job, false);
            if (!spotReserved)
            {
                pawn.ClearReservationsForJob(job);
                RatDiningFailureGuard.NotifyEnded(pawn, JobCondition.Incompletable);
                RatDiningApi.Debug($"Reservation failed: food={foodReserved}, chair={chairReserved}, spot={spotReserved}, job={job}");
            }
            return spotReserved;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition => RatDiningFailureGuard.NotifyEnded(pawn, condition));
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnBurningImmobile(TargetIndex.B);
            custody.Bind(pawn, pawn.Position); // Old saves bind once before the first resumed toil.
            this.FailOn(() => !RatDiningApi.IsRatkin(pawn) || !custody.Matches(pawn) || !RatDiningCustody.CanDine(pawn)
                || !RatDiningMod.Settings.enableChairTable || !RatDiningFurniture.AllowedFor(pawn, Chair)
                || Chair.IsForbidden(pawn) || !Chair.Position.InAllowedArea(pawn)
                || !RatDiningCustody.IsLegalCell(pawn, Chair.Position, custody.originCell)
                || !RatDiningCustody.IsLegalCell(pawn, job.targetC.Cell, custody.originCell)
                || RatDiningSpotFinder.FindAdjacentTable(Chair) != null);
            if (!EatingFromInventory)
            {
                Toil approach = ToilMaker.MakeToil("RDE_ApproachChairMeal");
                approach.initAction = delegate
                {
                    if (pawn.carryTracker?.CarriedThing == Food) ReadyForNextToil();
                    else if (Food?.Spawned == true) pawn.pather.StartPath(job.targetA, PathEndMode.ClosestTouch);
                    else EndJobWith(JobCondition.Incompletable);
                };
                approach.defaultCompleteMode = ToilCompleteMode.PatherArrival;
                yield return approach.FailOnDestroyedOrNull(TargetIndex.A);
            }
            Toil pickup = ToilMaker.MakeToil("RDE_PickupChairMeal");
            pickup.initAction = delegate
            {
                if (pawn.carryTracker?.CarriedThing == Food) return;
                if (!RatToddlerDiningUtility.TryGetPickupCount(pawn, Food, job.count, out int count))
                { EndJobWith(JobCondition.Incompletable); return; }
                job.count = count;
                Toil take = pawn.inventory?.Contains(Food) == true
                    ? Toils_Misc.TakeItemFromInventoryToCarrier(pawn, TargetIndex.A)
                    : Toils_Haul.StartCarryThing(TargetIndex.A, reserve: false);
                take.actor = pawn;
                try { take.initAction(); }
                finally { take.ReturnToPool(); }
            };
            pickup.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickup;
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            yield return DropFoodOnChair();
            yield return SnapshotTableThought();
            Toil chew = MakeChewToil(TargetIndex.B, 1f);
            yield return chew;
            yield return FinalizeMeal();
            yield return CorrectChairThought();
            // Keep the first eight saved toil indices, and repeat body-part
            // bites like vanilla rather than finishing a corpse meal hungry.
            if (Food is Corpse)
                yield return Toils_Jump.JumpIf(chew, () => Food != null && !Food.Destroyed && Food.IngestibleNow
                    && pawn.needs.food != null && pawn.needs.food.CurLevelPercentage < 0.9f);
        }

        private Toil DropFoodOnChair()
        {
            Toil toil = ToilMaker.MakeToil("RDE_DropFoodOnChair");
            toil.initAction = delegate
            {
                if (!pawn.carryTracker.TryDropCarriedThing(Chair.Position, ThingPlaceMode.Direct, out Thing dropped))
                { EndJobWith(JobCondition.Incompletable); return; }
                job.SetTarget(TargetIndex.A, dropped);
                pawn.Rotation = Rot4.FromAngleFlat((Chair.Position - pawn.Position).AngleFlat);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }

}
