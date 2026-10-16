using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace RatkinDiningEtiquette
{
    // A pawn's faction and guest status can change without destroying its job.
    // Save the original contract, rather than silently adopting new custody.
    public sealed class RatDiningCustodySnapshot : IExposable
    {
        public bool initialized;
        public bool prisoner;
        public bool slave;
        public Faction faction;
        public Faction hostFaction;
        public IntVec3 originCell = IntVec3.Invalid;

        internal void Bind(Pawn pawn, IntVec3 origin)
        {
            if (initialized || pawn == null) return;
            prisoner = pawn.IsPrisonerOfColony;
            slave = pawn.IsSlaveOfColony;
            faction = pawn.Faction;
            hostFaction = pawn.HostFaction;
            originCell = origin;
            initialized = true;
        }

        internal bool Matches(Pawn pawn)
        {
            return initialized && pawn != null && prisoner == pawn.IsPrisonerOfColony
                && slave == pawn.IsSlaveOfColony && faction == pawn.Faction && hostFaction == pawn.HostFaction;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "rdeCustodyInitialized");
            Scribe_Values.Look(ref prisoner, "rdePrisoner");
            Scribe_Values.Look(ref slave, "rdeSlave");
            Scribe_References.Look(ref faction, "rdeFaction");
            Scribe_References.Look(ref hostFaction, "rdeHostFaction");
            Scribe_Values.Look(ref originCell, "rdeOriginCell", IntVec3.Invalid);
        }
    }

    internal static class RatDiningCustody
    {
        internal static bool CanDine(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Drafted || pawn.InMentalState || pawn.IsFormingCaravan()) return false;
            if (pawn.IsPrisonerOfColony)
                return pawn.HostFaction == Faction.OfPlayer && pawn.guest?.PrisonerIsSecure == true;
            if (pawn.IsSlaveOfColony) return pawn.guest?.SlaveIsSecure == true;
            return pawn.IsColonist && pawn.HostFaction == null || IsVisitor(pawn);
        }

        internal static bool IsVisitor(Pawn pawn)
        {
            if (pawn?.MapHeld?.IsPlayerHome != true || !pawn.RaceProps.Humanlike
                || pawn.Faction == null || pawn.Faction == Faction.OfPlayer || pawn.Faction.HostileTo(Faction.OfPlayer)
                || pawn.IsPrisoner || pawn.IsSlave || pawn.guest?.Released == true
                || pawn.HostFaction != null && pawn.HostFaction != Faction.OfPlayer
                || pawn.IsFormingCaravan() || pawn.CurJob?.exitMapOnArrival == true) return false;
            // Arrival, departure, escort and wounded-guest duties must remain
            // available even before an exit job has been issued. The ordinary
            // visitor/trader rest toil derives from DefendPoint.
            Lord lord = pawn.GetLord();
            return lord == null ? pawn.mindState?.duty == null
                : lord.CurLordToil is LordToil_DefendPoint && pawn.mindState?.duty?.def == DutyDefOf.Defend;
        }

        internal static bool CanRequestHelp(Pawn pawn)
        {
            if (!CanDine(pawn) || !RatDiningMod.Settings.enableToddlerHelp) return false;
            // Secure slaves also count as colonists in vanilla; custody wins.
            if (pawn.IsSlaveOfColony) return RatDiningMod.Settings.enableSlaveToddlerHelp;
            if (pawn.IsPrisonerOfColony)
                return RatDiningMod.Settings.enablePrisonerToddlerHelp && pawn.guest.CanBeBroughtFood;
            if (IsVisitor(pawn)) return RatDiningMod.Settings.enableVisitorToddlerHelp;
            return RatDiningMod.Settings.enableColonistToddlerHelp;
        }

        internal static bool IsLegalCell(Pawn pawn, IntVec3 cell, IntVec3 origin)
        {
            if (pawn == null || !cell.IsValid) return false;
            Map map = pawn?.MapHeld;
            if (map == null || !cell.IsValid || !cell.InBounds(map)) return false;
            if (!pawn.IsPrisonerOfColony) return true;
            IntVec3 reference = origin.IsValid && origin.InBounds(map) ? origin : pawn.PositionHeld;
            if (!reference.IsValid || !reference.InBounds(map)) return false;
            Room room = reference.GetRoom(map);
            return room?.IsPrisonCell == true && room.ProperRoom && cell.GetRoom(map) == room;
        }

        internal static bool IsLegalCell(Pawn pawn, IntVec3 cell)
        { return IsLegalCell(pawn, cell, IntVec3.Invalid); }

        internal static bool ValidRequest(RatToddlerDiningRequest request)
        {
            if (request?.toddler == null || !CanRequestHelp(request.toddler)
                || !RatDiningFurniture.AllowedFor(request.toddler, request.chair)) return false;
            request.custody.Bind(request.toddler, request.stagingCell);
            return request.custody.Matches(request.toddler)
                && IsLegalCell(request.toddler, request.stagingCell, request.custody.originCell)
                && IsLegalCell(request.toddler, request.chair?.Position ?? IntVec3.Invalid, request.custody.originCell)
                && (request.state == RatToddlerDiningState.BeingCarried
                    && request.helper?.carryTracker?.CarriedThing == request.toddler
                    && RatToddlerDiningUtility.IsHelperJob(request, request.helper)
                    || IsLegalCell(request.toddler, request.toddler.PositionHeld, request.custody.originCell));
        }

        internal static bool TryDropChild(Pawn helper, Pawn child, IntVec3 origin)
        {
            if (helper?.carryTracker?.CarriedThing != child) return true;
            Map map = helper.MapHeld;
            if (map == null || child.MapHeld != map) return false;
            IntVec3 root = helper.PositionHeld;
            // Direct placement avoids Near choosing a cell across a prison door.
            if (TryDropAt(root)) return true;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                if (TryDropAt(root + GenAdj.CardinalDirections[i])) return true;
            return false;

            bool TryDropAt(IntVec3 cell)
            {
                return IsLegalRecoveryCell(child, cell, origin) && cell.Standable(map)
                    && cell.GetDangerFor(child, map) != Danger.Deadly
                    && GenTemperature.SafeTemperatureAtCell(child, cell, map)
                    && helper.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out Thing dropped)
                    && dropped == child;
            }
        }

        internal static bool IsLegalRecoveryCell(Pawn child, IntVec3 cell, IntVec3 origin)
        {
            if (child == null || !cell.IsValid) return false;
            Map map = child?.MapHeld;
            if (map == null || !cell.IsValid || !cell.InBounds(map)) return false;
            if (!child.IsPrisonerOfColony) return true;
            // Removing only the bed designation permits a drop in the intact
            // original room. A breached wall can merge that coordinate with
            // outdoors; room equality then no longer proves confinement.
            Room original = origin.IsValid && origin.InBounds(map) ? origin.GetRoom(map) : null;
            Room destination = cell.GetRoom(map);
            return destination?.ProperRoom == true && (destination == original || destination.IsPrisonCell);
        }
    }
}
