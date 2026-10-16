using RimWorld;
using Verse;

namespace RatkinDiningEtiquette
{
    internal static class RatDiningModeResolver
    {
        internal static bool TryResolve(Pawn pawn, Thing food, out RatDiningMode mode, out Building chair, out IntVec3 standCell, out Building table)
        {
            mode = RatDiningMode.FloorFallback;
            chair = null; table = null; standCell = IntVec3.Invalid;
            if (!RatDiningApi.IsRatkin(pawn) || food?.def?.IsNutritionGivingIngestible != true || pawn.Map == null
                || !RatDiningCustody.CanDine(pawn)) return false;
            if (Toddlers.ToddlerUtility.IsToddler(pawn) && !RatToddlerDiningUtility.CanEatMeal(pawn, food)) return false;

            if (Toddlers.ToddlerUtility.IsToddler(pawn)
                && (!RatDiningMod.Settings.enableToddlerHelp || !RatDiningApi.IsEligibleToddler(pawn)
                    || !RatToddlerMealCoordinator.SafeForChairMeal(pawn))) return true;

            // Preserve table meals selected by vanilla or the separate table mod.
            bool ordinary;
            IntVec3 ordinaryCell = IntVec3.Invalid;
            using (RatDiningSpotSafety.SelectFor(pawn, pawn.Position))
            {
                try { ordinary = Toils_Ingest.TryFindChairOrSpot(pawn, food, out ordinaryCell); }
                catch (RatDiningNoChewSpotException) { ordinary = false; }
            }
            if (ordinary && RatDiningCustody.IsLegalCell(pawn, ordinaryCell) && IsDiningCell(pawn, ordinaryCell))
            {
                mode = ordinaryCell.GetEdifice(pawn.Map)?.def?.building?.isSittable == true
                    ? RatDiningMode.NormalDining : RatDiningMode.StandAtTable;
                return true;
            }

            if (RatDiningApi.IsEligibleToddler(pawn) && RatDiningMod.Settings.enableToddlerHelp)
            {
                if (!food.IngestibleNow || food is Building || food is Corpse
                    || pawn.Map.GetComponent<MapComponent_RatToddlerDining>()?.IsCoolingDown(pawn) == true
                    || pawn.Map.GetComponent<MapComponent_RatToddlerDining>()?.IsRecovering(pawn) == true) return true;
                if (RatDiningApi.IsCriticalHunger(pawn)) { mode = RatDiningMode.FloorFallback; return true; }
                if (RatDiningSpotFinder.TryFindChairAsTable(pawn, out chair, out standCell, out table))
                { mode = RatDiningMode.ToddlerNeedsAssistance; return true; }
                return true;
            }

            if (!Toddlers.ToddlerUtility.IsToddler(pawn) && RatDiningMod.Settings.enableChairTable
                && RatDiningSpotFinder.TryFindChairAsTable(pawn, out chair, out standCell, out table))
            { mode = RatDiningMode.ChairAsTable; return true; }
            return true;
        }

        private static bool IsDiningCell(Pawn pawn, IntVec3 cell)
        {
            if (!cell.IsValid || !cell.InBounds(pawn.Map)) return false;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 neighbor = cell + GenAdj.CardinalDirections[i];
                if (neighbor.InBounds(pawn.Map) && neighbor.HasEatSurface(pawn.Map)) return true;
            }
            return false;
        }
    }
}
