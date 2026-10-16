using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Toddlers;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    [HarmonyAfter("cyanobot.toddlers", "lenghuan.ratkintablemanners")]
    internal static class JobGiver_GetFood_Patch
    {
        private static readonly ConditionalWeakTable<Pawn, object> LoggedToddlerEvaluations = new ConditionalWeakTable<Pawn, object>();

        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result?.def != JobDefOf.Ingest || __result.targetA.Thing == null || !RatDiningApi.IsRatkin(pawn)) return;
            MapComponent_RatToddlerDining currentManager = pawn.Map?.GetComponent<MapComponent_RatToddlerDining>();
            RatToddlerDiningRequest currentMeal = currentManager?.GetForPawn(pawn);
            if (pawn.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair && currentMeal?.IsActive == true
                && pawn.CurJob.targetA.Thing == currentMeal.food && pawn.CurJob.targetB.Thing == currentMeal.chair
                && (currentManager.IsFinalizedMeal(currentMeal, pawn.CurJob) || currentManager.CanResumeRequest(currentMeal)))
            {
                // A repeated GetFood proposal must not replace this exact meal
                // with a new Ingest job and restart chewing on the floor.
                JobMaker.ReturnToPool(__result);
                __result = pawn.CurJob;
                return;
            }
            if (RatDiningCustody.CanDine(pawn) && (RatDiningMod.Settings.enableChairTable || RatDiningMod.Settings.enableToddlerHelp))
                pawn.Map?.GetComponent<MapComponent_RatToddlerDining>()?.TrackVanillaMeal(pawn, __result);
            if (pawn.Map?.GetComponent<MapComponent_RatToddlerDining>()?.TryMakeRecoveryJob(pawn, out Job recovery) == true)
            { __result = recovery; return; }
            if (RatDiningFailureGuard.IsCoolingDown(pawn)
                || pawn.Map?.GetComponent<MapComponent_RatToddlerDining>()?.IsCoolingDown(pawn) == true)
            {
                LogToddlerEvaluation(pawn, __result.targetA.Thing, RatDiningMode.FloorFallback, null, IntVec3.Invalid, "cooldown");
                return;
            }
            MapComponent_RatToddlerDining existingManager = pawn.Map?.GetComponent<MapComponent_RatToddlerDining>();
            RatToddlerDiningRequest existing = existingManager?.GetForPawn(pawn);
            if (existing?.IsActive == true)
            {
                // A saved or changed request can be selected before the next
                // map reconciliation. Check custody and cell bounds before
                // CanReserveSittableOrSpot reads the native edifice grid.
                if (!existingManager.CanResumeRequest(existing))
                { existingManager.StartFloorFallback(existing, false); return; }
                if (existing.state == RatToddlerDiningState.MovingFoodToDiningSpot)
                {
                    if (!RatToddlerDiningUtility.TryGetPickupCount(pawn, existing.food, existing.foodCount, out int pickupCount)
                        || !pawn.CanReserveSittableOrSpot(existing.stagingCell))
                    { existingManager.StartFloorFallback(existing, false); return; }
                    existing.foodCount = pickupCount;
                    __result = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerPrepareDiningRequest, existing.food, existing.chair, existing.stagingCell);
                    __result.count = existing.foodCount;
                }
                else if (existing.state == RatToddlerDiningState.WaitingForHelp || existing.state == RatToddlerDiningState.Claimed)
                {
                    __result = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForDiningHelp, existing.food, existing.chair, existing.stagingCell);
                    __result.count = existing.foodCount;
                }
                else if (existing.state == RatToddlerDiningState.CollectingFood)
                    __result = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForMeal, existing.food, existing.chair);
                return;
            }
            Thing food = __result.targetA.Thing;
            if (!RatDiningModeResolver.TryResolve(pawn, food, out RatDiningMode mode, out Building chair, out IntVec3 stand, out Building table)) return;
            LogToddlerEvaluation(pawn, food, mode, chair, stand, "resolved");
            if (mode == RatDiningMode.ChairAsTable)
            {
                Job replacement = JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, food, chair, stand);
                replacement.count = __result.count > 0 ? __result.count : 1;
                RatDiningFailureGuard.NotifyIssued(pawn);
                __result = replacement;
            }
            else if (mode == RatDiningMode.ToddlerNeedsAssistance)
            {
                // Toddlers' captive branch calls GetFood without a priority
                // sorter. Its default maxLevelPercentage=1 can propose food
                // even when satiated. Do not turn that proposal into repeated
                // help requests; retain the ordinary eating/care path.
                if ((pawn.IsPrisonerOfColony || RatDiningCustody.IsVisitor(pawn)) && pawn.needs.food.CurLevelPercentage
                    >= pawn.RaceProps.FoodLevelPercentageWantEat) return;
                if (!RatToddlerDiningUtility.TryGetPickupCount(pawn, food, __result.count, out int pickupCount)
                    || !pawn.CanReserveSittableOrSpot(stand)) return;
                MapComponent_RatToddlerDining manager = pawn.Map.GetComponent<MapComponent_RatToddlerDining>();
                RatToddlerDiningRequest request = manager.Create(pawn, food, chair, table, stand);
                if (request == null) return;
                Job replacement = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerPrepareDiningRequest, food, chair, stand);
                replacement.count = pickupCount;
                request.foodCount = replacement.count;
                __result = replacement;
            }
        }

        private static void LogToddlerEvaluation(Pawn pawn, Thing food, RatDiningMode mode, Building chair, IntVec3 stand, string reason)
        {
            if (!ToddlerUtility.IsToddler(pawn) || LoggedToddlerEvaluations.TryGetValue(pawn, out _)) return;
            LoggedToddlerEvaluations.Add(pawn, new object());
            Log.Message($"[RDE] Toddler dining evaluation: pawn={pawn}, ratkin={RatDiningApi.IsRatkin(pawn)}, canFeedSelf={ToddlerLearningUtility.CanFeedSelf(pawn)}, eatsOnFloor={ToddlerLearningUtility.EatsOnFloor(pawn)}, eligible={RatDiningApi.IsEligibleToddler(pawn)}, foodPct={pawn.needs?.food?.CurLevelPercentage.ToString("0.000") ?? "n/a"}, critical={RatDiningApi.IsCriticalHunger(pawn)}, mode={mode}, chair={chair}, stand={stand}, food={food}, reason={reason}.");
        }
    }

    public sealed class WorkGiver_AssistRatToddlerDining : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool Prioritized => true;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            MapComponent_RatToddlerDining manager = pawn.Map?.GetComponent<MapComponent_RatToddlerDining>();
            if (manager == null) yield break;
            IReadOnlyList<RatToddlerDiningRequest> requests = manager.Requests;
            for (int i = 0; i < requests.Count; i++)
                if (requests[i].IsAvailable && requests[i].toddler != null
                    && !requests[i].toddler.IsPrisonerOfColony) yield return requests[i].toddler;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        { return GetRejectionReason(pawn, t as Pawn) == null; }

        internal string GetRejectionReason(Pawn pawn, Pawn toddler)
        {
            if (toddler?.IsPrisonerOfColony == true) return "prisoner assistance belongs to Warden work";
            return GetSharedRejectionReason(pawn, toddler);
        }

        internal static string GetSharedRejectionReason(Pawn pawn, Pawn toddler)
        {
            RatToddlerDiningRequest request = pawn?.Map?.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(toddler);
            if (request == null) return "no active request";
            if (!request.IsAvailable) return "request unavailable: " + request.state + ", helper=" + request.helper;
            if (!RatDiningApi.IsEligibleToddler(toddler)) return "toddler not eligible";
            if (!RatDiningCustody.ValidRequest(request)) return "custody, faction or room changed";
            if (!RatToddlerDiningUtility.HoldsFood(request)) return "toddler no longer holds this meal";
            if (RatDiningApi.IsCriticalHunger(toddler)) return "critical hunger";
            if (!RatToddlerMealCoordinator.SafeForChairMeal(toddler)) return "medical/rest/temperature/area danger";
            if (pawn.Position.DistanceToSquared(toddler.Position) > RatDiningMod.Settings.helperSearchRadius * RatDiningMod.Settings.helperSearchRadius)
                return "helper outside search radius";
            if (!RatDiningMod.Settings.allowNonParents && !RatToddlerDiningUtility.IsParent(toddler, pawn)) return "non-parent helpers disabled";
            if (!RatToddlerMealCoordinator.CanHelp(pawn, toddler, true)) return "helper incapable, unavailable or required care work disabled";
            if (!RatDiningSpotFinder.IsUsableChair(toddler, request.chair)) return "chair forbidden, unsafe, unreachable or reserved";
            if (!pawn.CanReserve(toddler)) return "toddler reserved by another job";
            if (!pawn.CanReach(toddler, PathEndMode.Touch, Danger.Some)) return "helper cannot reach toddler";
            if (!pawn.CanReserve(request.chair)) return "chair reserved by another job";
            if (!pawn.CanReach(request.chair, PathEndMode.Touch, Danger.Some)) return "helper cannot reach chair";
            return null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            RatToddlerDiningRequest request = pawn.Map.GetComponent<MapComponent_RatToddlerDining>().GetForPawn(t as Pawn);
            if (request == null || !HasJobOnThing(pawn, t, forced)) return null;
            Job job = JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, request.toddler, request.chair);
            job.count = 1;
            return job;
        }

        public override float GetPriority(Pawn pawn, TargetInfo t)
        {
            Pawn toddler = t.Thing as Pawn;
            if (toddler == null) return 0f;
            float hunger = toddler.needs?.food == null ? 0f : (1f - toddler.needs.food.CurLevelPercentage) * 100f;
            float distance = pawn.Position.DistanceTo(toddler.Position) * 0.1f;
            float parentTieBreak = RatToddlerDiningUtility.IsParent(toddler, pawn) ? 5f : 0f;
            return hunger + parentTieBreak - distance;
        }
    }

    public sealed class FloatMenuOptionProvider_AssistRatToddlerDining : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        public override bool TargetPawnValid(Pawn pawn, FloatMenuContext context)
        { return RatDiningApi.IsEligibleToddler(pawn) && RatDiningApi.IsDiningRequestActive(pawn); }

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn helper = context.FirstSelectedPawn;
            if (helper == null) return null;
            WorkGiver_Scanner giver = clickedPawn.IsPrisonerOfColony
                ? (WorkGiver_Scanner)new WorkGiver_Warden_AssistRatToddlerDining()
                : new WorkGiver_AssistRatToddlerDining();
            if (!giver.HasJobOnThing(helper, clickedPawn, true)) return new FloatMenuOption("RDE_ManualAssist".Translate(), null);
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("RDE_ManualAssist".Translate(), delegate
            {
                TargetingParameters parameters = new TargetingParameters
                {
                    canTargetBuildings = true,
                    validator = target => target.Thing is Building building && RatDiningSpotFinder.IsUsableChair(clickedPawn, building)
                };
                Find.Targeter.BeginTargeting(parameters, target =>
                {
                    Building chair = target.Thing as Building;
                    MapComponent_RatToddlerDining manager = clickedPawn.Map?.GetComponent<MapComponent_RatToddlerDining>();
                    RatToddlerDiningRequest request = manager?.GetForPawn(clickedPawn);
                    if (request?.IsAvailable != true || chair == null || !giver.HasJobOnThing(helper, clickedPawn, true)
                        || !helper.CanReserveAndReach(chair, PathEndMode.Touch, Danger.Some)
                        || !RatDiningSpotFinder.TryFindAdjacentStandCell(clickedPawn, chair, out IntVec3 staging)
                        || !manager.TryReassignChair(request, chair, staging)) return;
                    Job job = JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, request.toddler, request.chair);
                    job.count = 1;
                    helper.jobs.TryTakeOrderedJob(job);
                }, null);
            }), helper, clickedPawn);
        }
    }

    public sealed class WorkGiver_Warden_AssistRatToddlerDining : WorkGiver_Warden
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool Prioritized => true;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null) yield break;
            foreach (Pawn child in pawn.Map.mapPawns.SlavesAndPrisonersOfColonySpawned)
                if (child.IsPrisonerOfColony && RatDiningApi.IsEligibleToddler(child)) yield return child;
        }

        internal string GetRejectionReason(Pawn pawn, Pawn child, bool forced = false)
        {
            if (child?.IsPrisonerOfColony != true) return "not a colony prisoner";
            if (!ShouldTakeCareOfPrisoner(pawn, child, forced)) return "prisoner unsecured, forbidden or unreachable";
            RatToddlerDiningRequest request = pawn.Map.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(child);
            if (request?.IsActive == true)
                return WorkGiver_AssistRatToddlerDining.GetSharedRejectionReason(pawn, child);
            return RatToddlerMealCoordinator.TryPlanPrisonerMeal(pawn, child, out _, out _, out _, out _)
                ? null : "no safe, permitted prisoner chair meal";
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        { return GetRejectionReason(pawn, t as Pawn, forced) == null; }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn child = t as Pawn;
            if (!HasJobOnThing(pawn, child, forced)) return null;
            RatToddlerDiningRequest request = pawn.Map.GetComponent<MapComponent_RatToddlerDining>().GetForPawn(child);
            if (request?.IsAvailable == true)
            {
                Job assist = JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, child, request.chair);
                assist.count = 1;
                return assist;
            }
            if (!RatToddlerMealCoordinator.TryPlanPrisonerMeal(pawn, child, out Thing food,
                out Building chair, out _, out int count)) return null;
            Job provide = JobMaker.MakeJob(RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, child, chair, food);
            provide.count = count;
            return provide;
        }

        public override float GetPriority(Pawn pawn, TargetInfo target)
        { return new WorkGiver_AssistRatToddlerDining().GetPriority(pawn, target); }
    }
}
