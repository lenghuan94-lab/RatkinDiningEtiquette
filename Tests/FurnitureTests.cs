using System;
using System.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static Building Furniture(Map map, int position, bool seat, bool ratkin)
    {
        var building = new Building { Map = map, Position = new IntVec3(position),
            def = new ThingDef { building = new BuildingProperties { isSittable = seat },
                surfaceType = seat ? SurfaceType.None : SurfaceType.Eat,
                Extension = new RatDiningFurnitureExtension { ratkinScale = ratkin } } };
        map.Buildings.Add(building);
        return building;
    }

    private static void FurnitureTests()
    {
        Test("chair classifier uses supported packages and rejects tables", () =>
        {
            var map = new Map(); var chair = Furniture(map, 10, true, false); chair.def.Extension = null;
            chair.def.modContentPack = new ModContentPack { PackageIdPlayerFacing = "gloomy.furniture.continued" };
            chair.def.defName = "RGK_Chair";
            Check(RatDiningFurniture.IsRatkinScale(chair), "supported little chair");
            chair.def.defName = "DiningChair"; Check(!RatDiningFurniture.IsRatkinScale(chair), "ordinary chair in same pack");
            chair.def.modContentPack.PackageIdPlayerFacing = "unknown.mod"; chair.def.defName = "RGK_Chair";
            Check(!RatDiningFurniture.IsRatkinScale(chair), "name alone is insufficient");
            chair.def.modContentPack.PackageIdPlayerFacing = "OARK.RatkinKnightOrderFurniture";
            Check(RatDiningFurniture.IsRatkinScale(chair), "Ratkin furniture package");
            Check(!RatDiningFurniture.IsRatkinScale(Furniture(map, 11, false, true)), "table with opt-in still not a chair");
        });
        Test("explicit chair extension supports opt-in and opt-out", () =>
        {
            var chair = Furniture(new Map(), 10, true, false);
            chair.def.modContentPack = new ModContentPack { PackageIdPlayerFacing = "OARK.RatkinKnightOrderFurniture" };
            Check(!RatDiningFurniture.IsRatkinScale(chair), "explicit opt-out");
            ((RatDiningFurnitureExtension)chair.def.Extension).ratkinScale = true;
            Check(RatDiningFurniture.IsRatkinScale(chair), "explicit opt-in");
        });
        Test("four active seat memories with four biological age bands", () =>
        {
            var doc = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "ThoughtDefs.xml"));
            Check(doc.Root.Elements().Count() == 5, "four seat defs plus actual help timeout");
            foreach (var thought in doc.Root.Elements().Where(e => e.Element("defName").Value != "RDE_ToddlerNoDiningHelp"))
            {
                Check(thought.Element("stages").Elements().Count() == 12, "four ages times three repetitions");
                Check(thought.Element("developmentalStageFilter").Value == "Baby, Child, Adult", "baby filter");
                Check(thought.Element("stages").Elements().Select(e => e.Element("label").Value).Distinct().Count() == 12, "distinct age and repetition wording");
            }
        });
        Test("biological age boundaries are independent of toddler-learning status", () =>
        {
            var pawn = new Pawn { Toddler = false };
            float[] years = { 0, 3.99f, 4, 13.99f, 14, 17.99f, 18, 90 };
            int[] expected = { 0, 0, 1, 1, 2, 2, 3, 3 };
            for (int i = 0; i < years.Length; i++)
            { pawn.ageTracker.AgeBiologicalYearsFloat = years[i]; Check(RatDiningThoughts.AgeBand(pawn) == expected[i], "age " + years[i]); }
        });
        Test("each chair type grants its own age-specific mood", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map };
            var ordinary = Furniture(map, 10, true, false); var ratkin = Furniture(map, 20, true, true);
            float[] years = { 2, 8, 16, 25 }, ordinaryMood = { 1, 1, -1, -1 }, ratkinMood = { 2, 2, 1, 1 };
            for (int band = 0; band < years.Length; band++)
            {
                pawn.ageTracker.AgeBiologicalYearsFloat = years[band];
                RatDiningThoughts.ApplyChair(pawn, ordinary); var memory = pawn.needs.mood.thoughts.memories.Memories.Single();
                Check(memory.def == RDE_DefOf.RDE_UsedChairAsTable && memory.stage == band * 3 && memory.def.stages[band * 3] == ordinaryMood[band], "ordinary age mood");
                RatDiningThoughts.ApplyChair(pawn, ratkin); memory = pawn.needs.mood.thoughts.memories.Memories.Single();
                Check(memory.def == RDE_DefOf.RDE_RatkinChairAsTable && memory.stage == band * 3 && memory.def.stages[band * 3] == ratkinMood[band], "Ratkin age mood");
            }
        });
        Test("actual finder prefers a lone Ratkin chair and never returns a table", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map, Toddler = false };
            Furniture(map, 12, true, false); var preferred = Furniture(map, 30, true, true);
            Check(RatDiningSpotFinder.TryFindChairAsTable(pawn, out var chair, out var staging, out var table)
                && chair == preferred && staging.IsValid && table == null, "prefer small chair within radius");
            preferred.Forbidden = true;
            Check(RatDiningSpotFinder.TryFindChairAsTable(pawn, out chair, out _, out _) && chair.Position == new IntVec3(12), "fallback to ordinary chair");
        });
        Test("connected chairs are excluded even when the table is forbidden", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var chair = Furniture(map, 12, true, true);
            var table = Furniture(map, 13, false, true);
            Check(!RatDiningSpotFinder.IsUsableChair(pawn, chair), "complete set belongs to table mod");
            table.Forbidden = true; Check(!RatDiningSpotFinder.IsUsableChair(pawn, chair), "forbidden table does not turn set into lone chair");
            Check(!RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _), "no eligible lone chairs");
        });
        Test("actual finder respects reservation, danger, radius and request claims", () =>
        {
            var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
            var pawn = new Pawn { Map = map }; var chair = Furniture(map, 12, true, true);
            Check(RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _), "baseline");
            pawn.CanReserveTargets = false; Check(!RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _), "reservation"); pawn.CanReserveTargets = true;
            pawn.CanReachTargets = false; Check(!RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _), "path"); pawn.CanReachTargets = true;
            pawn.Danger = Danger.Deadly; Check(!RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _), "danger"); pawn.Danger = Danger.None;
            RatDiningMod.Settings.chairSearchRadius = 1; Check(!RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _), "radius"); RatDiningMod.Settings.chairSearchRadius = 32;
            var request = map.Manager.Create(pawn, new Thing { Map = map }, chair, null, new IntVec3(11)); Check(request != null, "claimed fixture");
            var other = new Pawn { Map = map }; Check(!RatDiningSpotFinder.TryFindChairAsTable(other, out _, out _, out _), "request claim");
        });
        Test("existing table meals retain normal mode for adults and toddlers", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var food = new Thing { Map = map };
            Furniture(map, 20, true, false); Furniture(map, 21, false, true); Furniture(map, 12, true, true);
            Toils_Ingest.TestDiningCell = new IntVec3(20);
            foreach (bool toddler in new[] { true, false })
            {
                pawn.Toddler = toddler;
                Check(RatDiningModeResolver.TryResolve(pawn, food, out var mode, out var chair, out _, out _)
                    && mode == RatDiningMode.NormalDining && chair == null, "normal table meal untouched");
            }
        });
        Test("table-only map never creates a chair or standing-table job", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map, Toddler = false };
            Furniture(map, 12, false, true);
            Check(RatDiningModeResolver.TryResolve(pawn, new Thing { Map = map }, out var mode, out var chair, out _, out _)
                && mode == RatDiningMode.FloorFallback && chair == null, "no new table mode");
        });
        Test("chair memory replacement never removes another mod's table memories", () =>
        {
            var pawn = new Pawn(); var memories = pawn.needs.mood.thoughts.memories;
            var external = new ThoughtDef { defName = "RatkinTableRatkinSeatAge18Plus" };
            var fineMeal = new ThoughtDef { defName = "FineMeal" };
            memories.TryGainMemory(external); memories.TryGainMemory(fineMeal); memories.TryGainMemory(RDE_DefOf.RDE_ToddlerNoDiningHelp);
            RatDiningThoughts.ApplyChair(pawn, Furniture(new Map(), 12, true, true));
            RatDiningThoughts.ApplyChair(pawn, Furniture(new Map(), 12, true, false));
            Check(memories.Memories.Count == 3 && memories.GetFirstMemoryOfDef(external) != null && memories.GetFirstMemoryOfDef(fineMeal) != null
                && memories.GetFirstMemoryOfDef(RDE_DefOf.RDE_ToddlerNoDiningHelp) == null, "external moods preserved and successful meal resolves help complaint");
            Check(memories.Memories.Count(m => RatDiningThoughts.IsOwnChairThought(m.def)) == 1, "one chair memory");
        });
        Test("normal meal callback removes only this mod's previous chair reward", () =>
        {
            var pawn = new Pawn { Map = new Map() }; var food = new Thing();
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, food));
            var memories = pawn.needs.mood.thoughts.memories; var external = new ThoughtDef { defName = "ToddlerRatkinCannotReachTable" };
            memories.TryGainMemory(external); memories.TryGainMemory(RDE_DefOf.RDE_RatkinChairAsTable);
            Thing_Ingested_ClearPreviousChairThought.Postfix(food, pawn, .5f);
            Check(memories.Memories.Count == 1 && memories.Memories[0].def == external, "external table penalty untouched");
        });
        Test("failed ingestion and another species leave chair memories intact", () =>
        {
            var pawn = new Pawn { Map = new Map() }; var food = new Thing(); pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, food));
            pawn.needs.mood.thoughts.memories.TryGainMemory(RDE_DefOf.RDE_RatkinChairAsTable);
            Thing_Ingested_ClearPreviousChairThought.Postfix(food, pawn, 0);
            Check(pawn.needs.mood.thoughts.memories.Memories.Count == 1, "failed ingestion");
            pawn.def = new ThingDef(); Thing_Ingested_ClearPreviousChairThought.Postfix(food, pawn, .5f);
            Check(pawn.needs.mood.thoughts.memories.Memories.Count == 1, "non Ratkin");
        });
        Test("old own table memories retire on load while current chairs get correct age", () =>
        {
            var pawn = new Pawn(); pawn.ageTracker.AgeBiologicalYearsFloat = 16;
            var memories = pawn.needs.mood.thoughts.memories; var external = new ThoughtDef { defName = "RatkinTableRatkinSeatAge14To17" };
            memories.TryGainMemory(new ThoughtDef { defName = "RDE_RatkinDiningSet" });
            memories.TryGainMemory(new ThoughtDef { defName = "RDE_ToddlerAteOnChair" });
            memories.TryGainMemory(RDE_DefOf.RDE_UsedChairAsTable); memories.TryGainMemory(external);
            Scribe.mode = LoadSaveMode.PostLoadInit; Pawn_ExposeData_RetireTableThoughts.Postfix(pawn);
            Check(memories.Memories.Count == 2 && memories.GetFirstMemoryOfDef(RDE_DefOf.RDE_UsedChairAsTable).stage == 6
                && memories.GetFirstMemoryOfDef(external) != null, "own save migration only");
        });
        Test("adding a table during toddler carrying releases claims and preserves meal", () =>
        {
            var f = new Fixture(); f.Carrying(); Furniture(f.Map, 21, false, true); f.Tick(60);
            Check(!f.Request.IsActive && f.Child.Spawned && f.Helper.carryTracker.CarriedThing == null && !f.Manager.IsChairClaimed(f.Chair), "release and drop safely");
            f.Tick(120); Check(f.Child.carryTracker.CarriedThing == f.Food && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "continue held meal");
            Check(f.Child.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RDE_DefOf.RDE_ToddlerNoDiningHelp) == null, "not a help timeout");
        });
        Test("new table requests are rejected and old serialized table requests recover", () =>
        {
            var f = new Fixture(); f.Manager.Complete(f.Request); var table = Furniture(f.Map, 30, false, true);
            Check(f.Manager.Create(f.Child, f.Food, f.Chair, table, new IntVec3(19)) == null, "explicit table target rejected");
            var request = f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)); Check(request != null, "lone chair accepted");
            request.table = table; f.Child.carryTracker.CarriedThing = f.Food; f.Food.Spawned = false;
            f.Manager.SetState(request, RatToddlerDiningState.WaitingForHelp); f.Tick(60);
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "old table request retired safely");
        });
        Test("pure-chair toddler job grants biological-age memory and releases request", () =>
        {
            var f = new Fixture(); f.Waiting(); f.Chair.def.Extension = new RatDiningFurnitureExtension();
            f.Child.ageTracker.AgeBiologicalYearsFloat = 3; f.Child.Position = f.Chair.Position;
            f.Manager.SetState(f.Request, RatToddlerDiningState.PlacedAtChair);
            f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair, f.Chair), keepCarryingThingOverride: true);
            var toils = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Check(toils.Count == 6, "saved toil layout");
            Run(toils, 0, 5);
            var memory = f.Child.needs.mood.thoughts.memories.Memories.Single();
            Check(memory.def == RDE_DefOf.RDE_RatkinChairAsTable && memory.stage == 0, "youngest chair memory");
            Check(f.Manager.GetForPawn(f.Child) == null && !f.Manager.IsChairClaimed(f.Chair), "request completed");
        });
        Test("adult chair job grants only the mature chair memory after consumption", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map, Toddler = false }; pawn.ageTracker.AgeBiologicalYearsFloat = 25;
            var chair = Furniture(map, 20, true, true); var food = new Thing { Map = map };
            pawn.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, food, chair, new IntVec3(19)));
            var toils = Attach(new JobDriver_EatUsingChairAsTable(), pawn); Check(toils.Count == 8, "adult save indices");
            Run(toils, 0, 7); var memory = pawn.needs.mood.thoughts.memories.Memories.Single();
            Check(memory.def == RDE_DefOf.RDE_RatkinChairAsTable && memory.stage == 9, "mature Ratkin chair stage");
        });
        Test("old toddler table job fails reservations safely and continues its held meal", () =>
        {
            var f = new Fixture(); f.Waiting(); var table = Furniture(f.Map, 30, false, true); f.Request.table = table;
            f.Child.Position = f.Chair.Position; f.Manager.SetState(f.Request, RatToddlerDiningState.PlacedAtChair);
            f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair, table), keepCarryingThingOverride: true);
            var driver = new JobDriver_ToddlerEatAtChair { pawn = f.Child, job = f.Child.CurJob }; f.Child.jobs.curDriver = driver;
            Check(!driver.TryMakePreToilReservations(false), "legacy table target refused");
            f.Child.jobs.EndCurrentJob(JobCondition.QueuedNoLongerValid);
            Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor && f.Child.carryTracker.CarriedThing == f.Food, "constructor finalizer retains original meal");
            Check(!f.Manager.IsChairClaimed(f.Chair), "claim released");
        });
    }
}
