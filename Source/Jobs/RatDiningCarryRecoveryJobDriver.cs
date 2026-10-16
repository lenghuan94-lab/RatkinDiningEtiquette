using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    // This is safety cleanup, not another chair request or a new food selection.
    public sealed class JobDriver_ReturnDiningChild : JobDriver
    {
        private int startedTick = -1;
        private bool placed;
        private Pawn Child => job.targetA.Pawn;
        private IntVec3 Origin => job.targetC.Cell;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref startedTick, "rdeReturnStarted", -1);
            Scribe_Values.Look(ref placed, "rdeReturnPlaced");
            base.ExposeData();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Child != null && pawn.carryTracker?.CarriedThing == Child && SafeTarget()
                && pawn.ReserveSittableOrSpot(job.targetB.Cell, job, false);
        }

        private bool SafeTarget()
        {
            RatToddlerDiningRequest request = RatDiningCarryRecovery.GetContext(pawn, true);
            if (request != null && !RatDiningCarryRecovery.CanContinueReturn(request)) return false;
            return RatDiningCarryRecovery.IsSafeReturnTarget(pawn, Child, job.targetB.Cell, Origin);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (startedTick < 0) startedTick = Find.TickManager.TicksGame;
            AddFinishAction(condition =>
            {
                if (RatDiningCarryRecovery.GetContext(pawn, true)?.careHandoff != true)
                    RatDiningCustody.TryDropChild(pawn, Child, Origin);
            });
            this.FailOn(() => !placed && (pawn.Dead || pawn.Downed || pawn.Drafted || pawn.InMentalState || Child == null || Child.Dead
                || pawn.carryTracker?.CarriedThing != Child || !SafeTarget()
                || Find.TickManager.TicksGame - startedTick >= RatDiningCarryRecovery.ReturnTimeoutTicks));
            Toil walk = ToilMaker.MakeToil("RDE_ReturnChildToSafeCell");
            walk.initAction = delegate { pawn.pather.StartPath(job.targetB, PathEndMode.OnCell); };
            walk.tickIntervalAction = delta =>
            {
                if (Find.TickManager.TicksGame - startedTick >= RatDiningCarryRecovery.ReturnTimeoutTicks)
                    EndJobWith(JobCondition.Incompletable);
            };
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return walk;
            Toil place = ToilMaker.MakeToil("RDE_PutDownReturnedChild");
            place.initAction = delegate
            {
                if (!SafeTarget() || !RatDiningCustody.TryDropChild(pawn, Child, Origin))
                { EndJobWith(JobCondition.Incompletable); return; }
                placed = true;
            };
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return place;
        }
    }
}
