using HarmonyLib;
using RimWorld;
using Verse;

namespace RatkinDiningEtiquette
{
    // Completing another meal retires only our previous chair memory. Table
    // positioning and memories owned by RatkinTableManners are left to that mod.
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    internal static class Thing_Ingested_ClearPreviousChairThought
    {
        internal static void Postfix(Thing __instance, Pawn ingester, float __result)
        {
            RatDiningMealSafety.RecordIngestion(ingester, __instance, __result);
            if (__result <= 0f || !RatDiningApi.IsRatkin(ingester) || !ingester.Spawned
                || __instance?.def?.IsNutritionGivingIngestible != true) return;
            if (ingester.CurJobDef == JobDefOf.Ingest || ingester.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor)
                RatDiningThoughts.ClearChair(ingester);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    internal static class Pawn_ExposeData_RetireTableThoughts
    {
        internal static void Postfix(Pawn __instance)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit && RatDiningApi.IsRatkin(__instance))
                RatDiningThoughts.RefreshLoadedMemories(__instance);
        }
    }
}
