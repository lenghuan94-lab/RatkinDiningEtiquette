using RimWorld;
using Verse;

namespace RatkinDiningEtiquette
{
    public sealed class RatDiningRaceExtension : DefModExtension { }

    [DefOf]
#pragma warning disable 0649
    internal static class RDE_DefOf
    {
        public static JobDef RDE_EatUsingChairAsTable;
        public static JobDef RDE_ToddlerPrepareDiningRequest;
        public static JobDef RDE_ToddlerWaitForDiningHelp;
        public static JobDef RDE_AssistRatToddlerDining;
        public static JobDef RDE_ToddlerEatAtChair;
        public static JobDef RDE_ToddlerEatHeldFoodOnFloor;
        public static JobDef RDE_ProvideRatToddlerDiningMeal;
        public static JobDef RDE_ToddlerWaitForMeal;
        public static JobDef RDE_ReturnDiningChild;
        public static ThoughtDef RDE_UsedChairAsTable;
        public static ThoughtDef RDE_ToddlerNoDiningHelp;
        public static ThoughtDef RDE_RatkinChairAsTable;
        public static ThoughtDef RDE_ToiletAsTable;
        public static ThoughtDef RDE_SpecialSeatAsTable;

        static RDE_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RDE_DefOf));
    }
#pragma warning restore 0649
}
