using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    [HarmonyPatch(typeof(JobGiver_Wander), "TryGiveJob")]
    [HarmonyAfter("cyanobot.toddlers")]
    internal static class JobGiver_Wander_VisitorDining
    {
        private static void Postfix(Pawn pawn, ref Job __result)
        {
            // Only replace an idle job that the native duty tree actually
            // selected. Never issue work from MapComponentTick or override a
            // higher-priority trade, travel, combat, medical or feeding result.
            if (__result == null || (__result.def != JobDefOf.GotoWander && __result.def != JobDefOf.Wait_Wander)
                || __result.playerForced || __result.exitMapOnArrival) return;
            if (CanContinueMeal(pawn))
            {
                // Native ShouldStartJobFromThinkTree preserves this exact job
                // reference. A fresh assist/provide job would lose its saved
                // claim ID; a normal wander would interrupt an owned meal.
                JobMaker.ReturnToPool(__result);
                __result = pawn.CurJob;
                return;
            }
            if (!RatDiningMod.Settings.enableToddlerHelp || !RatDiningCustody.IsVisitor(pawn)
                || !RatDiningCustody.CanDine(pawn) || pawn.DevelopmentalStage.Baby()
                || pawn.CurJob?.playerForced == true || pawn.carryTracker?.CarriedThing != null
                || RatToddlerMealCoordinator.IsFeedingJob(pawn.CurJob)
                || HealthAIUtility.ShouldSeekMedicalRest(pawn) || pawn.needs?.food == null
                || pawn.needs.food.CurLevelPercentage < pawn.RaceProps.FoodLevelPercentageWantEat
                || pawn.needs.rest?.CurLevelPercentage < .1f
                || !pawn.Position.InBounds(pawn.Map) || pawn.Position.GetDangerFor(pawn, pawn.Map) != Danger.None) return;
            MapComponent_RatToddlerDining manager = pawn.Map.GetComponent<MapComponent_RatToddlerDining>();
            if (manager == null) return;
            // Child-first meals use the request index, not a map-wide scan.
            for (int i = 0; i < manager.Requests.Count; i++)
            {
                RatToddlerDiningRequest request = manager.Requests[i];
                if (request.toddler?.Faction != pawn.Faction || !RatDiningCustody.IsVisitor(request.toddler)
                    || WorkGiver_AssistRatToddlerDining.GetSharedRejectionReason(pawn, request.toddler) != null) continue;
                __result = JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, request.toddler, request.chair);
                __result.count = 1;
                return;
            }
            // Caregiver-first planning reads the faction pawn lister only at
            // this idle decision. It acquires no requests or reservations;
            // StartJob revalidates the chosen meal before claiming anything.
            foreach (Pawn child in pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction))
            {
                if (!RatDiningApi.IsEligibleToddler(child)
                    || !RatToddlerMealCoordinator.TryPlanVisitorMeal(pawn, child, out Thing food,
                        out Building chair, out _, out int count)) continue;
                __result = JobMaker.MakeJob(RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, child, chair, food);
                __result.count = count;
                return;
            }
        }

        private static bool CanContinueMeal(Pawn pawn)
        {
            if (!RatDiningCustody.IsVisitor(pawn) || !RatDiningCustody.CanDine(pawn)) return false;
            Job current = pawn.CurJob;
            if (current == null || current.playerForced) return false;
            // Safety cleanup must survive routine idle reevaluation even when
            // this visitor category or the master experiment is now disabled.
            // The original bounded deadline and destination checks still apply.
            if (current.def == RDE_DefOf.RDE_ReturnDiningChild)
                return RatDiningCarryRecovery.CanKeepReturnJob(pawn);
            // A completed recovery handoff retires the map request. Its held
            // floor meal still belongs to this Ratkin, and idle wandering must
            // not drop that food while it is being eaten. Medical and changed
            // visitor duties continue to take precedence.
            if (current.def == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor)
                return RatDiningApi.CanSafelySelfFeed(pawn) && !HealthAIUtility.ShouldSeekMedicalRest(pawn)
                    && pawn.carryTracker?.CarriedThing == current.targetA.Thing
                    && RatToddlerDiningUtility.CanEatMeal(pawn, current.targetA.Thing);
            MapComponent_RatToddlerDining manager = pawn.Map?.GetComponent<MapComponent_RatToddlerDining>();
            if (manager == null) return false;
            if (current.def == RDE_DefOf.RDE_AssistRatToddlerDining || current.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal)
            {
                RatToddlerDiningRequest request = manager.GetForPawn(current.targetA.Pawn);
                return request?.helper == pawn && manager.CanResumeRequest(request)
                    && RatToddlerDiningUtility.IsHelperJob(request, pawn)
                    && RatToddlerMealCoordinator.CanHelp(pawn, request.toddler, true)
                    && !HealthAIUtility.ShouldSeekMedicalRest(pawn) && !RatDiningApi.IsCriticalHunger(pawn)
                    && (current.def != RDE_DefOf.RDE_ProvideRatToddlerDiningMeal || current.targetC.Thing == request.food);
            }
            if (!RatDiningApi.IsRatkin(pawn) || !RatToddlerDiningUtility.IsToddlerJob(current.def)) return false;
            RatToddlerDiningRequest meal = manager.GetForPawn(pawn);
            return manager.CanResumeRequest(meal) && current.targetA.Thing == meal.food && current.targetB.Thing == meal.chair
                && ((current.def != RDE_DefOf.RDE_ToddlerPrepareDiningRequest && current.def != RDE_DefOf.RDE_ToddlerWaitForDiningHelp)
                    || current.targetC.Cell == meal.stagingCell);
        }
    }
}
