using Verse;

namespace RatkinDiningEtiquette
{
    public enum RatToddlerDiningState
    {
        MovingFoodToDiningSpot, WaitingForHelp, Claimed, BeingCarried,
        PlacedAtChair, EatingAtTable, EatingOnChair, FloorFallback, // EatingAtTable is a legacy save value.
        Completed, Failed, TimedOut, CollectingFood
    }

    public sealed class RatToddlerDiningRequest : IExposable
    {
        public int id;
        public Pawn toddler;
        public Thing food;
        public Building chair;
        public Building table; // Legacy save field; new requests must leave it null.
        public IntVec3 stagingCell = IntVec3.Invalid;
        public Pawn helper;
        public RatToddlerDiningState state;
        public int createdTick;
        public int stateTick;
        public int claimTick;
        public int waitStartedTick = -1;
        public int helperJobId = -1;
        public int foodCount = 1;
        public bool mealFinalized;
        public int mealJobId = -1;
        public int cooldownUntilTick;
        public int retryCount;
        public bool failureThoughtApplied;
        public bool recoveryPending;
        public bool resumeWaitingPending;
        public bool recoverMeal = true;
        public bool careHandoff;
        public int recoveryStartedTick = -1;
        public int returnAttempts;
        public int returnJobId = -1;
        public RatDiningCustodySnapshot custody = new RatDiningCustodySnapshot();

        public bool IsActive => state != RatToddlerDiningState.Completed && state != RatToddlerDiningState.Failed
            && state != RatToddlerDiningState.TimedOut && state != RatToddlerDiningState.FloorFallback;
        public bool IsAvailable => state == RatToddlerDiningState.WaitingForHelp && helper == null
            && cooldownUntilTick <= Find.TickManager.TicksGame;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_References.Look(ref toddler, "toddler");
            Scribe_References.Look(ref food, "food");
            Scribe_References.Look(ref chair, "chair");
            Scribe_References.Look(ref table, "table");
            Scribe_Values.Look(ref stagingCell, "stagingCell", IntVec3.Invalid);
            Scribe_References.Look(ref helper, "helper");
            Scribe_Values.Look(ref state, "state", RatToddlerDiningState.MovingFoodToDiningSpot);
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref stateTick, "stateTick");
            Scribe_Values.Look(ref claimTick, "claimTick");
            Scribe_Values.Look(ref waitStartedTick, "waitStartedTick", -1);
            Scribe_Values.Look(ref helperJobId, "helperJobId", -1);
            Scribe_Values.Look(ref foodCount, "foodCount", 1);
            Scribe_Values.Look(ref mealFinalized, "rdeMealFinalized");
            Scribe_Values.Look(ref mealJobId, "rdeMealJobId", -1);
            Scribe_Values.Look(ref cooldownUntilTick, "cooldownUntilTick");
            Scribe_Values.Look(ref retryCount, "retryCount");
            Scribe_Values.Look(ref failureThoughtApplied, "failureThoughtApplied");
            Scribe_Values.Look(ref recoveryPending, "recoveryPending");
            Scribe_Values.Look(ref resumeWaitingPending, "resumeWaitingPending");
            Scribe_Values.Look(ref recoverMeal, "rdeRecoverMeal", true);
            Scribe_Values.Look(ref careHandoff, "rdeCareHandoff");
            Scribe_Values.Look(ref recoveryStartedTick, "rdeRecoveryStarted", -1);
            Scribe_Values.Look(ref returnAttempts, "rdeReturnAttempts");
            Scribe_Values.Look(ref returnJobId, "rdeReturnJobId", -1);
            custody.ExposeData();
        }
    }
}
