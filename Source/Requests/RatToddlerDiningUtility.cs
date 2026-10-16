using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    internal static class RatToddlerDiningUtility
    {
        internal static bool CanEatMeal(Pawn eater, Thing food, Pawn getter = null)
        {
            // Self-feeding ability does not bypass Baby-stage food permission.
            // Use native rules so compatible milk/food is supported without a
            // BabyFood Def-name whitelist or the adult getter's diet.
            return eater != null && food != null && !(food is Pawn) && !(food is Building) && !(food is Corpse)
                && !food.Destroyed && food.IngestibleNow && food.def?.IsNutritionGivingIngestible == true
                && food.def.ingestible != null && eater.WillEat(food, getter);
        }

        internal static bool TryGetPickupCount(Pawn pawn, Thing food, int wantedCount, out int count, Pawn recipient = null)
        {
            count = 0;
            if (pawn?.Map == null || food == null || food.Destroyed || !food.IngestibleNow
                || food.def.IsNutritionGivingIngestible != true) return false;
            if (recipient != null ? !CanEatMeal(recipient, food, pawn)
                : Toddlers.ToddlerUtility.IsToddler(pawn) && !CanEatMeal(pawn, food)) return false;
            bool owned = pawn.carryTracker?.CarriedThing == food || pawn.inventory?.Contains(food) == true;
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried != null && carried != food) return false;
            // As in native FoodUtility, supplied food may be proper for the
            // eater even when prison-room food is improper for the getter.
            // Only a permitted warden supplying an eligible captive gets this
            // recipient path; ordinary/self pickup keeps its original rule.
            if (!owned && (!food.Spawned || food.Map != pawn.Map || food.IsForbidden(pawn)
                || !(food.IsSociallyProper(pawn)
                    || recipient?.IsPrisonerOfColony == true && RatDiningApi.IsEligibleToddler(recipient)
                        && RatToddlerMealCoordinator.CanHelp(pawn, recipient, true)
                        && food.IsSociallyProper(recipient, true, true))
                || pawn.IsPrisonerOfColony && !RatDiningCustody.IsLegalCell(pawn, food.Position)
                || !pawn.CanReach(food, PathEndMode.ClosestTouch, Danger.Some))) return false;
            // Vanilla accounts for portions reserved by other eaters and caregivers.
            count = FoodUtility.GetMaxAmountToPickup(food, pawn, Math.Max(1, wantedCount));
            if (carried != food)
                count = Math.Min(count, pawn.carryTracker?.AvailableStackSpace(food.def) ?? 0);
            return count > 0 && (owned || pawn.CanReserve(food, 10, count, null, false));
        }

        internal static bool IsWaitingJob(JobDef def)
        {
            return def == RDE_DefOf.RDE_ToddlerPrepareDiningRequest || def == RDE_DefOf.RDE_ToddlerWaitForDiningHelp;
        }

        internal static bool IsToddlerJob(JobDef def)
        {
            return IsWaitingJob(def) || def == RDE_DefOf.RDE_ToddlerEatAtChair
                || def == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor || def == RDE_DefOf.RDE_ToddlerWaitForMeal;
        }

        internal static bool IsHelperJob(RatToddlerDiningRequest request, Pawn helper)
        {
            Job current = helper?.CurJob;
            return request != null && (current?.def == RDE_DefOf.RDE_AssistRatToddlerDining
                || current?.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal)
                && current.targetA.Pawn == request.toddler && current.targetB.Thing == request.chair
                && (request.helperJobId < 0 || current.loadID == request.helperJobId);
        }

        internal static bool IsParent(Pawn toddler, Pawn helper)
        {
            return helper != null && toddler?.relations?.GetDirectRelation(PawnRelationDefOf.Parent, helper) != null;
        }

        internal static bool HoldsFood(RatToddlerDiningRequest request)
        {
            return request != null && CanEatMeal(request.toddler, request.food, request.helper)
                && request.toddler?.carryTracker?.CarriedThing == request.food;
        }

        internal static RatToddlerDiningRequest GetBoundRequest(MapComponent_RatToddlerDining manager, Pawn toddler, ref int requestId)
        {
            RatToddlerDiningRequest request = manager?.GetForPawn(toddler) ?? manager?.GetRecoveringForPawn(toddler);
            if (requestId < 0 && request != null) requestId = request.id;
            return request?.id == requestId ? request : null;
        }
    }
}
