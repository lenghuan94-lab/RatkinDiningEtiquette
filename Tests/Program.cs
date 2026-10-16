using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static int passed;

    private sealed class Fixture
    {
        public readonly Map Map = new Map();
        public readonly Pawn Child = new Pawn();
        public readonly Pawn Helper = new Pawn { Toddler = false };
        public readonly Building Chair = new Building { Position = new IntVec3(20) };
        public readonly Thing Food = new Thing { stackCount = 10 };
        public MapComponent_RatToddlerDining Manager => Map.Manager;
        public readonly RatToddlerDiningRequest Request;
        public Fixture()
        {
            Child.Map = Helper.Map = Chair.Map = Food.Map = Map;
            Map.Manager = new MapComponent_RatToddlerDining(Map);
            Request = Manager.Create(Child, Food, Chair, null, new IntVec3(19));
            Check(Request != null, "fixture request");
            Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerPrepareDiningRequest, Food, Chair, Request.stagingCell));
        }
        public void Waiting()
        {
            Child.carryTracker.CarriedThing = Food; Food.Spawned = false;
            Manager.SetState(Request, RatToddlerDiningState.WaitingForHelp);
        }
        public void Claim()
        {
            Waiting();
            Helper.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, Child, Chair));
            Check(Manager.TryClaim(Request, Helper), "claim");
        }
        public void Carrying()
        {
            Claim(); Manager.SetState(Request, RatToddlerDiningState.BeingCarried);
            Helper.carryTracker.Take(Child, 1);
        }
        public void Tick(int tick) { Find.TickManager.TicksGame = tick; Manager.MapComponentTick(); }
    }

    private static void Check(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }

    private static void Test(string name, Action action)
    {
        Find.TickManager.TicksGame = 0;
        RatDiningMod.Settings = new RatDiningSettings();
        Scribe.mode = LoadSaveMode.Inactive;
        Scribe_Values.Values.Clear();
        Log.Errors.Clear();
        Log.Messages.Clear();
        TestFurniture.Chair = null;
        FoodUtility.BabyFood = null;
        Toddlers.BabyMoveUtility.TestBestPlace = LocalTargetInfo.Invalid;
        ModsConfig.TableMannersActive = false;
        Toils_Ingest.TestDiningCell = IntVec3.Invalid;
        Toils_Ingest.TestUseStandingSelector = false;
        Toils_Ingest.TestCarryAction = null;
        Toils_Ingest.TestFinderAction = null;
        Toils_Ingest.TestFinalizeAction = null;
        RCellFinder.TestSpot = IntVec3.Invalid;
        MoteMaker.Count = 0;
        MoteMaker.LastText = null;
        TranslationExtensions.Translations.Clear();
        action(); passed++; Console.WriteLine("PASS " + name);
    }

    private static List<Toil> Attach(JobDriver driver, Pawn pawn)
    {
        driver.pawn = pawn; driver.job = pawn.CurJob;
        pawn.jobs.curDriver = driver;
        Check(driver.TryMakePreToilReservations(false), driver.GetType().Name + " reservations");
        return driver.TestToils();
    }

    private static void Run(List<Toil> toils, int first, int last)
    { for (int i = first; i <= last; i++) toils[i].initAction?.Invoke(); }

    private static void LoadDefs()
    {
        foreach (var element in XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ThoughtDefs.xml")).Root.Elements())
        {
            var thought = new ThoughtDef { defName = element.Element("defName").Value,
                thoughtClass = Type.GetType(element.Element("thoughtClass").Value) ?? typeof(Thought_Memory),
                descriptions = element.Element("stages").Elements().Select(s => s.Element("description").Value).ToList(),
                stages = element.Element("stages").Elements().Select(s => float.Parse(s.Element("baseMoodEffect").Value,
                    System.Globalization.CultureInfo.InvariantCulture)).ToList() };
            typeof(RDE_DefOf).GetField(thought.defName).SetValue(null, thought);
        }
        foreach (var element in XDocument.Load(Path.Combine(AppContext.BaseDirectory, "JobDefs.xml")).Root.Elements())
        {
            var def = new JobDef { defName = element.Element("defName").Value };
            foreach (string name in new[] { "carryThingAfterJob", "dropThingBeforeJob", "casualInterruptible", "suspendable", "playerInterruptible" })
            {
                var value = element.Element(name);
                if (value != null) typeof(JobDef).GetField(name).SetValue(def, bool.Parse(value.Value));
            }
            typeof(RDE_DefOf).GetField(def.defName).SetValue(null, def);
        }
    }

    private static int Main()
    {
        LoadDefs();
        try
        {
            FurnitureTests();
            SpecialSeatTests();
            RepeatedDiningTests();
            SpeechTests();
            TableMannersCompatibilityTests();
            CustodyAndSafetyTests();
            PrisonerMealSelectionTests();
            VisitorAndSlaveTests();
            ToddlerCategorySettingsTests();
            CarryRecoveryTests();
            MealFinalizationTests();
            ToddlerCompletionTests();
            Test("pickup -> waiting -> carried -> chair meal", () =>
            {
                var f = new Fixture(); f.Child.CurJob.count = 4; f.Request.foodCount = 4;
                var prepare = new JobDriver_ToddlerPrepareDiningRequest();
                var prepToils = Attach(prepare, f.Child);
                Check(prepToils.Count == 6, "original prepare save indices");
                Run(prepToils, 0, 2);
                Check(f.Request.food != f.Food && f.Food.stackCount == 6, "split stack reference must update before walking");
                Check(f.Request.foodCount == 4, "raw food quantity must survive pickup");
                Run(prepToils, 3, 4);
                Check(f.Request.IsAvailable, "request available after arrival");
                f.Helper.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, f.Child, f.Chair));
                var assist = new JobDriver_AssistRatToddlerDining();
                var assistToils = Attach(assist, f.Helper);
                Check(assistToils.Count == 7, "original helper save indices");
                Run(assistToils, 0, 3);
                Check(!f.Child.Spawned && f.Helper.carryTracker.CarriedThing == f.Child, "child picked up");
                Check(RatToddlerDiningUtility.HoldsFood(f.Request), "meal must survive waiting cleanup and Carried start");
                Check(f.Request.state == RatToddlerDiningState.BeingCarried, "handoff must not cancel request");
                Run(assistToils, 4, 6);
                Check(f.Child.Position == f.Chair.Position && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair, "chair handoff");
                var eat = new JobDriver_ToddlerEatAtChair();
                var eatToils = Attach(eat, f.Child);
                Run(eatToils, 0, 3);
                Check(!eat.Failures.Any(fail => fail()), "consumed food must not abort successful meal");
                Run(eatToils, 4, 5);
                Check(f.Child.needs.food.CurLevelPercentage == 1f, "meal consumed");
                Check(f.Manager.GetForPawn(f.Child) == null && !f.Manager.IsChairClaimed(f.Chair), "meal releases claims");
            });
            Test("resumed pickup does not pick up held food twice", () =>
            {
                var f = new Fixture(); f.Waiting();
                var toils = Attach(new JobDriver_ToddlerPrepareDiningRequest(), f.Child);
                Run(toils, 0, 2);
                Check(f.Child.carryTracker.CarriedThing == f.Food, "already held food kept");
            });
            Test("inventory food can prepare a request", () =>
            {
                var f = new Fixture(); f.Food.Spawned = false; f.Child.inventory.Items.Add(f.Food);
                var toils = Attach(new JobDriver_ToddlerPrepareDiningRequest(), f.Child);
                Run(toils, 0, 2);
                Check(RatToddlerDiningUtility.HoldsFood(f.Request), "inventory food transferred");
            });
            Test("retired cleanup cannot erase replacement pawn or chair ownership", () =>
            {
                var f = new Fixture(); f.Manager.Complete(f.Request);
                var replacement = f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
                Check(replacement != null, "replacement request"); f.Tick(60);
                Check(f.Manager.GetForPawn(f.Child) == replacement && f.Manager.IsChairClaimed(f.Chair), "replacement owns indexes");
                f.Manager.Complete(f.Request);
                Check(f.Manager.GetForPawn(f.Child) == replacement, "late completion cannot touch new request");
            });
            Test("a stale job binds only to its original request", () =>
            {
                var f = new Fixture(); int id = -1;
                Check(RatToddlerDiningUtility.GetBoundRequest(f.Manager, f.Child, ref id) == f.Request, "initial binding");
                f.Manager.Complete(f.Request);
                f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
                Check(RatToddlerDiningUtility.GetBoundRequest(f.Manager, f.Child, ref id) == null, "stale driver excluded");
            });
            Test("one chair cannot serve two active requests", () =>
            {
                var f = new Fixture(); var child2 = new Pawn { Map = f.Map };
                Check(f.Manager.Create(child2, f.Food, f.Chair, null, new IntVec3(19)) == null, "chair contention");
            });
            Test("two caregivers cannot claim one request", () =>
            {
                var f = new Fixture(); f.Claim(); var other = new Pawn { Map = f.Map, Toddler = false };
                other.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, f.Child, f.Chair));
                Check(!f.Manager.TryClaim(f.Request, other), "duplicate helper refused");
            });
            Test("both parents qualify when non-parents are disabled", () =>
            {
                var f = new Fixture(); f.Waiting(); RatDiningMod.Settings.allowNonParents = false;
                var first = new Pawn { Map = f.Map, Toddler = false };
                f.Child.relations.Parents.Add(first); f.Child.relations.Parents.Add(f.Helper);
                var giver = new WorkGiver_AssistRatToddlerDining();
                Check(giver.HasJobOnThing(first, f.Child) && giver.HasJobOnThing(f.Helper, f.Child), "both parents offered work");
                Check(giver.Prioritized, "parent/hunger work priorities enabled");
                Check(!giver.HasJobOnThing(new Pawn { Map = f.Map, Toddler = false }, f.Child), "non-parent excluded");
            });
            Test("timeout is finite, idempotent and recovers outside cleanup", () =>
            {
                var f = new Fixture(); f.Waiting(); int starts = f.Child.jobs.Starts;
                f.Tick(1200);
                Check(f.Request.state == RatToddlerDiningState.TimedOut, "finite wait");
                Check(f.Child.jobs.Starts == starts, "no nested job start in failure");
                f.Manager.StartFloorFallback(f.Request, true);
                Check(f.Child.needs.mood.thoughts.memories.Memories.Count == 1, "one failure memory");
                Check(f.Manager.IsCoolingDown(f.Child) && !f.Manager.IsChairClaimed(f.Chair), "cooldown and release");
                f.Tick(1260);
                Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "held food recovery");
                var floor = new JobDriver_ToddlerEatHeldFoodOnFloor(); var toils = Attach(floor, f.Child);
                Check(toils.Count == 5, "original floor save indices"); Run(toils, 0, 4);
                Check(!floor.Failures.Any(fail => fail()), "successful floor consumption");
                Check(f.Child.needs.mood.thoughts.memories.Memories.Count(m => m.def == RDE_DefOf.RDE_ToddlerNoDiningHelp) == 1,
                    "floor job does not duplicate failure memory");
                Check(f.Child.needs.mood.thoughts.memories.GetFirstMemoryOfDef(ThoughtDefOf.AteWithoutTable) != null,
                    "floor meal keeps its separate vanilla table penalty");
            });
            Test("waiting timeout starts on arrival, not request creation", () =>
            {
                var f = new Fixture(); Find.TickManager.TicksGame = 900; f.Waiting(); f.Tick(1500);
                Check(f.Request.IsActive, "travel time not counted as help wait");
            });
            Test("broken furniture adds no nobody-helped memory", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Chair.Spawned = false; f.Tick(60);
                Check(!f.Request.IsActive && f.Child.needs.mood.thoughts.memories.Memories.Count == 0, "non-timeout failure");
                f.Tick(120); Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "furniture failure recovery");
            });
            Test("critical hunger while carried safely drops and feeds child", () =>
            {
                var f = new Fixture(); f.Carrying(); f.Child.needs.food.CurLevelPercentage = .01f; f.Tick(60);
                Check(f.Child.Spawned && f.Helper.carryTracker.CarriedThing == null, "safe drop");
                Check(!f.Request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "release during carry");
                f.Tick(120); Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "urgent meal");
            });
            Test("chair destroyed while carried does not strand child", () =>
            {
                var f = new Fixture(); f.Carrying(); f.Chair.Spawned = false; f.Tick(60);
                Check(f.Child.Spawned && !f.Request.IsActive, "destroyed chair during carry");
            });
            Test("caregiver interruption reopens request and resumes waiting", () =>
            {
                var f = new Fixture(); f.Carrying(); f.Helper.Drafted = true; f.Tick(60);
                Check(f.Child.Spawned && f.Request.state == RatToddlerDiningState.WaitingForHelp, "request reopened");
                f.Tick(120); Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerWaitForDiningHelp, "waiting restored after Carried");
                f.Tick(180);
                var second = new Pawn { Map = f.Map, Toddler = false };
                second.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, f.Child, f.Chair));
                Check(f.Manager.TryClaim(f.Request, second), "second helper can take over");
            });
            Test("repeated helper failures are bounded", () =>
            {
                var f = new Fixture(); f.Waiting(); for (int i = 0; i < 4; i++) f.Manager.Reopen(f.Request, false);
                Check(!f.Request.IsActive && f.Manager.IsCoolingDown(f.Child), "retry cap");
            });
            Test("player interruption releases request without replacing emergency job", () =>
            {
                var f = new Fixture(); f.Waiting(); Attach(new JobDriver_ToddlerPrepareDiningRequest(), f.Child);
                var emergency = new JobDef { defName = "RescueOrPlayerOrder" };
                f.Child.jobs.StartJob(JobMaker.MakeJob(emergency), JobCondition.InterruptForced);
                f.Tick(60);
                Check(f.Child.CurJobDef == emergency && !f.Request.IsActive, "player job allowed");
            });
            Test("failed reservations retire the prepared request immediately", () =>
            {
                var f = new Fixture(); f.Child.CanReserveTargets = false;
                var driver = new JobDriver_ToddlerPrepareDiningRequest { pawn = f.Child, job = f.Child.CurJob };
                Check(!driver.TryMakePreToilReservations(false), "failed reservations");
                Check(!f.Request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "no orphan request");
            });
            Test("ground food uses vanilla recovery instead of held-food-only job", () =>
            {
                var f = new Fixture(); f.Manager.StartFloorFallback(f.Request, false); f.Tick(60);
                Check(f.Child.CurJobDef == JobDefOf.Ingest, "ground food goes to vanilla ingestion");
            });
            Test("disabled assistance cancels an active request", () =>
            {
                var f = new Fixture(); f.Waiting(); RatDiningMod.Settings.enableToddlerHelp = false; f.Tick(60);
                Check(!f.Request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "settings respected mid-request");
            });
            Test("unsafe child and missing food cannot remain waiting", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Child.Downed = true; f.Tick(60);
                Check(!f.Request.IsActive, "downed child exits");
                var other = new Fixture(); other.Waiting(); other.Food.Destroyed = true; other.Tick(120);
                Check(!other.Request.IsActive, "missing food exits");
            });
            Test("late helper cleanup still drops child after request release", () =>
            {
                var f = new Fixture(); f.Claim();
                var driver = new JobDriver_AssistRatToddlerDining { pawn = f.Helper, job = f.Helper.CurJob };
                driver.TestToils(); f.Manager.SetState(f.Request, RatToddlerDiningState.BeingCarried);
                f.Helper.carryTracker.Take(f.Child, 1); f.Helper.carryTracker.DropFails = true;
                f.Manager.StartFloorFallback(f.Request, false);
                Check(f.Manager.GetForPawn(f.Child) == null, "request released before cleanup");
                f.Helper.carryTracker.DropFails = false; driver.Cleanup(JobCondition.InterruptForced);
                Check(f.Child.Spawned && f.Helper.carryTracker.CarriedThing == null, "cleanup still knows job target");
            });
            Test("manual chair reassignment is unavailable once claimed", () =>
            {
                var f = new Fixture(); f.Waiting(); var newChair = new Building { Map = f.Map };
                Check(f.Manager.TryReassignChair(f.Request, newChair, new IntVec3(11)), "available request can reassign");
                Check(f.Child.CurJob.targetB.Thing == newChair && !f.Manager.IsChairClaimed(f.Chair), "targets updated atomically");
                f.Helper.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_AssistRatToddlerDining, f.Child, newChair));
                Check(f.Manager.TryClaim(f.Request, f.Helper), "new chair claimed");
                Check(!f.Manager.TryReassignChair(f.Request, f.Chair, new IntVec3(19)), "claimed chair immutable");
            });
            Test("loaded duplicate requests cannot corrupt restored indexes", () =>
            {
                var f = new Fixture(); f.Waiting();
                var duplicate = new RatToddlerDiningRequest { id = 500, toddler = f.Child, food = f.Food, chair = f.Chair };
                ((List<RatToddlerDiningRequest>)f.Manager.Requests).Add(duplicate);
                Scribe.mode = LoadSaveMode.PostLoadInit; f.Manager.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
                f.Tick(60);
                Check(f.Manager.GetForPawn(f.Child) == f.Request && f.Manager.IsChairClaimed(f.Chair), "restored owner survives duplicate cleanup");
                f.Manager.Complete(f.Request); var replacement = f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
                Check(replacement.id > 500, "restored next ID monotonic");
            });
            Test("Carried meal preservation is gated to our active handoff", () =>
            {
                var f = new Fixture(); f.Waiting();
                f.Child.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Carried), JobCondition.InterruptForced);
                Check(f.Child.carryTracker.CarriedThing == null, "unrelated carry uses default drop behavior");
                var other = new Fixture(); other.Carrying();
                Check(RatToddlerDiningUtility.HoldsFood(other.Request), "own handoff preserves meal");
            });
            Test("non-Ratkin and non-self-feeding babies never create requests", () =>
            {
                var f = new Fixture(); var human = new Pawn { Map = f.Map, def = new ThingDef() };
                Check(f.Manager.Create(human, f.Food, new Building { Map = f.Map }, null, new IntVec3(9)) == null, "human excluded");
                var baby = new Pawn { Map = f.Map, SelfFeeding = false };
                Check(f.Manager.Create(baby, f.Food, new Building { Map = f.Map }, null, new IntVec3(9)) == null, "non-self-feeding baby excluded");
            });
            Test("safe dining defers routine relocation but allows urgent care", () =>
            {
                var f = new Fixture(); f.Waiting();
                Func<bool> busy = () =>
                {
                    object[] args = { f.Child, false };
                    typeof(ToddlerUtility_IsBabyBusy_DiningRequest).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
                    return (bool)args[1];
                };
                Check(busy(), "safe request is busy for routine relocation");
                f.Child.UnsafeTemperature = true; Check(!busy(), "temperature rescue allowed");
                f.Child.UnsafeTemperature = false; f.Child.MedicalRest = true; Check(!busy(), "medical care allowed");
                f.Child.MedicalRest = false; f.Child.needs.rest.CurLevelPercentage = .01f; Check(!busy(), "urgent sleep allowed");
                f.Child.needs.rest.CurLevelPercentage = .5f; f.Child.needs.food.CurLevelPercentage = .01f;
                Check(!busy(), "urgent feeding allowed");
            });
            Test("reported caregiver reservation coexists with a toddler's meal", () =>
            {
                var f = new Fixture(); f.Food.stackCount = 23; f.Child.CurJob.count = 5;
                Job bottleFeed = JobMaker.MakeJob(new JobDef { defName = "BottleFeedBaby" }, new Pawn { Map = f.Map }, f.Food);
                f.Helper.jobs.StartJob(bottleFeed);
                Check(f.Helper.Reserve(f.Food, bottleFeed, 10, 2), "reported caregiver reservation");
                Check(!f.Child.CanReserve(f.Food, 1, -1), "old whole-stack rule reproduces the reported conflict");
                var driver = new JobDriver_ToddlerPrepareDiningRequest { pawn = f.Child, job = f.Child.CurJob };
                Check(driver.TryMakePreToilReservations(true), "remaining portions are shared normally");
                Check(f.Map.reservationManager.Reservations.Any(r => r.Pawn == f.Child && r.MaxPawns == 10 && r.Count == 5), "reserve meal quantity only");
                Check(f.Helper.CurJob == bottleFeed && Log.Errors.Count == 0, "caregiver job remains intact without red error");
            });
            Test("partial shared food reservation clamps pickup to free portions", () =>
            {
                var f = new Fixture(); f.Food.stackCount = 23; f.Child.CurJob.count = 23;
                Job bottleFeed = JobMaker.MakeJob(new JobDef { defName = "BottleFeedBaby" }, f.Child, f.Food);
                Check(f.Helper.Reserve(f.Food, bottleFeed, 10, 2), "caregiver's two portions reserved");
                var driver = new JobDriver_ToddlerPrepareDiningRequest { pawn = f.Child, job = f.Child.CurJob };
                Check(driver.TryMakePreToilReservations(true), "21 free portions remain usable");
                Check(f.Child.CurJob.count == 21 && f.Request.foodCount == 21, "pickup limited to available portions");
                Check(f.Map.reservationManager.Reservations.Any(r => r.Pawn == f.Helper && r.Count == 2), "caregiver reservation preserved");
                Check(Log.Errors.Count == 0, "shared stack does not report red error");
            });
            Test("fully reserved food is rejected before creating a new request", () =>
            {
                var f = new Fixture(); f.Manager.Complete(f.Request);
                Check(f.Helper.Reserve(f.Food, JobMaker.MakeJob(JobDefOf.Ingest), 10, f.Food.stackCount), "food claimed by caregiver");
                TestFurniture.Chair = f.Chair;
                Job original = JobMaker.MakeJob(JobDefOf.Ingest, f.Food); original.count = 2;
                object[] args = { f.Child, original };
                typeof(JobGiver_GetFood_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
                Check(ReferenceEquals(args[1], original), "vanilla job retained");
                Check(f.Manager.GetForPawn(f.Child) == null && !f.Manager.IsChairClaimed(f.Chair), "no orphan request or chair claim");
            });
            Test("reservation race retires request without error recovery loop", () =>
            {
                var f = new Fixture();
                Check(f.Child.jobs.LastReservationsCanFail, "RDE startup marks reservation contention as expected");
                Check(f.Helper.Reserve(f.Food, JobMaker.MakeJob(JobDefOf.Ingest), 10, f.Food.stackCount), "food taken after selection");
                var driver = new JobDriver_ToddlerPrepareDiningRequest { pawn = f.Child, job = f.Child.CurJob };
                Check(!driver.TryMakePreToilReservations(true), "race fails safely");
                Check(Log.Errors.Count == 0 && !f.Request.IsActive && f.Manager.IsCoolingDown(f.Child), "no red error or orphan request");
                Check(!f.Manager.IsChairClaimed(f.Chair), "chair released");
                var emergency = new JobDef { defName = "UnrelatedOrder" };
                f.Child.jobs.StartJob(JobMaker.MakeJob(emergency), JobCondition.InterruptForced);
                Check(!f.Child.jobs.LastReservationsCanFail, "unrelated jobs keep their own error handling");
            });
            Test("failed staging reservation releases only this job's partial reservations", () =>
            {
                var f = new Fixture();
                Check(f.Helper.Reserve(f.Request.stagingCell, JobMaker.MakeJob(JobDefOf.Wait)), "staging cell taken");
                var driver = new JobDriver_ToddlerPrepareDiningRequest { pawn = f.Child, job = f.Child.CurJob };
                Check(!driver.TryMakePreToilReservations(true), "staging contention rejected");
                Check(!f.Map.reservationManager.Reservations.Any(r => r.Pawn == f.Child), "child's partial food reservation released");
                Check(f.Map.reservationManager.Reservations.Any(r => r.Pawn == f.Helper), "other pawn's reservation preserved");
                Check(Log.Errors.Count == 0, "normal contention silent");
            });
            Test("caregiver-first bottle feeding becomes one provided chair meal", () =>
            {
                var f = new Fixture(); f.Manager.Complete(f.Request);
                f.Child.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait), JobCondition.InterruptForced);
                TestFurniture.Chair = f.Chair;
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var request = f.Manager.GetForPawn(f.Child);
                Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && request.helper == f.Helper, "adult entry claims one indexed meal at actual start");
                Check(f.Helper.jobs.LastReservationsCanFail, "provider startup races are expected");
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
                Run(toils, 0, 0); Attach(new JobDriver_ToddlerWaitForMeal(), f.Child);
                Run(toils, 1, 2);
                Check(f.Food.stackCount == 6 && request.food != f.Food && f.Helper.inventory.Contains(request.food), "exact split meal stored without merging or losing reference");
                Check(f.Helper.carryTracker.CarriedThing == null, "hands free before picking up child");
                Run(toils, 3, 6);
                Check(f.Helper.carryTracker.CarriedThing == f.Child && RatToddlerDiningUtility.HoldsFood(request), "provided food survives Carried handoff");
                Run(toils, 7, 8);
                Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair && f.Child.Position == f.Chair.Position, "child eats at chair");
                Run(Attach(new JobDriver_ToddlerEatAtChair(), f.Child), 0, 5);
                Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "adult entry meal completes and releases");
            });
            Test("caregiver entry adopts the child's existing meal instead of fetching another", () =>
            {
                var f = new Fixture(); var prepare = Attach(new JobDriver_ToddlerPrepareDiningRequest(), f.Child);
                Run(prepare, 0, 2); Thing meal = f.Request.food;
                var otherFood = new Thing { Map = f.Map, stackCount = 20 };
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, otherFood));
                Check(f.Manager.GetForPawn(f.Child) == f.Request && f.Request.food == meal, "adopts the same request and food");
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
                Run(toils, 0, 2);
                Check(f.Request.IsActive && RatToddlerDiningUtility.HoldsFood(f.Request), "replacing preparation does not cancel or drop meal");
                Run(toils, 3, 8);
                Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair && otherFood.stackCount == 20, "unused food untouched");
            });
            Test("active dining excludes automatic feeding but permits emergency care", () =>
            {
                var f = new Fixture(); f.Waiting();
                Check(!ChildcareUtility.WantsSuckle(f.Child), "active held meal defers automatic feeding");
                f.Child.needs.food.CurLevelPercentage = .01f;
                Check(ChildcareUtility.WantsSuckle(f.Child), "critical hunger permits feeding");
                f.Child.needs.food.CurLevelPercentage = .5f; f.Child.MedicalRest = true;
                Check(ChildcareUtility.WantsSuckle(f.Child), "medical care permits feeding");
                f.Child.MedicalRest = false; f.Child.UnsafeTemperature = true;
                Check(ChildcareUtility.WantsSuckle(f.Child), "unsafe temperature permits feeding");
            });
            Test("two caregivers cannot start two meals for one child", () =>
            {
                var f = new Fixture();
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var other = new Pawn { Map = f.Map, Toddler = false };
                other.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                Check(other.CurJobDef == JobDefOf.Wait && f.Request.helper == f.Helper, "stale second selection does not start feeding");
                Check(f.Manager.Requests.Count(r => r.IsActive) == 1, "single active meal");
            });
            Test("provided inventory meal transfers only this meal's portions", () =>
            {
                var f = new Fixture(); f.Food.Spawned = false; f.Food.stackCount = 23; f.Helper.inventory.Items.Add(f.Food);
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
                Run(toils, 0, 4);
                Check(f.Food.stackCount == 19 && f.Helper.inventory.Contains(f.Food), "caregiver retains unrelated portions");
                Check(f.Request.food != f.Food && f.Child.carryTracker.CarriedThing == f.Request.food && f.Request.foodCount == 4, "child receives separate four-portion meal");
            });
            Test("provider interruption preserves its split meal for normal recovery", () =>
            {
                var f = new Fixture(); f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper); Run(toils, 0, 2);
                Thing meal = f.Request.food;
                f.Helper.jobs.StartJob(JobMaker.MakeJob(new JobDef { defName = "PlayerOrder" }), JobCondition.InterruptForced);
                Check(!f.Request.IsActive && meal.Spawned && !f.Helper.inventory.Contains(meal), "failed provider releases original split meal");
                f.Tick(60);
                Check(f.Child.CurJobDef == JobDefOf.Ingest && f.Child.CurJob.targetA.Thing == meal, "same meal recovered without finding another");
            });
            Test("a failed transfer releases the supplied meal and claims", () =>
            {
                var f = new Fixture(); f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper); Run(toils, 0, 3);
                f.Child.carryTracker.innerContainer.TransferFails = true; Run(toils, 4, 4);
                Check(!f.Request.IsActive && !f.Manager.IsChairClaimed(f.Chair) && f.Request.food.Spawned, "failed handoff retires and exposes original meal");
            });
            Test("normal feeding remains for incapable babies, humans and missing chairs", () =>
            {
                var f = new Fixture(); f.Manager.Complete(f.Request); f.Child.SelfFeeding = false;
                TestFurniture.Chair = f.Chair;
                Job feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.CurJob == feeding, "incapable baby keeps feeding");
                f.Child.SelfFeeding = true; f.Child.def = new ThingDef();
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.CurJob == feeding, "human keeps feeding");
                f.Child.def = DefDatabase<ThingDef>.Ratkin; TestFurniture.Chair = null;
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.CurJob == feeding, "no chair keeps feeding");
            });
            Test("manual feeding cancels assistance reservations without redirecting the order", () =>
            {
                var f = new Fixture(); f.Claim();
                Check(f.Helper.Reserve(f.Child, f.Helper.CurJob), "caregiver owns child");
                var manualHelper = new Pawn { Map = f.Map, Toddler = false };
                var feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food); feeding.playerForced = true;
                manualHelper.jobs.StartJob(feeding);
                Check(manualHelper.CurJob == feeding && !f.Request.IsActive, "manual feeding retained");
                Check(!f.Map.reservationManager.Reservations.Any(r => r.Pawn == f.Helper), "assistance claim released for manual care");
            });
            Test("an existing feeding operation cannot be stolen by a new dining request", () =>
            {
                var f = new Fixture(); f.Manager.Complete(f.Request);
                var feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food); feeding.playerForced = true;
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.Reserve(f.Child, feeding), "normal feeding owns child");
                Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null, "no request steals active feeding");
            });
            Test("breastfeeding entry finds actual food before choosing a chair meal", () =>
            {
                var f = new Fixture(); FoodUtility.BabyFood = f.Food;
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Breastfeed, f.Child));
                Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && f.Helper.CurJob.targetC.Thing == f.Food, "milk entry redirected only with an actual meal");
                var other = new Fixture(); other.Manager.Complete(other.Request); FoodUtility.BabyFood = null;
                TestFurniture.Chair = other.Chair;
                other.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Breastfeed, other.Child));
                Check(other.Helper.CurJobDef == JobDefOf.Breastfeed && other.Manager.GetForPawn(other.Child) == null, "no food preserves breastfeeding");
            });
            Test("a claimed request has a separate travel timeout", () =>
            {
                var f = new Fixture(); f.Child.needs.food.CurLevelPercentage = .15f; f.Waiting();
                Find.TickManager.TicksGame = 420; f.Claim(); f.Tick(480);
                Check(f.Request.state == RatToddlerDiningState.Claimed, "helper walking is not timed out by hungry wait");
                f.Tick(1620); Check(!f.Request.IsActive, "helper travel remains bounded");
            });
            Test("failure redirects immediate food selection to the original held meal", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Manager.StartFloorFallback(f.Request, false);
                Thing otherFood = new Thing { Map = f.Map };
                f.Child.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, otherFood), JobCondition.InterruptForced);
                Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor && f.Child.CurJob.targetA.Thing == f.Food, "selection race uses held food immediately");
                Check(f.Child.carryTracker.CarriedThing == f.Food && !f.Food.Spawned, "no needless drop before eating");
            });
            Test("recovery changes an automatic alternate meal but preserves player food orders", () =>
            {
                var f = new Fixture(); f.Manager.StartFloorFallback(f.Request, false);
                Thing otherFood = new Thing { Map = f.Map }; var order = JobMaker.MakeJob(JobDefOf.Ingest, otherFood); order.playerForced = true;
                f.Child.jobs.StartJob(order, JobCondition.InterruptForced); f.Tick(60);
                Check(f.Child.CurJob == order, "player's chosen food respected");
                var other = new Fixture(); other.Manager.StartFloorFallback(other.Request, false);
                other.Child.jobs.curJob = JobMaker.MakeJob(JobDefOf.Ingest, new Thing { Map = other.Map });
                other.Tick(60);
                Check(other.Child.CurJob.targetA.Thing == other.Food, "automatic alternate food replaced by recoverable original meal");
            });
            Test("waiting child tracks the supplied split even if the original stack disappears", () =>
            {
                var f = new Fixture(); f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
                Run(toils, 0, 0); var wait = new JobDriver_ToddlerWaitForMeal(); Attach(wait, f.Child);
                Run(toils, 1, 2); f.Food.Destroyed = true;
                Check(f.Child.CurJob.targetA.Thing == f.Request.food && !wait.Failures.Any(fail => fail()), "waiting job follows actual meal instead of original stack");
                Scribe.mode = LoadSaveMode.PostLoadInit; f.Manager.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
                f.Tick(60); Check(f.Request.IsActive && f.Request.helperJobId == f.Helper.CurJob.loadID, "loaded collecting state retains ownership");
                Run(toils, 3, 8); Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair, "split meal survives recovery of indexes");
            });
            Test("food already carried by caregiver is stored before carrying the child", () =>
            {
                var f = new Fixture(); f.Helper.carryTracker.Take(f.Food, f.Food.stackCount);
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                Check(f.Helper.carryTracker.CarriedThing == f.Food, "selected carried food preserved through job start");
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper); Run(toils, 0, 4);
                Check(f.Child.carryTracker.CarriedThing.stackCount == 4 && f.Helper.carryTracker.CarriedThing == null, "meal handed over and hands freed");
                Check(f.Helper.inventory.Contains(f.Food) && f.Food.stackCount == 6, "unused carried food retained in inventory");
            });
            Test("caregiver reservation race retires meal without touching child or logging errors", () =>
            {
                var f = new Fixture(); f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var other = new Pawn { Map = f.Map, Toddler = false };
                Check(other.Reserve(f.Chair, JobMaker.MakeJob(JobDefOf.Wait)), "chair taken after meal selected");
                var driver = new JobDriver_ProvideRatToddlerDiningMeal { pawn = f.Helper, job = f.Helper.CurJob };
                Check(!driver.TryMakePreToilReservations(true), "race fails normally");
                Check(!f.Request.IsActive && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerPrepareDiningRequest, "child job was not replaced before successful reservations");
                Check(!f.Map.reservationManager.Reservations.Any(r => r.Pawn == f.Helper) && Log.Errors.Count == 0, "partial claims released without red error");
            });
            Test("emergency feeding releases an active caregiver's claims", () =>
            {
                var f = new Fixture(); f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
                f.Child.needs.food.CurLevelPercentage = .01f;
                var rescuer = new Pawn { Map = f.Map, Toddler = false };
                var feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
                rescuer.jobs.StartJob(feeding);
                Check(rescuer.CurJob == feeding && !f.Request.IsActive && rescuer.CanReserve(f.Child), "urgent vanilla feeding can acquire child");
            });
            Test("adult entrance respects childcare and parent restrictions", () =>
            {
                var f = new Fixture(); f.Helper.workSettings.Childcare = false;
                var feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.CurJob == feeding && f.Request.helper == null, "unassigned helper not redirected");
                f.Helper.jobs.curJob = null; f.Helper.workSettings.Childcare = true; RatDiningMod.Settings.allowNonParents = false;
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.CurJob == feeding && f.Request.helper == null, "non-parent not redirected when disabled");
                f.Helper.jobs.curJob = null; f.Child.relations.Parents.Add(f.Helper);
                f.Helper.jobs.StartJob(feeding); Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, "assigned parent can provide meal");
            });
            Test("interrupted adoption reopens help without its old wait canceling the request", () =>
            {
                var f = new Fixture(); f.Waiting();
                f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper); Run(toils, 0, 0);
                var wait = new JobDriver_ToddlerWaitForMeal(); Attach(wait, f.Child);
                f.Helper.jobs.StartJob(JobMaker.MakeJob(new JobDef { defName = "InterruptProvider" }), JobCondition.InterruptForced);
                Check(f.Request.resumeWaitingPending && !wait.Failures.Any(fail => fail()), "child's old wait permits deferred reopen");
                f.Tick(60);
                Check(f.Request.IsActive && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerWaitForDiningHelp, "internal wait handoff retains request");
                Check(RatToddlerDiningUtility.HoldsFood(f.Request), "meal survives interruption and wait handoff");
            });
            Test("failed chair job finalizes into held food without a drop or a second selection", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Child.Position = f.Chair.Position;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair, f.Chair), JobCondition.InterruptForced, true);
                Attach(new JobDriver_ToddlerEatAtChair(), f.Child);
                f.Child.jobs.EndCurrentJob(JobCondition.Incompletable);
                Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor && f.Child.carryTracker.CarriedThing == f.Food, "cleanup finalizer carries the original meal straight into eating");
                Check(!f.Food.Spawned && !f.Request.IsActive, "meal never dropped and request released");
            });
            Test("failed chair pre-reservations still have a safe meal finalizer", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Child.Position = f.Chair.Position;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair, f.Chair), JobCondition.InterruptForced, true);
                var driver = new JobDriver_ToddlerEatAtChair { pawn = f.Child, job = f.Child.CurJob };
                f.Child.jobs.curDriver = driver; f.Child.CanReserveTargets = false;
                Check(!driver.TryMakePreToilReservations(false), "chair reservation fails before SetupToils");
                f.Child.jobs.EndCurrentJob(JobCondition.QueuedNoLongerValid);
                Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor && f.Child.carryTracker.CarriedThing == f.Food, "constructor-installed finalizer preserves food before toils exist");
            });
            Test("medical, unsafe or exhausted children do not begin new chair requests", () =>
            {
                var f = new Fixture(); f.Manager.Complete(f.Request); f.Child.MedicalRest = true;
                Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null, "medical rest uses normal care");
                f.Child.MedicalRest = false; f.Child.UnsafeTemperature = true;
                Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null, "unsafe temperature uses normal care");
                f.Child.UnsafeTemperature = false; f.Child.needs.rest.CurLevelPercentage = .01f;
                Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null, "urgent sleep uses normal care");
            });
            Console.WriteLine($"{passed} regression scenarios passed (engine doubles; in-game validation still required).");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
