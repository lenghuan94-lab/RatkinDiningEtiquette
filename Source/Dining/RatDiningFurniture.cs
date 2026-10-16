using System;
using System.Collections.Generic;
using Verse;

namespace RatkinDiningEtiquette
{
    public enum RatDiningSeatKind { Chair, Toilet, SpecialSeat }

    public sealed class RatDiningFurnitureExtension : DefModExtension
    {
        public bool ratkinScale = true;
        public RatDiningSeatKind diningKind = RatDiningSeatKind.Chair;
    }

    internal static class RatDiningFurniture
    {
        private static readonly HashSet<string> HygieneToilets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "PitLatrine", "ToiletStuff", "ToiletAdv", "ToiletAdvStuff", "ToiletSpacer" };

        // No dependency on hygiene assemblies. Inspect the inheritance chain so
        // reskins and derived toilets retain the same classification.
        internal static bool IsKnownToilet(Building seat)
        {
            for (Type type = seat?.def?.thingClass ?? seat?.GetType(); type != null; type = type.BaseType)
                if (type.FullName == "DubsBadHygiene.Building_toilet"
                    || type.FullName == "DubsBadHygiene.Building_Latrine"
                    || type.FullName == "DubsBadHygiene.Building_AdvToilet"
                    || type.FullName == "DubsBadHygiene.Building_SpacerToilet") return true;
            string package = seat?.def?.modContentPack?.PackageIdPlayerFacing;
            return (string.Equals(package, "Dubwise.DubsBadHygiene", StringComparison.OrdinalIgnoreCase)
                || string.Equals(package, "Dubwise.DubsBadHygieneLite", StringComparison.OrdinalIgnoreCase))
                && HygieneToilets.Contains(seat.def.defName ?? "");
        }

        internal static bool IsDiningSeat(Building seat)
            => seat?.def?.building != null && (seat.def.building.isSittable || IsKnownToilet(seat));

        internal static RatDiningSeatKind Kind(Building seat)
        {
            if (IsKnownToilet(seat)) return RatDiningSeatKind.Toilet;
            if (!IsDiningSeat(seat)) return RatDiningSeatKind.Chair;
            string name = seat.def.defName ?? "";
            if (Contains(name, "toilet", "latrine", "potty")) return RatDiningSeatKind.Toilet;
            RatDiningFurnitureExtension extension = seat.def.GetModExtension<RatDiningFurnitureExtension>();
            if (extension != null) return extension.diningKind;
            for (Type type = seat.def.thingClass ?? seat.GetType(); type != null; type = type.BaseType)
                if (type.Namespace == "DubsBadHygiene" || Contains(type.Name, "throne", "interrogation", "torture"))
                    return RatDiningSeatKind.SpecialSeat;
            return Contains(name, "throne", "interrogation", "torture", "bath", "shower", "washing", "washbasin")
                ? RatDiningSeatKind.SpecialSeat : RatDiningSeatKind.Chair;
        }

        private static bool Contains(string value, params string[] terms)
        {
            foreach (string term in terms)
                if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        internal static bool AllowedFor(Pawn pawn, Building seat)
        {
            if (pawn == null || !IsDiningSeat(seat)) return false;
            if (Kind(seat) == RatDiningSeatKind.Chair) return true;
            RatDiningSettings settings = RatDiningMod.Settings;
            if (settings == null) return false;
            if (pawn.IsSlaveOfColony) return settings.enableSlaveSpecialSeats;
            if (pawn.IsPrisonerOfColony) return settings.enablePrisonerSpecialSeats;
            if (RatDiningCustody.IsVisitor(pawn)) return settings.enableVisitorSpecialSeats;
            return pawn.IsColonist && settings.enableColonistSpecialSeats;
        }

        private static readonly HashSet<string> GloomyPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Solaris.FurnitureBase", "Gloomy.Furniture.Fix", "Gloomy.Furniture.Continued", "Gloomy.Furniture.Continued.Fix" };
        private static readonly HashSet<string> RatkinPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "fxz.ratkinlifestyle.update", "OARK.RatkinKnightOrderFurniture", "OARK.RatkinKnightOrderFurniture.OldOdyssey", "EoralMilk.RatkinMoustate" };
        private static readonly HashSet<string> GloomySeats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "RGK_SimpleChair", "RGK_Chair", "RGKDiningChair" };

        internal static bool IsRatkinScale(Building chair)
        {
            ThingDef def = chair?.def;
            if (def?.building?.isSittable != true || Kind(chair) != RatDiningSeatKind.Chair) return false;
            RatDiningFurnitureExtension extension = def.GetModExtension<RatDiningFurnitureExtension>();
            if (extension != null) return extension.ratkinScale;
            string package = def.modContentPack?.PackageIdPlayerFacing;
            if (package == null) return false;
            return RatkinPackages.Contains(package)
                || (GloomyPackages.Contains(package) && GloomySeats.Contains(def.defName));
        }
    }
}
