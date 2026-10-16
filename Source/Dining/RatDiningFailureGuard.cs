using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace RatkinDiningEtiquette
{
    internal static class RatDiningFailureGuard
    {
        private const int FailureCooldownTicks = 120;
        // Pawn IDs and ticks are reused when a different save is loaded. Weak
        // identity keys prevent an old save's transient cooldown crossing over.
        private sealed class State
        {
            public State() { }
            public int cooldownUntil = int.MinValue;
            public int issuedTick = int.MinValue;
            public int warningTick = int.MinValue;
        }
        private static readonly ConditionalWeakTable<Pawn, State> States = new ConditionalWeakTable<Pawn, State>();

        internal static bool IsCoolingDown(Pawn pawn)
        {
            if (pawn == null) return false;
            return States.TryGetValue(pawn, out State state)
                && (state.issuedTick == Find.TickManager.TicksGame || Find.TickManager.TicksGame < state.cooldownUntil);
        }

        internal static void NotifyIssued(Pawn pawn)
        {
            if (pawn != null) States.GetOrCreateValue(pawn).issuedTick = Find.TickManager.TicksGame;
        }

        internal static void NotifyEnded(Pawn pawn, JobCondition condition)
        {
            if (pawn == null) return;
            State state = States.GetOrCreateValue(pawn);
            if (condition == JobCondition.Succeeded)
            {
                state.cooldownUntil = int.MinValue;
                return;
            }
            state.cooldownUntil = Find.TickManager.TicksGame + FailureCooldownTicks;
            if ((condition == JobCondition.Incompletable || condition == JobCondition.Errored || condition == JobCondition.ErroredPather)
                && (state.warningTick == int.MinValue || Find.TickManager.TicksGame - state.warningTick >= 2500))
            {
                state.warningTick = Find.TickManager.TicksGame;
                Log.Warning($"[RDE] Chair-table job for {pawn} ended with {condition}; falling back to vanilla ingestion for {FailureCooldownTicks} ticks.");
            }
            RatDiningApi.Debug($"Chair-table job for {pawn} ended with {condition}; using vanilla ingestion during cooldown.");
        }
    }
}
