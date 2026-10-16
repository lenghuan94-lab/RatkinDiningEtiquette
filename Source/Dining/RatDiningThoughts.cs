using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RatkinDiningEtiquette
{
    public sealed class Thought_Memory_RatDining : Thought_Memory
    {
        public int repetitions = 1;
        public int biologicalAgeBand = -1;
        public int lastMealJobId = -1;
        internal bool suppressLoadBubble;

        // NeedsCardUtility shows BabyTalk above the localized Description as
        // its translation. Keep these channels separate to avoid babbling twice.
        public override string BabyTalk
        {
            get
            {
                int stage = CurStageIndex;
                if (stage >= 0 && stage < RatDiningThoughts.MaxRepetitions)
                {
                    string group = def == RDE_DefOf.RDE_UsedChairAsTable ? "OrdinaryChair"
                        : def == RDE_DefOf.RDE_RatkinChairAsTable ? "RatkinChair"
                        : def == RDE_DefOf.RDE_ToiletAsTable ? "Toilet"
                        : def == RDE_DefOf.RDE_SpecialSeatAsTable ? "SpecialSeat"
                        : def == RDE_DefOf.RDE_ToddlerNoDiningHelp ? "NoHelp" : null;
                    if (group != null) return ("RDE_BabyTalk_" + group + "_" + stage).Translate().ToString();
                }
                return base.BabyTalk;
            }
        }

        public override bool TryMergeWithExistingMemory(out bool showBubble)
        {
            bool merged = base.TryMergeWithExistingMemory(out showBubble);
            if (suppressLoadBubble) showBubble = false;
            return merged;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref repetitions, "rdeRepetitions", 1);
            Scribe_Values.Look(ref biologicalAgeBand, "rdeAgeBand", -1);
            Scribe_Values.Look(ref lastMealJobId, "rdeLastMealJobId", -1);
        }
    }

    internal static class RatDiningThoughts
    {
        internal const int MaxRepetitions = 3;
        // These identifiers are readable only to retire memories in 1.0.6 saves.
        // They never receive new memories and never include another mod's defs.
        private static readonly HashSet<string> RetiredDefs = new HashSet<string>(StringComparer.Ordinal)
        {
            "RDE_ToddlerAteOnChair", "RDE_RatkinDiningSet", "RDE_RatkinTableMixedChair",
            "RDE_RatkinTableWithoutChair", "RDE_RatkinChairHighTable", "RDE_OrdinaryDiningSet", "RDE_OrdinaryTableWithoutChair"
        };

        internal static int AgeBand(Pawn pawn)
        {
            float years = pawn.ageTracker?.AgeBiologicalYearsFloat ?? 18f;
            return years < 4f ? 0 : years < 14f ? 1 : years < 18f ? 2 : 3;
        }

        internal static ThoughtDef ForChair(Building chair)
        {
            RatDiningSeatKind kind = RatDiningFurniture.Kind(chair);
            return kind == RatDiningSeatKind.Toilet ? RDE_DefOf.RDE_ToiletAsTable
                : kind == RatDiningSeatKind.SpecialSeat ? RDE_DefOf.RDE_SpecialSeatAsTable
                : RatDiningFurniture.IsRatkinScale(chair) ? RDE_DefOf.RDE_RatkinChairAsTable : RDE_DefOf.RDE_UsedChairAsTable;
        }

        internal static bool IsOwnChairThought(ThoughtDef def) => def != null &&
            (def == RDE_DefOf.RDE_UsedChairAsTable || def == RDE_DefOf.RDE_RatkinChairAsTable
                || def == RDE_DefOf.RDE_ToiletAsTable || def == RDE_DefOf.RDE_SpecialSeatAsTable
                || RetiredDefs.Contains(def.defName));

        internal static void ClearChair(Pawn pawn)
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null) return;
            for (int i = memories.Memories.Count - 1; i >= 0; i--)
                if (IsOwnChairThought(memories.Memories[i].def)) memories.RemoveMemory(memories.Memories[i]);
        }

        internal static void ApplyChair(Pawn pawn, Building chair)
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null) return;
            ThoughtDef def = ForChair(chair);
            int band = AgeBand(pawn);
            int jobId = pawn.CurJob?.loadID ?? -1;
            Thought_Memory_RatDining previous = FindCurrent(memories, def) as Thought_Memory_RatDining;
            if (previous != null && previous.biologicalAgeBand == band && jobId >= 0 && previous.lastMealJobId == jobId) return;
            int repetitions = previous?.biologicalAgeBand == band ? Clamp(Clamp(previous.repetitions) + 1) : 1;
            // One displayed memory holds the TOTAL for all repetitions. Physical
            // memory stacking would multiply the +4/-3 limit a second time.
            ClearAllOwnDining(pawn);
            memories.TryGainMemory(MakeMemory(def, band, repetitions, jobId));
        }

        internal static void ApplyHelpTimeout(Pawn pawn)
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null) return;
            Thought_Memory_RatDining previous = FindCurrent(memories, RDE_DefOf.RDE_ToddlerNoDiningHelp) as Thought_Memory_RatDining;
            int repetitions = previous == null ? 1 : Clamp(Clamp(previous.repetitions) + 1);
            ClearAllOwnDining(pawn);
            memories.TryGainMemory(MakeMemory(RDE_DefOf.RDE_ToddlerNoDiningHelp, 0, repetitions, -1));
        }

        private static int Clamp(int repetitions) => Math.Max(1, Math.Min(MaxRepetitions, repetitions));

        private static Thought_Memory FindCurrent(MemoryThoughtHandler memories, ThoughtDef def)
        {
            for (int i = memories.Memories.Count - 1; i >= 0; i--)
                if (memories.Memories[i].def == def && !memories.Memories[i].ShouldDiscard) return memories.Memories[i];
            return null;
        }

        private static Thought_Memory_RatDining MakeMemory(ThoughtDef def, int band, int repetitions, int jobId)
        {
            int stage = def == RDE_DefOf.RDE_ToddlerNoDiningHelp ? repetitions - 1 : band * MaxRepetitions + repetitions - 1;
            var memory = (Thought_Memory_RatDining)ThoughtMaker.MakeThought(def, stage);
            memory.repetitions = repetitions;
            memory.biologicalAgeBand = band;
            memory.lastMealJobId = jobId;
            return memory;
        }

        private static void ClearAllOwnDining(Pawn pawn)
        {
            ClearChair(pawn);
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null) return;
            for (int i = memories.Memories.Count - 1; i >= 0; i--)
                if (memories.Memories[i].def == RDE_DefOf.RDE_ToddlerNoDiningHelp) memories.RemoveMemory(memories.Memories[i]);
        }

        internal static void RefreshLoadedMemories(Pawn pawn)
        {
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null) return;
            Thought_Memory newest = null;
            for (int i = memories.Memories.Count - 1; i >= 0; i--)
            {
                Thought_Memory memory = memories.Memories[i];
                if (memory.ShouldDiscard || RetiredDefs.Contains(memory.def.defName)) continue;
                if ((IsOwnChairThought(memory.def) || memory.def == RDE_DefOf.RDE_ToddlerNoDiningHelp)
                    && (newest == null || memory.age < newest.age)) newest = memory;
            }
            ClearAllOwnDining(pawn);
            if (newest == null) return;
            int band = newest.def == RDE_DefOf.RDE_ToddlerNoDiningHelp ? 0 : AgeBand(pawn);
            Thought_Memory_RatDining previous = newest as Thought_Memory_RatDining;
            int repetitions = previous?.biologicalAgeBand == band ? Clamp(previous.repetitions) : 1;
            // Old base-class memories migrate as one event. New saves keep their
            // repetition count, completed-job identity and remaining lifetime.
            Thought_Memory_RatDining migrated = MakeMemory(newest.def, band, repetitions, previous?.lastMealJobId ?? -1);
            migrated.age = newest.age;
            migrated.suppressLoadBubble = true;
            memories.TryGainMemory(migrated);
        }
    }
}
