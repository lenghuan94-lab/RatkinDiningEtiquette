using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    internal static class RatDiningSpotFinder
    {
        internal static bool TryFindChairAsTable(Pawn pawn, out Building chair, out IntVec3 standCell, out Building table)
        {
            chair = null;
            table = null;
            standCell = IntVec3.Invalid;
            if (pawn?.Map == null) return false;

            float maxDistSq = RatDiningMod.Settings.chairSearchRadius * RatDiningMod.Settings.chairSearchRadius;
            float best = float.MaxValue;
            int bestRank = int.MaxValue;
            List<Building> buildings = pawn.Map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building candidate = buildings[i];
                if (!IsUsableChair(pawn, candidate) || pawn.Position.DistanceToSquared(candidate.Position) > maxDistSq) continue;
                if (pawn.Map.GetComponent<MapComponent_RatToddlerDining>()?.IsChairClaimed(candidate) == true) continue;
                if (!TryFindAdjacentStandCell(pawn, candidate, out IntVec3 adjacent)) continue;
                float score = pawn.Position.DistanceToSquared(candidate.Position);
                int rank = RatDiningFurniture.Kind(candidate) != RatDiningSeatKind.Chair ? 2
                    : RatDiningFurniture.IsRatkinScale(candidate) ? 0 : 1;
                if (rank > bestRank || (rank == bestRank && score >= best)) continue;
                bestRank = rank;
                best = score;
                chair = candidate;
                standCell = adjacent;
                // The legacy table result stays null: this mod handles lone chairs.
            }
            return chair != null;
        }

        internal static bool IsUsableChair(Pawn pawn, Building chair)
        {
            if (pawn?.Map == null || !RatDiningCustody.CanDine(pawn)
                || !RatDiningFurniture.AllowedFor(pawn, chair)) return false;
            if (!chair.Spawned || chair.Destroyed || chair.Map != pawn.Map || chair.IsBurning()) return false;
            if (!RatDiningCustody.IsLegalCell(pawn, chair.Position)) return false;
            if (FindAdjacentTable(chair) != null || chair.HostileTo(pawn) || !chair.IsSociallyProper(pawn)
                || chair.Position.Fogged(pawn.Map)) return false;
            if (chair.IsForbidden(pawn) || !chair.Position.InAllowedArea(pawn) || !pawn.CanReserve(chair, 1, -1, null, false)) return false;
            if (RatDiningApi.IsEligibleToddler(pawn) && !chair.Position.Standable(pawn.Map)) return false;
            if (!pawn.CanReach(chair, PathEndMode.Touch, Danger.Some)) return false;
            return chair.Position.GetDangerFor(pawn, pawn.Map) != Danger.Deadly;
        }

        internal static bool TryFindAdjacentStandCell(Pawn pawn, Building chair, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (pawn?.Map == null || chair?.Map != pawn.Map) return false;
            int best = int.MaxValue;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 candidate = chair.Position + GenAdj.CardinalDirections[i];
                if (!RatDiningCustody.IsLegalCell(pawn, candidate) || !candidate.Standable(pawn.Map) || candidate.IsForbidden(pawn)
                    || !candidate.InAllowedArea(pawn) || candidate.GetDangerFor(pawn, pawn.Map) == Danger.Deadly) continue;
                if (!pawn.CanReserveSittableOrSpot(candidate) || !pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Some)) continue;
                int dist = pawn.Position.DistanceToSquared(candidate);
                if (dist < best) { best = dist; cell = candidate; }
            }
            return cell.IsValid;
        }

        internal static Building FindAdjacentTable(Building chair)
        {
            if (chair?.Map == null) return null;
            foreach (IntVec3 occupied in chair.OccupiedRect())
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 cell = occupied + GenAdj.CardinalDirections[i];
                    if (!cell.InBounds(chair.Map)) continue;
                    Building table = cell.GetEdifice(chair.Map);
                    if (table?.def?.surfaceType == SurfaceType.Eat && table.def.building?.isSittable != true) return table;
                }
            return null;
        }
    }
}
