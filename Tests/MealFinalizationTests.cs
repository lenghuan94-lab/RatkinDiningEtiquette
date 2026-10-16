using System;
using System.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static void MealFinalizationTests()
    {
        Test("transient failure cooldown does not follow a pawn ID into a different loaded save", () =>
        {
            var oldPawn = new Pawn { thingIDNumber = 38447 };
            var loadedPawn = new Pawn { thingIDNumber = oldPawn.thingIDNumber };
            Find.TickManager.TicksGame = 20000;
            RatDiningFailureGuard.NotifyEnded(oldPawn, JobCondition.Incompletable);
            Find.TickManager.TicksGame = 6600;
            Check(RatDiningFailureGuard.IsCoolingDown(oldPawn), "original live pawn remains guarded");
            Check(!RatDiningFailureGuard.IsCoolingDown(loadedPawn), "loaded pawn gets no previous-save delay");
            RatDiningFailureGuard.NotifyIssued(oldPawn);
            Check(!RatDiningFailureGuard.IsCoolingDown(loadedPawn), "same-tick issued guard uses live identity too");
        });
        Test("failure guard preserves same-tick deduplication, expiry and successful recovery", () =>
        {
            var pawn = new Pawn();
            RatDiningFailureGuard.NotifyIssued(pawn);
            Check(RatDiningFailureGuard.IsCoolingDown(pawn), "same tick blocks duplicate custom issue");
            Find.TickManager.TicksGame = 1;
            Check(!RatDiningFailureGuard.IsCoolingDown(pawn), "next tick allows normal selection");
            RatDiningFailureGuard.NotifyEnded(pawn, JobCondition.Errored);
            Find.TickManager.TicksGame = 120;
            Check(RatDiningFailureGuard.IsCoolingDown(pawn), "failure cooldown still active");
            Find.TickManager.TicksGame = 121;
            Check(!RatDiningFailureGuard.IsCoolingDown(pawn), "cooldown expires at its bound");
            RatDiningFailureGuard.NotifyEnded(pawn, JobCondition.Incompletable);
            RatDiningFailureGuard.NotifyEnded(pawn, JobCondition.Succeeded);
            Check(!RatDiningFailureGuard.IsCoolingDown(pawn), "successful recovery clears failure state");
        });
        Test("chair meal preserves the identity and age of pre-existing vanilla table memories", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map, Toddler = false };
            var chair = Furniture(map, 20, true, false); var food = new Thing { Map = map };
            var memories = pawn.needs.mood.thoughts.memories;
            memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
            var old = memories.Memories.Single(); old.age = 1234;
            pawn.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, food, chair, new IntVec3(19)));
            var toils = Attach(new JobDriver_EatUsingChairAsTable(), pawn); Run(toils, 0, 7);
            Check(memories.GetFirstMemoryOfDef(ThoughtDefOf.AteWithoutTable) == old && old.age == 1234, "old memory untouched");
            Check(memories.Memories.Count(m => m.def == ThoughtDefOf.AteWithoutTable) == 1, "chair meal does not append penalty");
        });
        Test("chair memory suppression is scoped to one handler and survives nested scopes and exceptions", () =>
        {
            var pawn = new Pawn(); var other = new Pawn(); var external = new ThoughtDef { defName = "ExternalMealThought" };
            var memories = pawn.needs.mood.thoughts.memories;
            try
            {
                using (RatDiningMealSafety.FinalizeChair(pawn))
                {
                    memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
                    memories.TryGainMemory(external);
                    other.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
                    using (RatDiningMealSafety.FinalizeChair(other)) memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
                    memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
                    throw new InvalidOperationException("probe");
                }
            }
            catch (InvalidOperationException) { }
            Check(memories.GetFirstMemoryOfDef(external) != null && memories.Memories.Count == 2, "external and nested different handler unchanged");
            memories.TryGainMemory(ThoughtDefOf.AteWithoutTable);
            Check(memories.Memories.Count == 3 && other.needs.mood.thoughts.memories.Memories.Count == 1, "scope restored after exception");
        });
        Test("floor meal on the map boundary rotates safely and retains vanilla floor penalty", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map, Position = new IntVec3(0), Rotation = new Rot4(1) };
            var food = new Thing { Map = map, Spawned = false }; pawn.carryTracker.CarriedThing = food;
            pawn.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, food), keepCarryingThingOverride: true);
            var toils = Attach(new JobDriver_ToddlerEatHeldFoodOnFloor(), pawn); Run(toils, 0, toils.Count - 1);
            Check((pawn.Position + pawn.Rotation.FacingCell).InBounds(map) && food.Destroyed, "safe finalization consumes meal");
            Check(pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(ThoughtDefOf.AteWithoutTable) != null, "vanilla floor memory preserved");
        });
        Test("owned vanilla finalization guards map edges while unowned finalization remains unchanged", () =>
        {
            var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
            var pawn = new Pawn { Map = map, Position = new IntVec3(0), Rotation = new Rot4(1), Toddler = false };
            var food = new Thing { Map = map, Spawned = false }; pawn.carryTracker.CarriedThing = food;
            Job job = EvaluateIngest(pawn, food); pawn.jobs.StartJob(job, keepCarryingThingOverride: true);
            Toils_Ingest.FinalizeIngest(pawn, TargetIndex.A).initAction();
            Check(food.Destroyed && (pawn.Position + pawn.Rotation.FacingCell).InBounds(map), "indexed vanilla meal protected");
            var ordinary = new Pawn { Map = map, Position = new IntVec3(0), Rotation = new Rot4(1), def = new ThingDef() };
            ordinary.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, new Thing { Map = map }));
            bool threw = false;
            try { Toils_Ingest.FinalizeIngest(ordinary, TargetIndex.A).initAction(); }
            catch (IndexOutOfRangeException) { threw = true; }
            Check(threw, "unowned callback receives no dining changes");
        });
        Test("corpse chair meal repeats body-part bites without repeating the chair reward", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map, Toddler = false };
            var food = new Corpse { Map = map }; var chair = Furniture(map, 20, true, false);
            pawn.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, food, chair, new IntVec3(19)));
            var toils = Attach(new JobDriver_EatUsingChairAsTable(), pawn);
            Check(toils.Count == 9, "corpse jump appended after existing save indices");
            Run(toils, 0, 8); int bites = 1;
            while (Toils_Jump.LastJump && bites++ < 10) { Check(Toils_Jump.LastTarget == toils[5], "jump to chew only"); Run(toils, 5, 8); }
            Check(bites == 4 && food.BodyParts == 1 && pawn.needs.food.CurLevelPercentage >= .9f, "repeat until fed");
            var memory = (Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.Memories.Single();
            Check(memory.repetitions == 1, "one reward per meal job");
        });
        Test("floor recovery stops when self-feeding eligibility is lost", () =>
        {
            var pawn = new Pawn { Map = new Map() }; var food = new Thing { Map = pawn.Map, Spawned = false };
            pawn.carryTracker.CarriedThing = food;
            pawn.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, food), keepCarryingThingOverride: true);
            var driver = new JobDriver_ToddlerEatHeldFoodOnFloor(); Attach(driver, pawn);
            pawn.SelfFeeding = false; Check(driver.Failures.Any(f => f()) && !food.Destroyed, "stop before consuming");
        });
    }
}
