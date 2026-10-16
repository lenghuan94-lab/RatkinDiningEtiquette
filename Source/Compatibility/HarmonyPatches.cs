using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    [HarmonyAfter("cyanobot.toddlers")]
    internal static class Pawn_JobTracker_StartJob_CoordinateToddlerMeal
    {
        private static void Prefix(Pawn ___pawn, ref Job newJob, ref bool preToilReservationsCanFail,
            ref bool? keepCarryingThingOverride)
        {
            RatDiningCarryRecovery.CoordinateStart(___pawn, ref newJob, ref keepCarryingThingOverride,
                ref preToilReservationsCanFail);
            if (newJob?.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal)
            {
                preToilReservationsCanFail = true;
                if (!RatToddlerMealCoordinator.TryBeginPlannedProvision(___pawn, newJob))
                    newJob = JobMaker.MakeJob(JobDefOf.Wait, 60);
                else if (___pawn.carryTracker?.CarriedThing == newJob.targetC.Thing)
                    keepCarryingThingOverride = true;
            }
            if (RatToddlerMealCoordinator.TryRedirectFeeding(___pawn, ref newJob))
            {
                preToilReservationsCanFail = true;
                if (newJob.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal)
                    keepCarryingThingOverride = ___pawn.carryTracker?.CarriedThing == newJob.targetC.Thing;
            }
            if (newJob == null || newJob.playerForced || !RatDiningApi.IsRatkin(___pawn)) return;
            MapComponent_RatToddlerDining manager = ___pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
            if (newJob.def == JobDefOf.Wait && manager?.GetRecoveringForPawn(___pawn)?.food == ___pawn.carryTracker?.CarriedThing
                && ___pawn.carryTracker?.CarriedThing != null)
                keepCarryingThingOverride = true;
            if (newJob.def != JobDefOf.Ingest) return;
            if (manager?.TryMakeRecoveryJob(___pawn, out Job recovery) == true)
            {
                newJob = recovery;
                keepCarryingThingOverride = ___pawn.carryTracker?.CarriedThing == recovery.targetA.Thing;
                preToilReservationsCanFail = true;
            }
        }
    }

    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.WantsSuckle))]
    [HarmonyAfter("cyanobot.toddlers")]
    internal static class ChildcareUtility_WantsSuckle_DiningRequest
    {
        private static void Postfix(Pawn baby, ref bool __result)
        {
            if (__result && RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(baby)) __result = false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_PlayWithBaby), nameof(WorkGiver_PlayWithBaby.HasJobOnThing))]
    [HarmonyAfter("cyanobot.toddlers")]
    internal static class WorkGiver_PlayWithBaby_DeferDiningRequest
    {
        private static void Postfix(Thing t, bool forced, ref bool __result)
        {
            // Native play work does not ask Toddlers.IsBabyBusy. Keep routine
            // play from replacing a safe, indexed meal; manual orders and
            // urgent care retain their normal behavior.
            if (__result && !forced && t is Pawn baby
                && RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(baby)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class Pawn_JobTracker_StartJob_DiningReservations
    {
        private static void Prefix(Pawn ___pawn, Job newJob, ref bool preToilReservationsCanFail)
        {
            if (!RatDiningApi.IsRatkin(___pawn) || newJob == null
                || (!RatToddlerDiningUtility.IsWaitingJob(newJob.def) && newJob.def != RDE_DefOf.RDE_ToddlerEatAtChair)) return;
            RatToddlerDiningRequest request = ___pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(___pawn);
            if (request?.IsActive != true || newJob.targetA.Thing != request.food || newJob.targetB.Thing != request.chair) return;
            // Another job may claim a portion or cell after selection. Let the driver
            // release this request and retry normal food selection without error recovery.
            preToilReservationsCanFail = true;
        }
    }

    [HarmonyPatch(typeof(Toddlers.ToddlerUtility), nameof(Toddlers.ToddlerUtility.IsBabyBusy))]
    internal static class ToddlerUtility_IsBabyBusy_DiningRequest
    {
        private static void Postfix(Pawn baby, ref bool __result)
        {
            if (__result || !RatDiningApi.IsEligibleToddler(baby) || RatDiningApi.IsCriticalHunger(baby)) return;
            RatToddlerDiningRequest request = baby.Map.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(baby);
            if (request?.IsActive != true || !RatToddlerDiningUtility.HoldsFood(request)
                || (!RatToddlerDiningUtility.IsWaitingJob(baby.CurJobDef) && baby.CurJobDef != RDE_DefOf.RDE_ToddlerEatAtChair)
                || HealthAIUtility.ShouldSeekMedicalRest(baby)
                || baby.needs.rest?.CurLevelPercentage < 0.1f
                || !baby.Position.InAllowedArea(baby) || baby.Position.GetDangerFor(baby, baby.Map) != Danger.None
                || !GenTemperature.SafeTemperatureAtCell(baby, baby.Position, baby.Map)) return;
            // Routine Toddlers relocation may wait for a safe active meal. Rescue and
            // direct carrying orders still use the unmodified childcare/job APIs.
            __result = true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    [HarmonyAfter("cyanobot.toddlers")]
    internal static class Pawn_JobTracker_StartJob_PreserveToddlerMeal
    {
        private static void Prefix(Pawn ___pawn, Job newJob, ref bool? keepCarryingThingOverride)
        {
            // Only the handoff to our own caregiver may preserve a carried meal.
            // Never suppress player orders, rescue jobs or other need jobs.
            if (newJob?.def != JobDefOf.Carried || !RatDiningApi.IsRatkin(___pawn)) return;
            RatToddlerDiningRequest request = ___pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(___pawn);
            if (request?.IsActive != true || request.state != RatToddlerDiningState.BeingCarried
                || !RatToddlerDiningUtility.IsHelperJob(request, request.helper)
                || !RatToddlerDiningUtility.HoldsFood(request)) return;
            keepCarryingThingOverride = true;
        }
    }
}
