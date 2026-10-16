using System;
using System.Linq;
using System.Reflection;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

// Execute RDE production callbacks with table-selector results supplied by the
// engine double. RTM's DLL, real Harmony and real pathfinding are not executed.
internal static partial class Program
{
    private static void TableMannersCompatibilityTests()
    {
        Test("automatic self-feeding preserves seated and standing table jobs for both ages", () =>
        {
            foreach (bool toddler in new[] { false, true }) foreach (bool seated in new[] { false, true })
            {
                var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
                var pawn = new Pawn { Map = map, Toddler = toddler }; var food = new Thing { Map = map, stackCount = 10 };
                Furniture(map, 12, true, true); Furniture(map, 21, false, true);
                if (seated) Furniture(map, 20, true, true);
                Toils_Ingest.TestDiningCell = new IntVec3(20);
                var original = JobMaker.MakeJob(JobDefOf.Ingest, food); original.count = 4;
                object[] args = { pawn, original };
                typeof(JobGiver_GetFood_Patch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
                Check(ReferenceEquals(args[1], original) && original.count == 4, "table job and portions unchanged");
                Check(map.Manager.Requests.Count == 0 && !map.Manager.IsChairClaimed(map.Buildings[0]), "no lone-chair request or claim");
            }
        });
        Test("a fresh caregiver meal defers to a selected table seat or standing spot", () =>
        {
            foreach (bool seated in new[] { false, true })
            {
                var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
                var child = new Pawn { Map = map }; var helper = new Pawn { Map = map, Toddler = false };
                var food = new Thing { Map = map, stackCount = 10 };
                var loneChair = Furniture(map, 12, true, true); Furniture(map, 21, false, true);
                if (seated) Furniture(map, 20, true, true);
                Toils_Ingest.TestDiningCell = new IntVec3(20);
                var original = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, child, food); original.count = 4;
                helper.jobs.StartJob(original);
                Check(helper.CurJob == original && child.CurJob == null, "do not replace normal feeding or the child's job");
                Check(map.Manager.Requests.Count == 0 && !map.Manager.IsChairClaimed(loneChair), "no unused meal request");
                Check(food.stackCount == 10 && child.carryTracker.CarriedThing == null, "do not split or take its food");
            }
        });
        Test("an active lone-chair request stays committed when the selector later prefers a table", () =>
        {
            var f = new Fixture(); f.Waiting();
            Furniture(f.Map, 31, false, true); Furniture(f.Map, 30, true, true);
            Toils_Ingest.TestDiningCell = new IntVec3(30);
            f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
            Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, "adopt the committed meal");
            Check(f.Manager.Requests.Count == 1 && f.Request.chair == f.Chair && f.Request.food == f.Food, "same request, chair and original food");
            var second = new Pawn { Map = f.Map, Toddler = false };
            second.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
            Check(second.CurJobDef == JobDefOf.Wait && f.Request.helper == f.Helper, "no second caregiver steals it");
        });
        Test("a chair becoming table-connected releases its request before ordinary feeding starts", () =>
        {
            var f = new Fixture(); f.Waiting(); Furniture(f.Map, 21, false, true);
            Toils_Ingest.TestDiningCell = f.Chair.Position;
            var original = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
            f.Helper.jobs.StartJob(original);
            Check(f.Helper.CurJob == original && !f.Request.IsActive, "table-linked request yields to ordinary feeding");
            Check(!f.Manager.IsChairClaimed(f.Chair) && f.Manager.GetForPawn(f.Child) == null, "chair and toddler claims released");
            Check(f.Child.carryTracker.CarriedThing == f.Food && !f.Food.Destroyed, "original meal survives");
            Check(f.Child.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RDE_DefOf.RDE_ToddlerNoDiningHelp) == null, "not a failed-help complaint");
        });
        Test("normal table memories survive either order of the shared ingestion callbacks", () =>
        {
            foreach (bool tableFirst in new[] { false, true })
            {
                var pawn = new Pawn { Map = new Map() }; var food = new Thing { Map = pawn.Map };
                pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, food));
                var memories = pawn.needs.mood.thoughts.memories;
                RatDiningThoughts.ApplyChair(pawn, Furniture(pawn.Map, 12, true, true));
                var external = new ThoughtDef { defName = "RatkinTableRatkinSeatAge0To3" };
                var mealQuality = new ThoughtDef { defName = "FineMeal" }; memories.TryGainMemory(mealQuality);
                if (tableFirst) memories.TryGainMemory(external);
                Thing_Ingested_ClearPreviousChairThought.Postfix(food, pawn, .5f);
                if (!tableFirst) memories.TryGainMemory(external);
                Check(memories.Memories.Count == 2 && memories.GetFirstMemoryOfDef(external) != null
                    && memories.GetFirstMemoryOfDef(mealQuality) != null, "only the previous RDE chair memory retires");
            }
        });
    }
}
