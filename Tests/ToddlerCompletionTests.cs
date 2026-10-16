using System;
using System.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static JobDriver_ToddlerEatAtChair StartChairChewing(Fixture f)
    {
        f.Waiting(); f.Child.Position = f.Chair.Position;
        f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair), keepCarryingThingOverride: true);
        return new JobDriver_ToddlerEatAtChair();
    }

    private static void ToddlerCompletionTests()
    {
        Test("completed chair food survives interleaved map reconciliation for all four identities", () =>
        {
            for (int category = 0; category < 4; category++)
            {
                var f = new Fixture(); Identity(f.Child, category); SetCategory(category, true);
                f.Request.custody = new RatDiningCustodySnapshot(); f.Request.custody.Bind(f.Child, f.Child.Position);
                var driver = StartChairChewing(f); f.Food.stackCount = 4; var toils = Attach(driver, f.Child);
                for (int i = 0; i <= 3; i++) { Check(!driver.Failures.Any(fail => fail()), "guards before native-shaped transition " + i); toils[i].initAction?.Invoke(); }
                Check(f.Request.mealFinalized && f.Request.mealJobId == f.Child.CurJob.loadID && f.Food.Destroyed, "actual consumption bound to job");
                f.Tick(60); Check(f.Request.state == RatToddlerDiningState.Completed && !f.Manager.IsCoolingDown(f.Child)
                    && !f.Manager.IsChairClaimed(f.Chair) && !f.Manager.IsRecovering(f.Child), "completed meal never becomes floor recovery");
                Check(!driver.Failures.Any(fail => fail()), "no false failure after request leaves index"); Run(toils, 4, 5);
                Check(f.Child.needs.mood.thoughts.memories.Memories.Count == 1, "one completed meal memory");
            }
        });
        Test("fresh GetFood reevaluation keeps the exact safe chair job and its food", () =>
        {
            for (int category = 0; category < 4; category++)
            {
                var f = new Fixture(); Identity(f.Child, category); SetCategory(category, true);
                f.Request.custody = new RatDiningCustodySnapshot(); f.Request.custody.Bind(f.Child, f.Child.Position);
                var toils = Attach(StartChairChewing(f), f.Child); Run(toils, 0, 2);
                Job current = f.Child.CurJob;
                Check(EvaluateIngest(f.Child, new Thing { Map = f.Map }) == current, "no replacement or second meal proposal");
                Check(f.Request.IsActive && f.Child.carryTracker.CarriedThing == f.Food && !f.Manager.IsCoolingDown(f.Child), "original meal and claim intact");
            }
        });
        Test("GetFood reevaluation does not pin unsafe or disabled unconsumed chair meals", () =>
        {
            foreach (Action<Fixture> change in new Action<Fixture>[] {
                f => RatDiningMod.Settings.enableToddlerHelp = false,
                f => f.Child.needs.food.CurLevelPercentage = .01f,
                f => f.Child.MedicalRest = true,
                f => f.Child.needs.rest.CurLevelPercentage = .05f,
                f => f.Child.Drafted = true })
            {
                RatDiningMod.Settings = new RatDiningSettings(); var f = new Fixture(); var toils = Attach(StartChairChewing(f), f.Child); Run(toils, 0, 2);
                Job current = f.Child.CurJob; change(f);
                Check(EvaluateIngest(f.Child, f.Food) != current && !f.Request.IsActive && !f.Food.Destroyed, "safety and cancellation stay effective");
            }
        });
        Test("long chair chewing never inherits caregiver wait or claim timeout", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
            for (int tick = 60; tick <= 6000; tick += 60)
            { f.Tick(tick); Check(f.Request.IsActive && !driver.Failures.Any(fail => fail()), "same safe long meal stays on chair"); }
            Check(!f.Food.Destroyed && !f.Manager.IsCoolingDown(f.Child), "no meal restart during slow chewing");
        });
        Test("after consumption eligibility changes do not reclassify the meal as failed", () =>
        {
            foreach (Action<Fixture> change in new Action<Fixture>[] {
                f => RatDiningMod.Settings.enableToddlerHelp = false,
                f => f.Child.SelfFeeding = false,
                f => f.Child.Toddler = false,
                f => f.Child.Drafted = true,
                f => f.Child.IsColonist = false })
            {
                RatDiningMod.Settings = new RatDiningSettings(); var f = new Fixture(); f.Food.stackCount = 4;
                var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 3); change(f);
                Check(!driver.Failures.Any(fail => fail()), "completed meal only needs cleanup");
                Check(driver.GetFinalizerJob(JobCondition.Incompletable) == null && !f.Manager.IsCoolingDown(f.Child), "no recovered second meal");
                Run(toils, 4, 5); Check(!f.Request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "releases original contract");
            }
        });
        Test("finalization records positive nutrition even when the callback disables eligibility", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
            Toils_Ingest.TestFinalizeAction = () => {
                Thing_Ingested_ClearPreviousChairThought.Postfix(f.Food, f.Child, .4f);
                f.Food.Destroyed = true; f.Child.carryTracker.CarriedThing = null;
                RatDiningMod.Settings.enableToddlerHelp = false;
            };
            Run(toils, 3, 5);
            Check(f.Request.mealFinalized && f.Request.state == RatToddlerDiningState.Completed
                && !f.Manager.IsCoolingDown(f.Child) && !f.Manager.IsRecovering(f.Child), "native result marks consumed meal before cleanup");
        });
        Test("successful finalization is not inferred from zero nutrition or missing food", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
            Toils_Ingest.TestFinalizeAction = () => Thing_Ingested_ClearPreviousChairThought.Postfix(f.Food, f.Child, 0f);
            Run(toils, 3, 5);
            Check(!driver.HasFinalizedMeal && !f.Request.mealFinalized && f.Food.stackCount == 10
                && f.Child.needs.mood.thoughts.memories.Memories.Count == 0, "zero result gets no successful meal or mood");
        });
        Test("ingestion observation ignores foreign pawn food job and invalid nutrition", () =>
        {
            var f = new Fixture(); Job job = f.Child.CurJob;
            using (var scope = RatDiningMealSafety.ObserveIngestion(f.Child, f.Food, job))
            {
                RatDiningMealSafety.RecordIngestion(f.Helper, f.Food, 1);
                RatDiningMealSafety.RecordIngestion(f.Child, new Thing(), 1);
                foreach (float nutrition in new[] { 0f, -1f, float.NaN, float.PositiveInfinity }) RatDiningMealSafety.RecordIngestion(f.Child, f.Food, nutrition);
                Check(!scope.ate, "unrelated result cannot commit the meal");
                f.Child.jobs.curJob = new Job { def = job.def };
                RatDiningMealSafety.RecordIngestion(f.Child, f.Food, 1); Check(!scope.ate, "job identity must still match");
                f.Child.jobs.curJob = job;
                Thing_Ingested_ClearPreviousChairThought.Postfix(f.Food, f.Child, .4f); Check(scope.ate, "actual matching nutrition signal");
            }
        });
        Test("nested ingestion observation restores the outer scope on exceptions", () =>
        {
            var f = new Fixture(); Job outerJob = f.Child.CurJob; Job innerJob = JobMaker.MakeJob(JobDefOf.Ingest, new Thing()); f.Helper.jobs.curJob = innerJob;
            using (var outer = RatDiningMealSafety.ObserveIngestion(f.Child, f.Food, outerJob))
            {
                try
                {
                    using (var inner = RatDiningMealSafety.ObserveIngestion(f.Helper, innerJob.targetA.Thing, innerJob))
                    { RatDiningMealSafety.RecordIngestion(f.Child, f.Food, .4f); Check(!outer.ate, "inner scope cannot mark outer meal"); throw new InvalidOperationException("probe"); }
                }
                catch (InvalidOperationException) { }
                RatDiningMealSafety.RecordIngestion(f.Child, f.Food, .4f); Check(outer.ate, "outer observer restored");
            }
        });
        Test("finalizer exceptions do not mark success and restore the observation scope", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
            Toils_Ingest.TestFinalizeAction = () => throw new InvalidOperationException("unrelated native/mod exception");
            bool threw = false; try { toils[3].initAction(); } catch (InvalidOperationException) { threw = true; }
            Check(threw && !f.Request.mealFinalized && !driver.HasFinalizedMeal, "no swallowed exception or fake completed state");
            RatDiningMealSafety.RecordIngestion(f.Child, f.Food, .4f); Check(!f.Request.mealFinalized, "closed observer cannot commit later results");
        });
        Test("saved completed chair job cleans up without eating an extra held portion", () =>
        {
            var f = new Fixture(); f.Child.needs.food.CurLevelPercentage = .6f;
            var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 3);
            int remaining = f.Food.stackCount; Check(remaining > 0, "oversized original stack has leftovers");
            Scribe.mode = LoadSaveMode.Saving; f.Request.ExposeData(); driver.ExposeData();
            f.Request.mealFinalized = false; f.Request.mealJobId = -1;
            var restored = new JobDriver_ToddlerEatAtChair(); Scribe.mode = LoadSaveMode.LoadingVars;
            f.Request.ExposeData(); restored.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
            RatDiningMod.Settings.enableToddlerHelp = false;
            var loadedToils = Attach(restored, f.Child); Run(loadedToils, 0, loadedToils.Count - 1);
            Check(f.Food.stackCount == remaining && f.Request.state == RatToddlerDiningState.Completed
                && !f.Manager.IsCoolingDown(f.Child), "serialized proof prevents replay");
        });
        Test("exceptions after a positive ingestion callback propagate without replaying that food", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
            Toils_Ingest.TestFinalizeAction = () => {
                Thing_Ingested_ClearPreviousChairThought.Postfix(f.Food, f.Child, .4f);
                f.Food.Destroyed = true; throw new InvalidOperationException("late native/mod error");
            };
            bool threw = false; try { toils[3].initAction(); } catch (InvalidOperationException) { threw = true; }
            Check(threw && f.Request.mealFinalized && driver.HasFinalizedMeal, "real consumption retained while error remains visible");
            driver.Cleanup(JobCondition.Errored);
            Check(driver.GetFinalizerJob(JobCondition.Errored) == null && !f.Manager.IsCoolingDown(f.Child), "no duplicate food job after consumed result");
        });
        Test("old saved driver completion migrates without new request marker fields", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 3);
            Scribe.mode = LoadSaveMode.Saving; driver.ExposeData();
            f.Request.mealFinalized = false; f.Request.mealJobId = -1;
            var restored = new JobDriver_ToddlerEatAtChair(); Scribe.mode = LoadSaveMode.LoadingVars; restored.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
            RatDiningMod.Settings.enableToddlerHelp = false;
            var loaded = Attach(restored, f.Child); Run(loaded, 3, 5);
            Check(f.Request.mealFinalized && f.Request.state == RatToddlerDiningState.Completed, "old driver proof upgrades its bound request");
        });
        Test("completed request readback never blocks a fresh request or enters recovery indexes", () =>
        {
            var f = new Fixture(); f.Food.stackCount = 4;
            var toils = Attach(StartChairChewing(f), f.Child); Run(toils, 0, 3);
            f.Request.chair = null; f.Request.food = null; // Destroyed references are absent in a real save.
            Scribe.mode = LoadSaveMode.PostLoadInit; f.Manager.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
            Check(f.Request.state == RatToddlerDiningState.Completed && f.Manager.GetForPawn(f.Child) == null
                && !f.Manager.IsRecovering(f.Child), "do not reserve missing consumed-food or chair references");
            f.Child.Position = new IntVec3(10); f.Child.needs.food.CurLevelPercentage = .6f;
            Check(f.Manager.Create(f.Child, new Thing { Map = f.Map }, f.Chair, null, new IntVec3(19)) != null, "new legitimate meal is not blocked");
        });
        Test("legacy request missing completion fields remains uncompleted", () =>
        {
            var request = new RatToddlerDiningRequest(); request.mealFinalized = true; request.mealJobId = 123;
            Scribe.mode = LoadSaveMode.LoadingVars; request.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
            Check(!request.mealFinalized && request.mealJobId == -1, "missing keys cannot create proof");
        });
        Test("completed marker rejects a different job id or changed meal targets", () =>
        {
            var f = new Fixture(); var toils = Attach(StartChairChewing(f), f.Child); Run(toils, 0, 3);
            Job actual = f.Child.CurJob;
            var other = JobMaker.MakeJob(actual.def, f.Food, f.Chair);
            Check(!f.Manager.IsFinalizedMeal(f.Request, other), "different identity");
            other.loadID = actual.loadID; other.targetA = new Thing();
            Check(!f.Manager.IsFinalizedMeal(f.Request, other), "food target changed");
            other.targetA = f.Food; other.targetB = new Building();
            Check(!f.Manager.IsFinalizedMeal(f.Request, other), "chair target changed");
        });
        Test("post-consumption chair destruction never starts floor recovery", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 3);
            f.Chair.Destroyed = true; driver.Cleanup(JobCondition.Incompletable);
            Check(driver.GetFinalizerJob(JobCondition.Incompletable) == null && f.Request.state == RatToddlerDiningState.Completed
                && !f.Manager.IsCoolingDown(f.Child) && !f.Manager.IsChairClaimed(f.Chair), "complete original meal despite furniture cleanup failure");
        });
        Test("unconsumed disabled request still releases claims and preserves its original meal", () =>
        {
            var f = new Fixture(); var driver = StartChairChewing(f); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
            RatDiningMod.Settings.enableToddlerHelp = false;
            Check(driver.Failures.Any(fail => fail()), "unconsumed meal obeys guard");
            driver.Cleanup(JobCondition.Incompletable); Job recovery = driver.GetFinalizerJob(JobCondition.Incompletable);
            Check(recovery?.targetA.Thing == f.Food && recovery.def == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor
                && !f.Manager.IsChairClaimed(f.Chair), "original meal floor recovery retained");
        });
        Test("critical hunger rest and medical safety remain effective before actual consumption", () =>
        {
            foreach (Action<Fixture> change in new Action<Fixture>[] {
                f => f.Child.needs.food.CurLevelPercentage = .01f,
                f => f.Child.needs.rest.CurLevelPercentage = .05f,
                f => f.Child.MedicalRest = true,
                f => f.Child.UnsafeTemperature = true,
                f => f.Child.Danger = Danger.Deadly })
            {
                var f = new Fixture(); var toils = Attach(StartChairChewing(f), f.Child); Run(toils, 0, 2);
                change(f); f.Tick(60);
                Check(!f.Request.IsActive && !f.Request.mealFinalized && f.Food.stackCount == 10
                    && !f.Manager.IsChairClaimed(f.Chair), "original safety condition cancels unconsumed meal");
            }
        });
        Test("failure diagnostics never query off-map cells", () =>
        {
            var f = new Fixture(); f.Waiting(); f.Child.Position = IntVec3.Invalid;
            string message = MapComponent_RatToddlerDining.DescribeFailure(f.Request);
            Check(message.Contains("cellValid=False") && message.Contains("danger=n/a"), "invalid grid is only described");
        });
        Test("duplicate finalized callbacks keep one memory and do not eat leftovers twice", () =>
        {
            var f = new Fixture(); var toils = Attach(StartChairChewing(f), f.Child); Run(toils, 0, 4);
            int left = f.Food.stackCount; float nutrition = f.Child.needs.food.CurLevelPercentage;
            toils[3].initAction(); toils[4].initAction();
            Check(f.Food.stackCount == left && f.Child.needs.food.CurLevelPercentage == nutrition
                && f.Child.needs.mood.thoughts.memories.Memories.Count == 1, "one successful meal event");
        });
        Test("fallback diagnostics log the real failed state only when debug logging is on", () =>
        {
            var f = new Fixture(); f.Waiting(); RatDiningMod.Settings.enableToddlerHelp = false;
            f.Tick(60); Check(!Log.Messages.Any(s => s.Contains("Toddler meal fallback")), "silent normal settings");
            RatDiningMod.Settings.enableToddlerHelp = true; var diagnosed = new Fixture(); diagnosed.Waiting();
            RatDiningMod.Settings.debugLogging = true; RatDiningMod.Settings.enableToddlerHelp = false;
            diagnosed.Tick(60); diagnosed.Tick(120);
            var logs = Log.Messages.Where(s => s.Contains("Toddler meal fallback")).ToList();
            Check(logs.Count == 1 && logs[0].Contains("helpMaster=False") && logs[0].Contains("holdingFood=True")
                && logs[0].Contains("finalized=False") && logs[0].Contains("state=WaitingForHelp"), "single transition records actionable original state");
        });
    }
}
