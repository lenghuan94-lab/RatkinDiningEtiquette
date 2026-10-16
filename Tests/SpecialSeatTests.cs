using System;
using System.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

// Type-shape doubles only; these do not execute the hygiene mod.
namespace DubsBadHygiene
{
    public class Building_toilet : Building { }
    public class Building_Latrine : Building { }
    public class Building_AdvToilet : Building_toilet { }
}
namespace TestSeatSkins { public class ReskinnedToilet : DubsBadHygiene.Building_toilet { } }

internal static partial class Program
{
    private static void SpecialSetting(int category, bool value)
    {
        switch (category)
        {
            case 0: RatDiningMod.Settings.enableColonistSpecialSeats = value; break;
            case 1: RatDiningMod.Settings.enableVisitorSpecialSeats = value; break;
            case 2: RatDiningMod.Settings.enablePrisonerSpecialSeats = value; break;
            case 3: RatDiningMod.Settings.enableSlaveSpecialSeats = value; break;
        }
    }

    private static void Identity(Pawn pawn, int category)
    {
        pawn.IsColonist = category == 0 || category == 3;
        pawn.IsPrisonerOfColony = category == 2;
        pawn.IsSlaveOfColony = category == 3;
        pawn.Faction = category == 0 || category == 3 ? Faction.OfPlayer : new Faction();
        pawn.HostFaction = category == 2 ? Faction.OfPlayer : null;
        if (category == 2)
        {
            var room = new Room { IsPrisonCell = true };
            foreach (int cell in new[] { 9, 10, 11, 19, 20, 21, 110, 119, 120 }) pawn.Map.Rooms[cell] = room;
        }
    }

    private static Building Seat(Map map, RatDiningSeatKind kind)
    {
        var seat = Furniture(map, 20, true, false);
        seat.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair";
        seat.def.Extension = null;
        return seat;
    }

    private static void SpecialSeatTests()
    {
        Test("special-seat legacy defaults do not change toddler-help defaults or master", () =>
        {
            Scribe_Values.Values["enableToddlerHelp"] = false;
            Scribe.mode = LoadSaveMode.LoadingVars; RatDiningMod.Settings.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
            var s = RatDiningMod.Settings;
            Check(!s.enableColonistSpecialSeats && !s.enableVisitorSpecialSeats && s.enablePrisonerSpecialSeats
                && s.enableSlaveSpecialSeats, "special defaults");
            Check(!s.enableToddlerHelp && s.enableColonistToddlerHelp && !s.enablePrisonerToddlerHelp
                && !s.enableSlaveToddlerHelp && !s.enableVisitorToddlerHelp, "old independent switches retained");
        });
        Test("all sixteen special-seat settings combinations survive saved fields", () =>
        {
            for (int mask = 0; mask < 16; mask++)
            {
                var s = new RatDiningSettings(); RatDiningMod.Settings = s;
                for (int category = 0; category < 4; category++) SpecialSetting(category, (mask & (1 << category)) != 0);
                Scribe.mode = LoadSaveMode.Saving; s.ExposeData();
                RatDiningMod.Settings = new RatDiningSettings();
                Scribe.mode = LoadSaveMode.LoadingVars; RatDiningMod.Settings.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
                var map = new Map(); var pawn = new Pawn { Map = map }; var seat = Seat(map, RatDiningSeatKind.Toilet);
                for (int category = 0; category < 4; category++)
                { Identity(pawn, category); Check(RatDiningFurniture.AllowedFor(pawn, seat) == ((mask & (1 << category)) != 0), "saved identity selection"); }
            }
        });
        Test("toilet recognition handles named sittable fixtures and derived native classes", () =>
        {
            var map = new Map();
            foreach (string name in new[] { "ToiletStuff", "PitLatrine", "TinyPotty", "TOILET_Reskin" })
            {
                var seat = Furniture(map, 20, true, true); seat.def.defName = name;
                Check(RatDiningFurniture.Kind(seat) == RatDiningSeatKind.Toilet && !RatDiningFurniture.IsRatkinScale(seat), "toilet outranks chair extension");
            }
            var custom = new TestSeatSkins.ReskinnedToilet(); custom.def.building.isSittable = false;
            Check(RatDiningFurniture.IsDiningSeat(custom) && RatDiningFurniture.Kind(custom) == RatDiningSeatKind.Toilet, "inherited toilet with custom name");
            custom.def.thingClass = typeof(DubsBadHygiene.Building_Latrine);
            Check(RatDiningFurniture.IsKnownToilet(custom), "resolved native ThingDef class");
        });
        Test("exact hygiene definitions support non-sittable toilets and exclude bathroom doors", () =>
        {
            var seat = new Building(); seat.def.building.isSittable = false;
            seat.def.modContentPack = new ModContentPack { PackageIdPlayerFacing = "dubwise.dubsbadhygiene" };
            foreach (string name in new[] { "PitLatrine", "ToiletStuff", "ToiletAdv", "ToiletAdvStuff", "ToiletSpacer" })
            { seat.def.defName = name; Check(RatDiningFurniture.IsDiningSeat(seat), "actual hygiene definition"); }
            foreach (string name in new[] { "ToiletStallDoor", "WashBucket", "ToiletFactory" })
            { seat.def.defName = name; Check(!RatDiningFurniture.IsDiningSeat(seat), "not a dining fixture"); }
            seat.def.defName = "ToiletStuff"; seat.def.modContentPack.PackageIdPlayerFacing = "other.mod";
            Check(!RatDiningFurniture.IsDiningSeat(seat), "non-seat names alone never opt in");
        });
        Test("special functional seats retain their own classification", () =>
        {
            var seat = Seat(new Map(), RatDiningSeatKind.SpecialSeat);
            foreach (string name in new[] { "PL_InterrogationChair", "GrandThrone", "TortureSeat", "BathSeat" })
            { seat.def.defName = name; Check(RatDiningFurniture.Kind(seat) == RatDiningSeatKind.SpecialSeat, "functional seat"); }
            seat.def.Extension = new RatDiningFurnitureExtension { diningKind = RatDiningSeatKind.SpecialSeat };
            seat.def.defName = "CustomFunctionalSeat";
            Check(!RatDiningFurniture.IsRatkinScale(seat), "functional extension cannot yield little-chair reward");
        });
        Test("slave identity takes precedence over overlapping colonist and captive flags", () =>
        {
            var f = new PrisonFixture(); f.Child.IsSlaveOfColony = true; f.Child.IsColonist = true;
            RatDiningMod.Settings.enableSlaveSpecialSeats = false; RatDiningMod.Settings.enableColonistSpecialSeats = true;
            Check(!RatDiningFurniture.AllowedFor(f.Child, Seat(f.Map, RatDiningSeatKind.Toilet)), "slave special switch");
            RatDiningMod.Settings.enableSlaveToddlerHelp = false;
            Check(!RatDiningCustody.CanRequestHelp(f.Child), "slave help switch");
        });
        foreach (RatDiningSeatKind kind in new[] { RatDiningSeatKind.Toilet, RatDiningSeatKind.SpecialSeat })
        {
            Test(kind + " finder defaults permit prisoners and slaves and reject colonists and visitors", () =>
            {
                for (int category = 0; category < 4; category++)
                {
                    var map = new Map(); var pawn = new Pawn { Map = map, Toddler = false }; Identity(pawn, category);
                    var seat = Seat(map, kind);
                    Check(RatDiningSpotFinder.TryFindChairAsTable(pawn, out var found, out _, out _) == (category >= 2), "defaults at finder");
                    if (category >= 2) Check(found == seat, "same seat");
                    SpecialSetting(category, category < 2);
                    Check(RatDiningSpotFinder.TryFindChairAsTable(pawn, out _, out _, out _) == (category < 2), "toggle at finder");
                    SpecialSetting(category, category >= 2);
                }
            });
            Test(kind + " settings apply to adult and both toddler eligibility switches", () =>
            {
                for (int category = 0; category < 4; category++) foreach (bool toddler in new[] { false, true })
                {
                    var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
                    var pawn = new Pawn { Map = map, Toddler = toddler }; Identity(pawn, category); var food = new Thing { Map = map };
                    Seat(map, kind); SetCategory(category, true); SpecialSetting(category, true);
                    Check(RatDiningModeResolver.TryResolve(pawn, food, out var mode, out _, out _, out _)
                        && mode == (toddler ? RatDiningMode.ToddlerNeedsAssistance : RatDiningMode.ChairAsTable), "enabled category entrance");
                    SpecialSetting(category, false);
                    RatDiningModeResolver.TryResolve(pawn, food, out mode, out _, out _, out _);
                    Check(mode == RatDiningMode.FloorFallback, "special-seat category denied");
                    SpecialSetting(category, true);
                    if (toddler) { SetCategory(category, false); RatDiningModeResolver.TryResolve(pawn, food, out mode, out _, out _, out _); Check(mode == RatDiningMode.FloorFallback, "help permission also required"); }
                }
            });
            Test(kind + " normal chairs stay eligible and outrank a closer functional seat", () =>
            {
                var f = new PrisonFixture(false); f.Chair.Position = new IntVec3(21); var seat = Seat(f.Map, kind);
                Check(RatDiningSpotFinder.TryFindChairAsTable(f.Child, out var found, out _, out _) && found == f.Chair, "normal chair preferred");
                RatDiningMod.Settings.enablePrisonerSpecialSeats = false;
                Check(RatDiningSpotFinder.IsUsableChair(f.Child, f.Chair) && !RatDiningSpotFinder.IsUsableChair(f.Child, seat), "normal independent of special flag");
            });
            Test(kind + " cannot evade prison rooms, forbidden seats, reservations or adjacent tables", () =>
            {
                var f = new PrisonFixture(false); var seat = Seat(f.Map, kind);
                f.Map.Rooms[20] = new Room(); Check(!RatDiningSpotFinder.IsUsableChair(f.Child, seat), "outside original prison");
                f.Map.Rooms[20] = f.Prison;
                seat.Forbidden = true; Check(!RatDiningSpotFinder.IsUsableChair(f.Child, seat), "forbidden"); seat.Forbidden = false;
                f.Child.CanReserveTargets = false; Check(!RatDiningSpotFinder.IsUsableChair(f.Child, seat), "reserved"); f.Child.CanReserveTargets = true;
                var table = Furniture(f.Map, 21, false, false); table.Forbidden = true;
                Check(!RatDiningSpotFinder.IsUsableChair(f.Child, seat), "even forbidden adjacent table excludes this mod");
            });
            Test(kind + " full adult meal grants its own memory only after finalization", () =>
            {
                var f = new PrisonFixture(false); f.Chair.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair"; f.Chair.def.Extension = null;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, f.Food, f.Chair, new IntVec3(19)));
                f.Child.CurJob.count = 2;
                var driver = new JobDriver_EatUsingChairAsTable(); var toils = Attach(driver, f.Child);
                toils[7].initAction(); Check(f.Child.needs.mood.thoughts.memories.Memories.Count == 0, "no evaluation without a completed meal");
                Run(toils, 0, 7);
                Check(f.Child.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RatDiningThoughts.ForChair(f.Chair)) != null, "new seat memory");
                Check(f.Child.needs.mood.thoughts.memories.Memories.All(m => m.def != RDE_DefOf.RDE_RatkinChairAsTable), "no ordinary positive memory");
            });
            Test(kind + " caregiver-first entrance completes for every recipient identity", () =>
            {
                for (int category = 0; category < 4; category++)
                {
                    var f = new Fixture(); f.Manager.Complete(f.Request); Identity(f.Child, category);
                    f.Child.ageTracker.AgeBiologicalYearsFloat = 2; f.Child.DevelopmentalStage = DevelopmentalStage.Baby;
                    f.Child.needs.food.CurLevelPercentage = .2f;
                    if (category == 1) { f.Helper.Faction = f.Child.Faction; f.Helper.IsColonist = false; f.Helper.workSettings = null; }
                    SetCategory(category, true); SpecialSetting(category, true);
                    f.Chair.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair";
                    f.Map.Buildings.Add(f.Chair);
                    f.Child.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait), JobCondition.InterruptForced);
                    f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                    var request = f.Manager.GetForPawn(f.Child);
                    Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && request != null
                        && request.chair == f.Chair, "provider uses eater identity, not helper defaults");
                    var driver = new JobDriver_ProvideRatToddlerDiningMeal(); var toils = Attach(driver, f.Helper);
                    foreach (Toil toil in toils) { Check(!driver.Failures.Any(fail => fail()), "valid native-shaped provision guards"); toil.initAction?.Invoke(); }
                    var eat = new JobDriver_ToddlerEatAtChair(); var meal = Attach(eat, f.Child);
                    Run(meal, 0, 3); Check(!eat.Failures.Any(fail => fail()), "food consumption does not invalidate successful meal"); Run(meal, 4, 5);
                    var memory = f.Child.needs.mood.thoughts.memories.GetFirstMemoryOfDef(RatDiningThoughts.ForChair(f.Chair));
                    Check(memory != null && memory.stage == 0 && f.Child.needs.food.CurLevelPercentage == 1f
                        && !request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "baby meal, own text, nutrition and release");
                }
            });
            Test(kind + " caregiver-first entrance leaves native feeding available when recipient switch is off", () =>
            {
                for (int category = 0; category < 4; category++)
                {
                    var f = new Fixture(); f.Manager.Complete(f.Request); Identity(f.Child, category);
                    if (category == 1) { f.Helper.Faction = f.Child.Faction; f.Helper.IsColonist = false; f.Helper.workSettings = null; }
                    SetCategory(category, true); SpecialSetting(category, false);
                    f.Chair.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair"; f.Map.Buildings.Add(f.Chair);
                    f.Child.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait), JobCondition.InterruptForced);
                    f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                    Check(f.Helper.CurJobDef == JobDefOf.BottleFeedBaby && f.Manager.GetForPawn(f.Child) == null
                        && f.Food.stackCount == 10 && !f.Manager.IsChairClaimed(f.Chair), "no claims or food changes on rejected provider path");
                }
            });
            Test(kind + " active adult guard observes a switch change and keeps held food", () =>
            {
                var f = new PrisonFixture(false); f.Chair.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair"; f.Chair.def.Extension = null;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, f.Food, f.Chair, new IntVec3(19)));
                f.Child.CurJob.count = 2;
                var driver = new JobDriver_EatUsingChairAsTable(); var toils = Attach(driver, f.Child); Run(toils, 0, 1);
                Thing held = f.Child.carryTracker.CarriedThing;
                RatDiningMod.Settings.enablePrisonerSpecialSeats = false;
                Check(driver.Failures.Any(fail => fail()), "setting invalidates active custom driver");
                Check(held != null && f.Child.carryTracker.CarriedThing == held && held == f.Child.CurJob.targetA.Thing, "original split food retained for vanilla handoff");
                toils[7].initAction(); Check(f.Child.needs.mood.thoughts.memories.Memories.Count == 0, "no uncompleted memory");
            });
            Test(kind + " disabling waiting releases the seat and preserves the same food", () =>
            {
                var f = new PrisonFixture(); f.Chair.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair"; f.Chair.def.Extension = null;
                var request = f.Waiting(); RatDiningMod.Settings.enablePrisonerSpecialSeats = false;
                Check(!f.Manager.CanResumeRequest(request) && !RatDiningCustody.ValidRequest(request), "immediately invalidated");
                f.Tick(60); f.Tick(120);
                Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair) && f.Child.carryTracker.CarriedThing == f.Food
                    && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "floor recovery holds original meal");
                Check(f.Child.needs.mood.thoughts.memories.Memories.Count == 0, "setting change is not a timeout or toilet meal");
            });
            Test(kind + " disabling during carry safely drops the child and releases helper claims", () =>
            {
                var f = new Fixture(); SpecialSetting(0, true); f.Chair.def.defName = kind == RatDiningSeatKind.Toilet ? "ToiletStuff" : "InterrogationChair";
                f.Carrying(); SpecialSetting(0, false); f.Tick(60); f.Tick(120);
                Check(f.Child.Spawned && f.Helper.carryTracker.CarriedThing != f.Child && !f.Request.IsActive
                    && !f.Manager.IsChairClaimed(f.Chair), "drop and release");
                Check(f.Child.carryTracker.CarriedThing == f.Food && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "same carried food");
            });
            Test(kind + " mood repeats are capped, age-specific, deduplicated and survive load", () =>
            {
                foreach (float years in new[] { 2f, 8f, 16f, 25f })
                {
                    var map = new Map(); var pawn = new Pawn { Map = map }; var seat = Seat(map, kind); pawn.ageTracker.AgeBiologicalYearsFloat = years;
                    for (int meal = 0; meal < 5; meal++)
                    {
                        var memory = ChairMeal(pawn, seat); int layers = Math.Min(3, meal + 1);
                        Check(memory.repetitions == layers && memory.stage == RatDiningThoughts.AgeBand(pawn) * 3 + layers - 1
                            && OwnDiningMood(pawn) == -layers, "own capped negative total");
                        RatDiningThoughts.ApplyChair(pawn, seat); Check(memory == pawn.needs.mood.thoughts.memories.Memories.Single(), "same-job deduplication");
                    }
                    var previous = pawn.needs.mood.thoughts.memories.Memories.Single(); previous.age = 2300;
                    RatDiningThoughts.RefreshLoadedMemories(pawn);
                    var loaded = (Thought_Memory_RatDining)pawn.needs.mood.thoughts.memories.Memories.Single();
                    Check(loaded.repetitions == 3 && loaded.age == 2300 && loaded.def == previous.def, "load retains seat type, count, lifetime");
                    var foreign = new ThoughtDef { defName = "OtherMod.ToiletMood" }; pawn.needs.mood.thoughts.memories.TryGainMemory(foreign);
                    RatDiningThoughts.ClearChair(pawn); Check(pawn.needs.mood.thoughts.memories.Memories.Single().def == foreign, "foreign hygiene memories untouched");
                }
            });
        }
        Test("adult meal finalization does not reward a destroyed chair or a newly attached table", () =>
        {
            foreach (bool destroyed in new[] { false, true })
            {
                var f = new PrisonFixture(false);
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, f.Food, f.Chair, new IntVec3(19)));
                f.Child.CurJob.count = 2; var toils = Attach(new JobDriver_EatUsingChairAsTable(), f.Child); Run(toils, 0, 6);
                if (destroyed) f.Chair.Destroyed = true; else Furniture(f.Map, 21, false, false);
                toils[7].initAction(); Check(f.Child.needs.mood.thoughts.memories.Memories.Count == 0, "invalid destination never gains a seat memory");
            }
        });
        Test("changing toilet, special seat and normal chair resets repetitions", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var toilet = Seat(map, RatDiningSeatKind.Toilet);
            var special = Seat(map, RatDiningSeatKind.SpecialSeat); var chair = Furniture(map, 30, true, false);
            ChairMeal(pawn, toilet); ChairMeal(pawn, toilet);
            Check(ChairMeal(pawn, special).repetitions == 1 && ChairMeal(pawn, chair).repetitions == 1, "separate memory classes");
            Check(ChairMeal(pawn, toilet).repetitions == 1, "switch back is a new sequence");
        });
        Test("legacy colonist toilet requests are cancelled safely with missing new settings keys", () =>
        {
            var f = new Fixture(); RatDiningMod.Settings.enableColonistSpecialSeats = true;
            f.Chair.def.defName = "ToiletStuff"; f.Waiting();
            Scribe.mode = LoadSaveMode.LoadingVars; RatDiningMod.Settings.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
            Check(!RatDiningMod.Settings.enableColonistSpecialSeats && !f.Manager.CanResumeRequest(f.Request)
                && !RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(f.Child), "old request respects new default before map tick");
            f.Tick(60); f.Tick(120);
            Check(!f.Request.IsActive && f.Child.carryTracker.CarriedThing == f.Food
                && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "old save keeps its original food");
        });
        Test("disabling toilet dining after caregiver pickup releases the split food and all claims", () =>
        {
            var f = new PrisonFixture(); f.Chair.def.defName = "ToiletStuff";
            f.Child.needs.food.CurLevelPercentage = .2f;
            var giver = new WorkGiver_Warden_AssistRatToddlerDining();
            f.Warden.jobs.StartJob(giver.JobOnThing(f.Warden, f.Child));
            var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Warden); Run(toils, 0, 2);
            var request = f.Manager.GetForPawn(f.Child); Thing meal = request.food;
            Check(meal != f.Food && f.Warden.inventory.Contains(meal), "provider holds original split portion");
            RatDiningMod.Settings.enablePrisonerSpecialSeats = false;
            Check(!RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(f.Child), "ordinary feeding is available immediately");
            f.Tick(60); f.Tick(120);
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair) && !f.Warden.inventory.Contains(meal)
                && (meal.Spawned || f.Child.carryTracker.CarriedThing == meal), "same split food released for recovery");
            Check(!f.Map.reservationManager.Reservations.Any(r => r.Job == f.Warden.CurJob), "helper reservations released");
        });
        Test("special toddler moods expose letter babbling and keep translated description separate", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; pawn.ageTracker.AgeBiologicalYearsFloat = 2;
            foreach (RatDiningSeatKind kind in new[] { RatDiningSeatKind.Toilet, RatDiningSeatKind.SpecialSeat })
            {
                var seat = Seat(map, kind);
                for (int meal = 0; meal < 3; meal++)
                { var memory = ChairMeal(pawn, seat); Check(memory.BabyTalk.StartsWith("RDE_BabyTalk_" + kind + "_"), "dedicated toddler babbling channel"); }
            }
        });
    }
}
