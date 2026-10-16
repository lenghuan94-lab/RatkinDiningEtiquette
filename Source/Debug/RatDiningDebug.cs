using System.Text;
using LudeonTK;
using RimWorld;
using Toddlers;
using Verse;

namespace RatkinDiningEtiquette
{
    internal static class RatDiningDebug
    {
        [DebugAction("This Is Your Table!!", "Log requests on current map", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogRequests()
        {
            MapComponent_RatToddlerDining manager = Find.CurrentMap?.GetComponent<MapComponent_RatToddlerDining>();
            StringBuilder text = new StringBuilder("[RDE] Requests:");
            if (manager != null)
                foreach (RatToddlerDiningRequest request in manager.Requests)
                    text.AppendLine().Append('#').Append(request.id).Append(' ').Append(request.state)
                        .Append(" toddler=").Append(request.toddler).Append(" helper=").Append(request.helper)
                        .Append(" chair=").Append(request.chair).Append(" food=").Append(request.food)
                        .Append(" foodCount=").Append(request.foodCount).Append(" retries=").Append(request.retryCount)
                        .Append(" helperJobId=").Append(request.helperJobId)
                        .Append(" stateAge=").Append(Find.TickManager.TicksGame - request.stateTick)
                        .Append(" waitAge=").Append(request.waitStartedTick < 0 ? -1 : Find.TickManager.TicksGame - request.waitStartedTick)
                        .Append(" recoveryPending=").Append(request.recoveryPending)
                        .Append(" prisoner=").Append(request.custody.prisoner)
                        .Append(" origin=").Append(request.custody.originCell)
                        .Append(" returnAttempts=").Append(request.returnAttempts)
                        .Append(" recoveryAge=").Append(request.recoveryStartedTick < 0 ? -1 : Find.TickManager.TicksGame - request.recoveryStartedTick)
                        .Append(" careHandoff=").Append(request.careHandoff)
                        .Append(" resumeWaitingPending=").Append(request.resumeWaitingPending);
            Log.Message(text.ToString());
        }

        [DebugAction("This Is Your Table!!", "Clear selected toddler request", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearRequest(Pawn pawn)
        { pawn?.MapHeld?.GetComponent<MapComponent_RatToddlerDining>()?.DebugCancel(pawn); }

        [DebugAction("This Is Your Table!!", "Force selected toddler request timeout", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TimeoutRequest(Pawn pawn)
        { pawn?.MapHeld?.GetComponent<MapComponent_RatToddlerDining>()?.DebugTimeout(pawn); }

        [DebugAction("This Is Your Table!!", "Inspect selected pawn dining eligibility", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InspectEligibility(Pawn pawn)
        {
            if (pawn == null) return;
            bool found = RatDiningSpotFinder.TryFindChairAsTable(pawn, out Building chair, out IntVec3 stand, out Building table);
            Log.Message($"[RDE] Eligibility {pawn}: ratkin={RatDiningApi.IsRatkin(pawn)}, toddler={ToddlerUtility.IsToddler(pawn)}, liveToddler={ToddlerUtility.IsLiveToddler(pawn)}, canFeedSelf={ToddlerLearningUtility.CanFeedSelf(pawn)}, eatsOnFloor={ToddlerLearningUtility.EatsOnFloor(pawn)}, eligible={RatDiningApi.IsEligibleToddler(pawn)}, foodPct={pawn.needs?.food?.CurLevelPercentage.ToString("0.000") ?? "n/a"}, chairFound={found}, chair={chair}, stand={stand}, table={table}.");
            MapComponent_RatToddlerDining manager = pawn.MapHeld?.GetComponent<MapComponent_RatToddlerDining>();
            Log.Message($"[RDE] Request status: job={pawn.CurJobDef}, state={manager?.GetForPawn(pawn)?.state.ToString() ?? "none"}, cooldown={manager?.IsCoolingDown(pawn) == true}.");
            Thing meal = pawn.carryTracker?.CarriedThing;
            if (meal?.def.IsNutritionGivingIngestible != true && pawn.Map != null)
            {
                meal = null;
                float closest = float.MaxValue;
                foreach (Thing candidate in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource))
                {
                    float distance = pawn.Position.DistanceToSquared(candidate.Position);
                    if (!candidate.def.IsNutritionGivingIngestible || distance >= closest) continue;
                    meal = candidate; closest = distance;
                }
            }
            bool resolved = RatDiningModeResolver.TryResolve(pawn, meal, out RatDiningMode mode, out _, out _, out _);
            Log.Message($"[RDE] Mode diagnosis: canDine={RatDiningCustody.CanDine(pawn)}, prisoner={pawn.IsPrisonerOfColony}, secure={pawn.guest?.PrisonerIsSecure}, food={meal}, resolved={resolved}, mode={mode}, failureCooldown={RatDiningFailureGuard.IsCoolingDown(pawn)}.");
            if (pawn.relations == null) return;
            foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
            {
                if (relation.def != PawnRelationDefOf.Parent) continue;
                Pawn parent = relation.otherPawn;
                WorkTypeDef careWork = pawn.IsPrisonerOfColony ? WorkTypeDefOf.Warden : WorkTypeDefOf.Childcare;
                string reason = RejectionReason(parent, pawn);
                bool canAssist = parent.Spawned && parent.Map == pawn.MapHeld && reason == null;
                Log.Message($"[RDE] Parent {parent}: requiredWork={careWork}, assigned={parent.workSettings?.WorkIsActive(careWork) == true}, drafted={parent.Drafted}, downed={parent.Downed}, job={parent.CurJobDef}, canAssist={canAssist}, reason={reason ?? "eligible; awaiting work scheduling"}.");
            }
        }

        [DebugAction("This Is Your Table!!", "Inspect selected helper dining jobs", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InspectHelper(Pawn helper)
        {
            MapComponent_RatToddlerDining manager = helper?.Map?.GetComponent<MapComponent_RatToddlerDining>();
            if (manager == null) return;
            Log.Message($"[RDE] Helper {helper}: childcare={helper.workSettings?.WorkIsActive(WorkTypeDefOf.Childcare) == true}, warden={helper.workSettings?.WorkIsActive(WorkTypeDefOf.Warden) == true}, job={helper.CurJobDef}.");
            foreach (RatToddlerDiningRequest request in manager.Requests)
                Log.Message($"[RDE] Helper {helper} -> request #{request.id}, toddler={request.toddler}: {RejectionReason(helper, request.toddler) ?? "eligible; awaiting work scheduling"}.");
            Log.Message($"[RDE] Native work indices: Warden={WorkTypeDefOf.Warden.index}, Childcare={WorkTypeDefOf.Childcare.index}.");
            // A one-action pawn-lister diagnostic, never a tick-time scan.
            foreach (Pawn child in helper.Map.mapPawns.SlavesAndPrisonersOfColonySpawned)
            {
                if (!child.IsPrisonerOfColony || !RatDiningApi.IsEligibleToddler(child)
                    || manager.GetForPawn(child)?.IsActive == true) continue;
                Thing food = ChildcareUtility.FindBabyFoodForBaby(helper, child);
                bool ownPickup = RatToddlerDiningUtility.TryGetPickupCount(helper, food, 1, out _);
                bool recipientPickup = RatToddlerDiningUtility.TryGetPickupCount(helper, food, 1, out _, child);
                Log.Message($"[RDE] Fresh captive meal {helper} -> {child}: safe={RatToddlerMealCoordinator.SafeForChairMeal(child)}, hungry={FeedPatientUtility.IsHungry(child)}, canHelp={RatToddlerMealCoordinator.CanHelp(helper, child, true)}, food={food}, getterProper={food?.IsSociallyProper(helper)}, eaterProper={food?.IsSociallyProper(child, true, true)}, ownPickup={ownPickup}, recipientPickup={recipientPickup}, rejection={RejectionReason(helper, child) ?? "eligible; awaiting work scheduling"}.");
            }
        }

        private static string RejectionReason(Pawn helper, Pawn child)
            => child?.IsPrisonerOfColony == true
                ? new WorkGiver_Warden_AssistRatToddlerDining().GetRejectionReason(helper, child)
                : new WorkGiver_AssistRatToddlerDining().GetRejectionReason(helper, child);
    }
}
