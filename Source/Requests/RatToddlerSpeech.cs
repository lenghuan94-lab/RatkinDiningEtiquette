using RimWorld;
using Verse;

namespace RatkinDiningEtiquette
{
    internal static class RatToddlerSpeech
    {
        internal static string RequestKey(Pawn toddler)
        {
            // Use the same Talking capacity shown on the health tab. Age and
            // Toddlers' manipulation/walking learning do not determine speech.
            float talking = toddler?.health?.capacities?.GetLevel(PawnCapacityDefOf.Talking) ?? 0f;
            if (float.IsNaN(talking) || float.IsInfinity(talking)) talking = 0f;
            return talking < .4f ? "RDE_RequestMote_Low"
                : talking < .8f ? "RDE_RequestMote_Medium" : "RDE_RequestMote_High";
        }

        internal static string RequestText(Pawn toddler) => RequestKey(toddler).Translate().ToString();

        internal static void ShowRequest(Pawn toddler)
        {
            if (RatDiningMod.Settings.showRequestMote && RatDiningApi.IsEligibleToddler(toddler))
                MoteMaker.ThrowText(toddler.DrawPos, toddler.Map, RequestText(toddler), 1.9f);
        }
    }
}
