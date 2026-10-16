using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    // This exception is emitted only by the scoped result check below. Never
    // swallow IndexOutOfRangeException or another mod's unexpected exceptions.
    internal sealed class RatDiningNoChewSpotException : Exception { }

    internal static class RatDiningSpotSafety
    {
        [ThreadStatic] private static Pawn selectingPawn;
        [ThreadStatic] private static IntVec3 selectingOrigin;

        private sealed class Scope : IDisposable
        {
            private readonly Pawn previousPawn = selectingPawn;
            private readonly IntVec3 previousOrigin = selectingOrigin;
            internal Scope(Pawn pawn, IntVec3 origin) { selectingPawn = pawn; selectingOrigin = origin; }
            public void Dispose() { selectingPawn = previousPawn; selectingOrigin = previousOrigin; }
        }

        internal static IDisposable SelectFor(Pawn pawn, IntVec3 origin) => new Scope(pawn, origin);
        internal static bool IsSelecting(Pawn pawn) => pawn != null && selectingPawn == pawn;
        internal static bool LegalResult(Pawn pawn, IntVec3 cell)
            => RatDiningCustody.IsLegalCell(pawn, cell, selectingOrigin);

        internal static bool CanProtectPrisonerSelection(Pawn pawn, Thing food)
        {
            return pawn?.Spawned == true && pawn.IsPrisonerOfColony && RatDiningApi.IsRatkin(pawn)
                && pawn.guest?.Released != true && RatDiningCustody.CanDine(pawn)
                && food?.def?.IsNutritionGivingIngestible == true
                && (RatDiningMod.Settings.enableChairTable || RatDiningMod.Settings.enableToddlerHelp);
        }

        internal static bool TryCurrentPrisonerSpot(Pawn pawn, Thing food, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (!CanProtectPrisonerSelection(pawn, food) || food.Destroyed || !food.IngestibleNow
                || Toddlers.ToddlerUtility.IsToddler(pawn)
                    && (!RatDiningApi.CanSafelySelfFeed(pawn) || !RatToddlerDiningUtility.CanEatMeal(pawn, food))) return false;
            IntVec3 current = pawn.Position;
            Map map = pawn.Map;
            if (!LegalResult(pawn, current) || !current.Standable(map) || current.IsForbidden(pawn)
                || !current.InAllowedArea(pawn) || current.GetDangerFor(pawn, map) != Danger.None
                || !GenTemperature.SafeTemperatureAtCell(pawn, current, map)
                || !pawn.CanReserveSittableOrSpot(current)) return false;
            // Native CarryIngestibleToChewSpot already falls back to chewing
            // at the current cell. Allow its earlier AI preflight to reach that
            // same safe result without requiring a separate food-placement cell.
            cell = current;
            return true;
        }

        internal static bool TryGetOwnedMeal(Pawn pawn, out RatDiningVanillaMeal meal)
        {
            meal = pawn?.MapHeld?.GetComponent<MapComponent_RatToddlerDining>()?.GetVanillaMeal(pawn);
            return meal != null && pawn.Spawned && pawn.CurJobDef == JobDefOf.Ingest
                && RatDiningApi.IsRatkin(pawn) && meal.jobId == pawn.CurJob.loadID;
        }

        internal static void ContinueHeldMeal(Pawn pawn, TargetIndex foodIndex, RatDiningVanillaMeal meal)
        {
            Thing food = pawn.CurJob?.GetTarget(foodIndex).Thing;
            if (meal.custody.Matches(pawn) && RatDiningCustody.IsLegalCell(pawn, pawn.Position, meal.custody.originCell)
                && RatDiningCustody.CanDine(pawn)
                && (!Toddlers.ToddlerUtility.IsToddler(pawn)
                    || RatDiningApi.CanSafelySelfFeed(pawn) && RatToddlerDiningUtility.CanEatMeal(pawn, food))
                && pawn.carryTracker?.CarriedThing == food
                && food?.IngestibleNow == true && food.def.IsNutritionGivingIngestible)
            {
                // Starting a path to the current cell is the vanilla no-spot
                // fallback. It advances the same job through normal chewing and
                // finalization, without nested job creation or a new food target.
                pawn.pather.StartPath(pawn.Position, PathEndMode.OnCell);
                return;
            }
            pawn.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.TryFindChairOrSpot))]
    [HarmonyAfter("cyanobot.toddlers", "lenghuan.ratkintablemanners")]
    internal static class Toils_Ingest_ProtectPrisonerMealSelection
    {
        private static void Prefix(Pawn pawn, Thing ingestible, out IDisposable __state)
        {
            __state = null;
            // GetFood invokes the selector before creating an Ingest job, so
            // there is no indexed meal yet. Preserve any more specific outer
            // request/custody scope instead of replacing its origin contract.
            if (!RatDiningSpotSafety.IsSelecting(pawn)
                && RatDiningSpotSafety.CanProtectPrisonerSelection(pawn, ingestible))
                __state = RatDiningSpotSafety.SelectFor(pawn, pawn.Position);
        }

        private static Exception Finalizer(Pawn pawn, Thing ingestible, IDisposable __state,
            Exception __exception, ref bool __result, ref IntVec3 cell)
        {
            try
            {
                if (__state == null || !(__exception is RatDiningNoChewSpotException)) return __exception;
                __result = RatDiningSpotSafety.TryCurrentPrisonerSpot(pawn, ingestible, out cell);
                return null; // Only our scoped no-result signal is handled.
            }
            finally { __state?.Dispose(); }
        }
    }

    [HarmonyPatch(typeof(RCellFinder), nameof(RCellFinder.SpotToChewStandingNear))]
    internal static class RCellFinder_ScopedDiningResult
    {
        private static void Postfix(Pawn pawn, IntVec3 __result)
        {
            if (RatDiningSpotSafety.IsSelecting(pawn) && !RatDiningSpotSafety.LegalResult(pawn, __result))
                throw new RatDiningNoChewSpotException();
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.CarryIngestibleToChewSpot))]
    [HarmonyAfter("cyanobot.toddlers", "lenghuan.ratkintablemanners")]
    internal static class Toils_Ingest_ProtectOwnedDiningSpot
    {
        private static void Postfix(TargetIndex ingestibleInd, ref Toil __result)
        {
            Toil toil = __result;
            Action original = toil?.initAction;
            if (original == null) return;
            toil.initAction = delegate
            {
                Pawn pawn = toil.actor;
                if (!RatDiningSpotSafety.TryGetOwnedMeal(pawn, out RatDiningVanillaMeal meal))
                { original(); return; }
                using (RatDiningSpotSafety.SelectFor(pawn, meal.custody.originCell))
                {
                    try { original(); }
                    catch (RatDiningNoChewSpotException) { RatDiningSpotSafety.ContinueHeldMeal(pawn, ingestibleInd, meal); }
                }
            };
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.FindAdjacentEatSurface))]
    [HarmonyAfter("cyanobot.toddlers", "lenghuan.ratkintablemanners")]
    internal static class Toils_Ingest_ProtectOwnedDiningMapEdge
    {
        private static void Postfix(TargetIndex eatSurfaceInd, TargetIndex foodInd, ref Toil __result)
        {
            Toil toil = __result;
            Action original = toil?.initAction;
            if (original == null) return;
            toil.initAction = delegate
            {
                Pawn pawn = toil.actor;
                if (!RatDiningSpotSafety.TryGetOwnedMeal(pawn, out _)) { original(); return; }
                IntVec3 position = pawn.Position;
                bool atEdge = false;
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                    if (!(position + GenAdj.CardinalDirections[i]).InBounds(pawn.Map)) { atEdge = true; break; }
                if (!atEdge) { original(); return; }
                // Preserve vanilla's rotation/order; omit only off-map probes.
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 cell = position + new Rot4(i).FacingCell;
                    if (!cell.InBounds(pawn.Map) || !cell.HasEatSurface(pawn.Map)) continue;
                    pawn.CurJob.SetTarget(eatSurfaceInd, cell);
                    pawn.jobs.curDriver.rotateToFace = eatSurfaceInd;
                    Thing food = pawn.CurJob.GetTarget(foodInd).Thing;
                    if (food?.def?.rotatable == true) food.Rotation = Rot4.FromIntVec3(cell - position);
                    break;
                }
            };
        }
    }
}
