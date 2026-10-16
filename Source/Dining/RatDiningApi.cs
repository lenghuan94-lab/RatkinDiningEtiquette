using RimWorld;
using Toddlers;
using Verse;

namespace RatkinDiningEtiquette
{
    public enum RatDiningMode
    {
        NormalDining,
        StandAtTable,
        ChairAsTable,
        ToddlerNeedsAssistance,
        ToddlerEatAtTable, // Retained API value for old integrations; never selected.
        ToddlerEatOnChair,
        FloorFallback
    }

    public static class RatDiningApi
    {
        private static ThingDef ratkinDef;

        public static bool IsRatkin(Pawn pawn)
        {
            if (pawn?.def == null) return false;
            if (ratkinDef == null) ratkinDef = DefDatabase<ThingDef>.GetNamedSilentFail("Ratkin");
            return pawn.def == ratkinDef || pawn.def.HasModExtension<RatDiningRaceExtension>();
        }

        public static bool IsEligibleToddler(Pawn pawn)
        {
            return CanParticipateInToddlerDining(pawn) && pawn.Spawned;
        }

        internal static bool CanParticipateInToddlerDining(Pawn pawn)
        {
            return CanSafelySelfFeed(pawn) && RatDiningCustody.CanRequestHelp(pawn);
        }

        internal static bool CanSafelySelfFeed(Pawn pawn)
        {
            return IsRatkin(pawn) && ToddlerUtility.IsToddler(pawn) && ToddlerLearningUtility.CanFeedSelf(pawn)
                && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && !pawn.Drafted
                && pawn.needs?.food != null;
        }

        public static bool TryResolveMode(Pawn pawn, Thing food, out RatDiningMode mode)
        {
            return RatDiningModeResolver.TryResolve(pawn, food, out mode, out _, out _, out _);
        }

        public static bool IsDiningRequestActive(Pawn pawn)
        {
            return pawn?.MapHeld?.GetComponent<MapComponent_RatToddlerDining>()?.GetForPawn(pawn)?.IsActive == true;
        }

        internal static bool IsCriticalHunger(Pawn pawn)
        {
            return pawn?.needs?.food != null && pawn.needs.food.CurLevelPercentage <= RatDiningMod.Settings.criticalFoodLevel;
        }

        internal static void Debug(string message)
        {
            if (RatDiningMod.Settings?.debugLogging == true) Log.Message("[RDE] " + message);
        }
    }
}
