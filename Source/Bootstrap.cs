using HarmonyLib;
using Verse;

namespace RatkinDiningEtiquette
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            new Harmony("lenghuan.ratkindiningetiquette").PatchAll();
            Log.Message("[This Is Your Table!!] 1.2.1 initialized.");
        }
    }
}
