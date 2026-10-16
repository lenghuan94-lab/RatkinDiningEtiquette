using RimWorld;
using Toddlers;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    internal static class RatDiningCarryRecovery
    {
        internal const int MaxReturnAttempts = 3;
        internal const int RecoveryTimeoutTicks = 1200;
        internal const int ReturnTimeoutTicks = 600;

        internal static bool IsCareOrder(Job job, Pawn child)
        {
            return job != null && job.def != RDE_DefOf.RDE_AssistRatToddlerDining
                && job.def != RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && job.def != RDE_DefOf.RDE_ReturnDiningChild
                && (job.playerForced || job.def == JobDefOf.BringBabyToSafety
                || job.def == JobDefOf.BringBabyToSafetyUnforced
                || job.AnyTargetIs(child));
        }

        internal static RatToddlerDiningRequest GetContext(Pawn helper, bool includeHandoff = false)
        {
            Pawn child = helper?.carryTracker?.CarriedThing as Pawn;
            if (!RatDiningApi.IsRatkin(child)) return null;
            MapComponent_RatToddlerDining manager = helper.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
            RatToddlerDiningRequest request = manager?.GetForPawn(child) ?? manager?.GetRecoveringForPawn(child);
            return request?.helper == helper && (includeHandoff || !request.careHandoff) ? request : null;
        }

        internal static bool CanContinueReturn(RatToddlerDiningRequest request)
        {
            Pawn child = request?.toddler;
            return request != null && !request.careHandoff && RatDiningApi.IsRatkin(child)
                && RatDiningCustody.CanDine(child) && !HealthAIUtility.ShouldSeekMedicalRest(child)
                && !RatDiningApi.IsCriticalHunger(child)
                && (!request.custody.initialized || request.custody.Matches(child));
        }

        internal static bool IsSafeReturnTarget(Pawn helper, Pawn child, IntVec3 target, IntVec3 origin)
        {
            return RatDiningCustody.IsLegalRecoveryCell(child, target, origin)
                && helper?.Map != null && child.MapHeld == helper.Map
                && target.Standable(helper.Map) && target.InAllowedArea(helper)
                && !target.IsForbidden(helper) && target.GetDangerFor(child, helper.Map) != Danger.Deadly
                && GenTemperature.SafeTemperatureAtCell(child, target, helper.Map);
        }

        internal static bool CanKeepReturnJob(Pawn helper)
        {
            Job current = helper?.CurJob;
            if (current?.def != RDE_DefOf.RDE_ReturnDiningChild || current.playerForced
                || HealthAIUtility.ShouldSeekMedicalRest(helper)) return false;
            RatToddlerDiningRequest request = GetContext(helper);
            return request != null && request.recoveryPending && request.returnJobId == current.loadID
                && current.targetA.Pawn == request.toddler && current.targetC.Cell == request.custody.originCell
                && CanContinueReturn(request) && request.recoveryStartedTick >= 0
                && Find.TickManager.TicksGame - request.recoveryStartedTick < RecoveryTimeoutTicks
                && IsSafeReturnTarget(helper, request.toddler, current.targetB.Cell, current.targetC.Cell);
        }

        internal static bool TryMakeReturnJob(RatToddlerDiningRequest request, out Job job)
        {
            job = null;
            Pawn helper = request?.helper, child = request?.toddler;
            if (helper?.carryTracker?.CarriedThing != child || child == null || child.Dead
                || !helper.Spawned || helper.Dead || helper.Downed || helper.InMentalState || helper.Drafted
                || !helper.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                || !CanContinueReturn(request)
                || request.careHandoff || request.returnAttempts >= MaxReturnAttempts
                || request.recoveryStartedTick >= 0 && Find.TickManager.TicksGame - request.recoveryStartedTick >= RecoveryTimeoutTicks)
                return false;
            IntVec3 origin = request.custody.originCell;
            if (!TryTarget(origin, out IntVec3 target)
                && !TryTarget(request.stagingCell, out target)
                && !TryTarget(request.chair?.Position ?? IntVec3.Invalid, out target))
            {
                // Reuse Toddlers' bed/temperature policy as a final candidate;
                // our return driver still verifies safety and current custody.
                LocalTargetInfo native = BabyMoveUtility.BestPlaceForBaby(child, helper);
                if (!native.IsValid || !TryTarget(native.Cell, out target)) return false;
            }
            job = JobMaker.MakeJob(RDE_DefOf.RDE_ReturnDiningChild, child, target, origin);
            job.count = 1;
            return true;

            bool TryTarget(IntVec3 root, out IntVec3 result)
            {
                result = IntVec3.Invalid;
                if (!root.IsValid || !root.InBounds(helper.Map)) return false;
                if (Accept(root)) { result = root; return true; }
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 cell = root + GenAdj.CardinalDirections[i];
                    if (Accept(cell)) { result = cell; return true; }
                }
                return false;
            }

            bool Accept(IntVec3 cell)
            {
                return RatDiningCustody.IsLegalRecoveryCell(child, cell, origin)
                    && cell.Standable(helper.Map) && !cell.IsForbidden(helper) && cell.InAllowedArea(helper)
                    && cell.GetDangerFor(child, helper.Map) != Danger.Deadly
                    && GenTemperature.SafeTemperatureAtCell(child, cell, helper.Map)
                    && helper.CanReserveSittableOrSpot(cell) && helper.CanReach(cell, PathEndMode.OnCell, Danger.Some);
            }
        }

        internal static void CoordinateStart(Pawn helper, ref Job newJob, ref bool? keepCarryingThingOverride,
            ref bool preToilReservationsCanFail)
        {
            RatToddlerDiningRequest request = GetContext(helper);
            if (request == null || newJob == null) return;
            if (newJob.def == RDE_DefOf.RDE_AssistRatToddlerDining || newJob.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal) return;
            if (newJob.def == RDE_DefOf.RDE_ReturnDiningChild && newJob.targetA.Pawn == request.toddler)
            {
                if (request.returnJobId != newJob.loadID)
                { request.returnAttempts++; request.returnJobId = newJob.loadID; }
                keepCarryingThingOverride = true;
                preToilReservationsCanFail = true;
                return;
            }
            MapComponent_RatToddlerDining manager = helper.MapHeld.GetComponent<MapComponent_RatToddlerDining>();
            // Explicit care or another urgent order owns the child from here.
            // Preserve that order and the carried child; never insert a meal.
            if (IsCareOrder(newJob, request.toddler))
            {
                manager.HandOffCare(request);
                keepCarryingThingOverride = true;
                return;
            }
            if (RatDiningCustody.TryDropChild(helper, request.toddler, request.custody.originCell)) return;
            if (request.IsActive) manager.StartFloorFallback(request, false);
            keepCarryingThingOverride = true;
            preToilReservationsCanFail = true;
            if (TryMakeReturnJob(request, out Job returning))
            {
                newJob = returning;
                request.returnAttempts++;
                request.returnJobId = returning.loadID;
            }
            else
            {
                // No legal placement/route remains. Relinquish dining control
                // to the installed vanilla/Toddlers rescue driver, once only.
                manager.HandOffCare(request);
                newJob = JobMaker.MakeJob(JobDefOf.BringBabyToSafety, request.toddler);
            }
        }
    }
}
