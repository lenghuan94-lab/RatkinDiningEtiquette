using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    internal static class RatDiningMealSafety
    {
        [ThreadStatic] private static MemoryThoughtHandler chairMemories;
        [ThreadStatic] private static IngestionObservation observingMeal;

        internal sealed class IngestionObservation : IDisposable
        {
            private readonly IngestionObservation previous = observingMeal;
            internal readonly Pawn pawn;
            internal readonly Thing food;
            internal readonly Job job;
            internal bool ate;
            internal IngestionObservation(Pawn pawn, Thing food, Job job)
            { this.pawn = pawn; this.food = food; this.job = job; observingMeal = this; }
            public void Dispose() { observingMeal = previous; }
        }

        internal static IngestionObservation ObserveIngestion(Pawn pawn, Thing food, Job job)
            => new IngestionObservation(pawn, food, job);

        internal static void RecordIngestion(Pawn pawn, Thing food, float nutrition)
        {
            IngestionObservation meal = observingMeal;
            if (meal != null && meal.pawn == pawn && meal.food == food && pawn?.CurJob == meal.job
                && nutrition > 0f && !float.IsNaN(nutrition) && !float.IsInfinity(nutrition)) meal.ate = true;
        }

        private sealed class ChairScope : IDisposable
        {
            private readonly MemoryThoughtHandler previous = chairMemories;
            internal ChairScope(Pawn pawn) { chairMemories = pawn.needs?.mood?.thoughts?.memories; }
            public void Dispose() { chairMemories = previous; }
        }

        internal static IDisposable FinalizeChair(Pawn pawn) => new ChairScope(pawn);
        internal static bool SuppressTableMemory(MemoryThoughtHandler handler, ThoughtDef def)
            => handler != null && handler == chairMemories && def == ThoughtDefOf.AteWithoutTable;

        internal static void EnsureFacingInBounds(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Map == null || !pawn.Position.InBounds(pawn.Map)
                || (pawn.Position + pawn.Rotation.FacingCell).InBounds(pawn.Map)) return;
            int first = -1;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 cell = pawn.Position + new Rot4(i).FacingCell;
                if (!cell.InBounds(pawn.Map)) continue;
                if (first < 0) first = i;
                if (!cell.HasEatSurface(pawn.Map)) continue;
                pawn.Rotation = new Rot4(i);
                return;
            }
            if (first >= 0) pawn.Rotation = new Rot4(first);
        }
    }

    // Only the explicitly scoped custom chair finalizer suppresses this new
    // vanilla memory. Existing memories and all other thought defs stay intact.
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) })]
    internal static class MemoryThoughtHandler_ChairMealTableThought
    {
        private static bool Prefix(MemoryThoughtHandler __instance, ThoughtDef def)
            => !RatDiningMealSafety.SuppressTableMemory(__instance, def);
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.FinalizeIngest))]
    [HarmonyAfter("cyanobot.toddlers", "lenghuan.ratkintablemanners")]
    internal static class Toils_Ingest_ProtectOwnedDiningFinalization
    {
        private static void Postfix(Pawn ingester, ref Toil __result)
        {
            Action original = __result?.initAction;
            if (original == null) return;
            __result.initAction = delegate
            {
                if (RatDiningSpotSafety.TryGetOwnedMeal(ingester, out _))
                    RatDiningMealSafety.EnsureFacingInBounds(ingester);
                original();
            };
        }
    }
}
