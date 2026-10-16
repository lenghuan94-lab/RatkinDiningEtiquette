using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    internal static class RatToddlerMealCoordinator
    {
        internal static bool SafeForChairMeal(Pawn child)
        {
            Map map = child?.MapHeld;
            return map != null && RatDiningMod.Settings.enableToddlerHelp
                && RatDiningApi.CanParticipateInToddlerDining(child) && !RatDiningApi.IsCriticalHunger(child)
                && !HealthAIUtility.ShouldSeekMedicalRest(child) && child.needs.rest?.CurLevelPercentage >= 0.1f
                && child.PositionHeld.IsValid && child.PositionHeld.InBounds(map)
                && child.PositionHeld.InAllowedArea(child) && child.PositionHeld.GetDangerFor(child, map) == Danger.None
                && GenTemperature.SafeTemperatureAtCell(child, child.PositionHeld, map);
        }

        internal static bool CanHelp(Pawn helper, Pawn child, bool requireChildcare = false)
        {
            WorkTypeDef work = child?.IsPrisonerOfColony == true ? WorkTypeDefOf.Warden : WorkTypeDefOf.Childcare;
            bool visitor = RatDiningCustody.IsVisitor(child);
            return helper != null && child != null && helper.Spawned && child.MapHeld == helper.Map
                && (visitor ? RatDiningCustody.IsVisitor(helper) && helper.Faction == child.Faction : helper.IsColonist)
                && !helper.IsPrisoner && RatDiningCustody.CanDine(helper) && RatDiningCustody.CanRequestHelp(child)
                && !helper.Dead && !helper.Downed && !helper.Drafted && !helper.InMentalState
                && !helper.DevelopmentalStage.Baby() && helper.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && !helper.WorkTypeIsDisabled(work)
                // Native NPCs can have a non-null, uninitialized tracker.
                // WorkIsActive would log an error and initialize player work
                // priorities. Read active priorities only when they exist.
                && (!requireChildcare || (visitor && helper.workSettings?.Initialized != true)
                    || helper.workSettings?.WorkIsActive(work) == true)
                && (RatDiningMod.Settings.allowNonParents || RatToddlerDiningUtility.IsParent(child, helper))
                && helper.Position.DistanceToSquared(child.PositionHeld) <= RatDiningMod.Settings.helperSearchRadius * RatDiningMod.Settings.helperSearchRadius
                && ChildcareUtility.CanHaulBaby(helper, child, out _);
        }

        internal static bool IsFeedingJob(Job job)
        {
            return job != null && (job.def == JobDefOf.BottleFeedBaby || job.def == JobDefOf.Breastfeed
                || job.def == JobDefOf.BreastfeedCarryToMom || job.def == JobDefOf.FeedPatient);
        }

        internal static Pawn FeedingTarget(Job job)
        { return !IsFeedingJob(job) ? null : job.def == JobDefOf.FeedPatient ? job.targetB.Pawn : job.targetA.Pawn; }

        private static bool IsWardenFeed(Job job)
        {
            return job?.def == JobDefOf.FeedPatient && job.workGiverDef?.giverClass != null
                && typeof(WorkGiver_Warden_Feed).IsAssignableFrom(job.workGiverDef.giverClass);
        }

        internal static bool ShouldDeferAutomaticFeeding(Pawn child)
        {
            if (!SafeForChairMeal(child)) return false;
            RatToddlerDiningRequest request = child.MapHeld.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(child);
            if (request?.IsActive != true || !RatDiningCustody.ValidRequest(request)
                || !RatToddlerDiningUtility.CanEatMeal(child, request.food, request.helper)
                || request.chair?.Spawned != true || request.chair.IsBurning()
                || request.chair.IsForbidden(child) || !request.chair.Position.InAllowedArea(child)) return false;
            if (request.table != null || RatDiningSpotFinder.FindAdjacentTable(request.chair) != null) return false;
            if (RatToddlerDiningUtility.HoldsFood(request)) return true;
            return request.state == RatToddlerDiningState.CollectingFood && RatToddlerDiningUtility.IsHelperJob(request, request.helper)
                || request.state == RatToddlerDiningState.MovingFoodToDiningSpot
                    && child.CurJobDef == RDE_DefOf.RDE_ToddlerPrepareDiningRequest;
        }

        // Called only when an automatic feeding job actually starts, never during
        // a WorkGiver scan. Both entrances consequently acquire the same map index.
        internal static bool TryRedirectFeeding(Pawn helper, ref Job feeding)
        {
            if (!IsFeedingJob(feeding)) return false;
            Pawn child = FeedingTarget(feeding);
            if (!RatDiningApi.IsRatkin(child)) return false;
            MapComponent_RatToddlerDining manager = child.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
            RatToddlerDiningRequest request = manager?.GetForPawn(child);
            // Medical FeedPatient jobs and other workgivers retain their original
            // target layout and behavior. A valid chair request yields to them.
            if (feeding.def == JobDefOf.FeedPatient && (!child.IsPrisonerOfColony || !IsWardenFeed(feeding)))
            {
                if (request?.IsActive == true) manager.CancelForFeeding(request);
                return false;
            }
            if (request?.IsActive == true && (request.table != null || RatDiningSpotFinder.FindAdjacentTable(request.chair) != null))
            { manager.CancelForFeeding(request); return false; }
            if (feeding.playerForced || !SafeForChairMeal(child))
            {
                if (request?.IsActive == true) manager.CancelForFeeding(request);
                return false;
            }
            if (!RatDiningApi.IsEligibleToddler(child)) return false;
            // An unrelated faction's routine feeding cannot be turned into a
            // wait. Medical and player-ordered feeding above still cancel this
            // meal so their original care job can take priority.
            if (RatDiningCustody.IsVisitor(child) && helper?.Faction != child.Faction) return false;
            if (request?.IsActive == true && (request.helper != null || request.state == RatToddlerDiningState.EatingAtTable
                || request.state == RatToddlerDiningState.EatingOnChair || request.state == RatToddlerDiningState.PlacedAtChair))
            {
                if (!ShouldDeferAutomaticFeeding(child)) return false;
                // A job selected just before another caregiver claimed this meal
                // must not start a second feeding operation.
                feeding = JobMaker.MakeJob(JobDefOf.Wait, 60);
                return true;
            }
            if (manager == null || manager.IsCoolingDown(child) || manager.IsRecovering(child) || !CanHelp(helper, child, true)
                || child.CurJob?.playerForced == true || child.CurJobDef == JobDefOf.BabySuckle
                || child.CurJobDef == JobDefOf.Ingest || child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor
                || FeedingTarget(helper.CurJob) == child
                || !helper.CanReserveAndReach(child, PathEndMode.Touch, Danger.Some)) return false;

            Building chair;
            Building table;
            IntVec3 staging;
            Thing held = child.carryTracker?.CarriedThing;
            Thing food = held ?? request?.food ?? (feeding.def == JobDefOf.BottleFeedBaby ? feeding.targetB.Thing
                : feeding.def == JobDefOf.FeedPatient ? feeding.targetA.Thing : null)
                ?? ChildcareUtility.FindBabyFoodForBaby(helper, child);
            if (!RatToddlerDiningUtility.CanEatMeal(child, food, helper)) return false;
            if (request?.IsActive == true)
            { chair = request.chair; table = request.table; staging = request.stagingCell; }
            else
            {
                // Both meal entrances must respect vanilla/the table mod's
                // chosen dining position before starting a lone-chair request.
                // Once a request is active, retain it rather than rerolling the
                // table mod's preference and flipping between meal providers.
                if (!RatDiningModeResolver.TryResolve(child, food, out RatDiningMode mode, out chair, out staging, out table)
                    || mode != RatDiningMode.ToddlerNeedsAssistance) return false;
            }
            if (!RatDiningSpotFinder.IsUsableChair(child, chair)
                || !helper.CanReserveAndReach(chair, PathEndMode.Touch, Danger.Some)) return false;
            int wanted = FoodUtility.WillIngestStackCountOf(child, food.def, FoodUtility.NutritionForEater(child, food));
            int count;
            if (held == food) count = Math.Min(food.stackCount, Math.Max(1, wanted));
            else if (!RatToddlerDiningUtility.TryGetPickupCount(helper, food, wanted, out count, child)) return false;
            if (request == null || !request.IsActive) request = manager.Create(child, food, chair, table, staging);
            if (request == null) return false;
            Job provide = JobMaker.MakeJob(RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, child, chair, food);
            provide.count = count;
            request.food = food;
            request.foodCount = count;
            manager.BeginProvision(request, helper, provide);
            feeding = provide;
            return true;
        }

        // Workgiver scans only plan. Claims are acquired in StartJob, after a
        // particular caregiver has actually been selected by the scheduler.
        internal static bool TryPlanPrisonerMeal(Pawn helper, Pawn child, out Thing food,
            out Building chair, out IntVec3 staging, out int count)
        { return TryPlanCareMeal(helper, child, true, out food, out chair, out staging, out count); }

        internal static bool TryPlanVisitorMeal(Pawn helper, Pawn child, out Thing food,
            out Building chair, out IntVec3 staging, out int count)
        { return TryPlanCareMeal(helper, child, false, out food, out chair, out staging, out count); }

        private static bool TryPlanCareMeal(Pawn helper, Pawn child, bool prisoner, out Thing food,
            out Building chair, out IntVec3 staging, out int count)
        {
            food = null; chair = null; staging = IntVec3.Invalid; count = 0;
            MapComponent_RatToddlerDining manager = child?.Map?.GetComponent<MapComponent_RatToddlerDining>();
            if (!(prisoner ? child?.IsPrisonerOfColony == true : RatDiningCustody.IsVisitor(child)) || !RatDiningApi.IsEligibleToddler(child)
                || !SafeForChairMeal(child) || !CanHelp(helper, child, true) || manager == null
                || manager.IsCoolingDown(child) || manager.IsRecovering(child) || manager.GetForPawn(child)?.IsActive == true
                || !FeedPatientUtility.IsHungry(child)
                || !prisoner && child.needs.food.CurLevelPercentage >= child.RaceProps.FoodLevelPercentageWantEat
                || !prisoner && !IsVisitorIdleJob(child.CurJob)
                || child.CurJob?.playerForced == true
                || child.CurJobDef == JobDefOf.BabySuckle || child.CurJobDef == JobDefOf.Ingest
                || child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor
                || FeedingTarget(helper.CurJob) == child
                || !helper.CanReserveAndReach(child, PathEndMode.Touch, Danger.Some)) return false;
            food = child.carryTracker?.CarriedThing ?? ChildcareUtility.FindBabyFoodForBaby(helper, child);
            if (!RatToddlerDiningUtility.CanEatMeal(child, food, helper)) return false;
            if (!RatDiningModeResolver.TryResolve(child, food, out RatDiningMode mode, out chair, out staging, out _)
                || mode != RatDiningMode.ToddlerNeedsAssistance
                || !helper.CanReserveAndReach(chair, PathEndMode.Touch, Danger.Some)) return false;
            int wanted = FoodUtility.WillIngestStackCountOf(child, food.def, FoodUtility.NutritionForEater(child, food));
            if (child.carryTracker?.CarriedThing == food) count = Math.Min(food.stackCount, Math.Max(1, wanted));
            else if (!RatToddlerDiningUtility.TryGetPickupCount(helper, food, wanted, out count, child)) return false;
            return count > 0;
        }

        internal static bool IsVisitorIdleJob(Job job)
        {
            return job == null || !job.playerForced && !job.exitMapOnArrival
                && (job.def == JobDefOf.GotoWander || job.def == JobDefOf.Wait_Wander || job.def == JobDefOf.Wait);
        }

        internal static bool TryBeginPlannedProvision(Pawn helper, Job job)
        {
            if (job?.def != RDE_DefOf.RDE_ProvideRatToddlerDiningMeal) return false;
            Pawn child = job.targetA.Pawn;
            MapComponent_RatToddlerDining manager = child?.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
            RatToddlerDiningRequest existing = manager?.GetForPawn(child);
            if (existing?.IsActive == true)
                return existing.helper == helper && existing.helperJobId == job.loadID
                    && existing.food == job.targetC.Thing && existing.chair == job.targetB.Thing;
            if (!TryPlanCareMeal(helper, child, child?.IsPrisonerOfColony == true,
                out Thing food, out Building chair, out IntVec3 staging, out int count)
                || food != job.targetC.Thing || chair != job.targetB.Thing) return false;
            RatToddlerDiningRequest request = manager.Create(child, food, chair, null, staging);
            if (request == null) return false;
            job.count = count;
            request.foodCount = count;
            manager.BeginProvision(request, helper, job);
            return request.helper == helper && request.helperJobId == job.loadID;
        }
    }
}
