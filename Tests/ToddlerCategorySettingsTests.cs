using System;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static void SetCategory(int category, bool value)
    {
        switch (category)
        {
            case 0: RatDiningMod.Settings.enableColonistToddlerHelp = value; break;
            case 1: RatDiningMod.Settings.enableVisitorToddlerHelp = value; break;
            case 2: RatDiningMod.Settings.enablePrisonerToddlerHelp = value; break;
            case 3: RatDiningMod.Settings.enableSlaveToddlerHelp = value; break;
        }
    }

    private static void ToddlerCategorySettingsTests()
    {
        Test("loaded requests yield to missing disabled category keys before map reconciliation", () =>
        {
            for (int category = 1; category < 4; category++)
            {
                RatDiningMod.Settings = new RatDiningSettings();
                Pawn child; Thing food; Building chair; Map map; RatToddlerDiningRequest request;
                if (category == 2)
                {
                    var f = new PrisonFixture(); request = f.Waiting();
                    child = f.Child; food = f.Food; chair = f.Chair; map = f.Map;
                }
                else
                {
                    var f = new VisitorFixture(category == 3); request = f.Waiting();
                    child = f.Child; food = f.Food; chair = f.Chair; map = f.Map;
                }
                // Existing request with an old settings file, whose new keys are absent.
                Scribe_Values.Values.Clear(); RatDiningMod.Settings = new RatDiningSettings();
                Scribe.mode = LoadSaveMode.LoadingVars; RatDiningMod.Settings.ExposeData();
                Scribe.mode = LoadSaveMode.PostLoadInit; map.Manager.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
                Check(!map.Manager.CanResumeRequest(request)
                    && !RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(child)
                    && EvaluateBabyPlay(child), "loaded disabled request cannot suppress ordinary care");
                Find.TickManager.TicksGame = 60; map.Manager.MapComponentTick();
                SetCategory(category, true); // Re-enabling must not resurrect the cancelled contract.
                Find.TickManager.TicksGame = 120; map.Manager.MapComponentTick();
                Check(!request.IsActive && map.Manager.GetForPawn(child) == null && !map.Manager.IsChairClaimed(chair)
                    && child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor
                    && child.carryTracker.CarriedThing == food, "loaded request retires and preserves its meal");
            }
        });
        Test("disabling a category after provider pickup releases reservations and keeps its split meal", () =>
        {
            for (int category = 0; category < 4; category++)
            {
                RatDiningMod.Settings = new RatDiningSettings();
                Pawn child, helper; Thing food; Building chair; Map map;
                if (category == 2)
                {
                    var f = new PrisonFixture(); child = f.Child; helper = f.Warden; food = f.Food; chair = f.Chair; map = f.Map;
                }
                else
                {
                    var f = new VisitorFixture(category == 3);
                    child = f.Child; helper = f.Helper; food = f.Food; chair = f.Chair; map = f.Map;
                    if (category == 0) { child.Faction = helper.Faction = Faction.OfPlayer; child.IsColonist = helper.IsColonist = true; helper.workSettings = new Pawn_WorkSettings(); }
                }
                child.needs.food.CurLevelPercentage = .2f;
                helper.jobs.StartJob(category == 1 ? VisitorIdle(helper) : JobMaker.MakeJob(JobDefOf.BottleFeedBaby, child, food));
                Run(Attach(new JobDriver_ProvideRatToddlerDiningMeal(), helper), 0, 2);
                var request = map.Manager.GetForPawn(child); Thing split = request.food; int amount = split.stackCount;
                Check(helper.inventory.Contains(split) && split != food, "provider holds exact allocated portions");
                SetCategory(category, false); Find.TickManager.TicksGame = 60; map.Manager.MapComponentTick();
                Check(!request.IsActive && !map.Manager.IsChairClaimed(chair)
                    && map.reservationManager.Reservations.Count == 0 && split.Spawned && !helper.inventory.Contains(split),
                    "food, child, chair and care reservations released");
                Find.TickManager.TicksGame = 120; map.Manager.MapComponentTick();
                Check(child.CurJobDef == JobDefOf.Ingest && child.CurJob.targetA.Thing == split
                    && split.stackCount == amount && !split.Destroyed, "recovery takes original split rather than selecting new food");
            }
        });
        Test("production settings default to colonists only", () =>
        {
            var settings = new RatDiningSettings();
            Check(settings.enableToddlerHelp && settings.enableColonistToddlerHelp
                && !settings.enableVisitorToddlerHelp && !settings.enablePrisonerToddlerHelp
                && !settings.enableSlaveToddlerHelp, "four defaults in actual production settings");
        });
        foreach (bool enabled in new[] { false, true })
        {
            Test("legacy master " + enabled + " survives missing category keys", () =>
            {
                Scribe_Values.Values["enableToddlerHelp"] = enabled;
                var settings = new RatDiningSettings { enableVisitorToddlerHelp = true,
                    enablePrisonerToddlerHelp = true, enableSlaveToddlerHelp = true };
                Scribe.mode = LoadSaveMode.LoadingVars; settings.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
                Check(settings.enableToddlerHelp == enabled && settings.enableColonistToddlerHelp
                    && !settings.enableVisitorToddlerHelp && !settings.enablePrisonerToddlerHelp
                    && !settings.enableSlaveToddlerHelp, "missing keys apply defaults without overwriting master");
            });
        }
        Test("production category selections persist independently", () =>
        {
            for (int mask = 0; mask < 16; mask++)
            {
                for (int i = 0; i < 4; i++) SetCategory(i, (mask & (1 << i)) != 0);
                Scribe.mode = LoadSaveMode.Saving; RatDiningMod.Settings.ExposeData();
                var loaded = new RatDiningSettings(); Scribe.mode = LoadSaveMode.LoadingVars; loaded.ExposeData();
                Scribe.mode = LoadSaveMode.Inactive;
                Check(loaded.enableColonistToddlerHelp == ((mask & 1) != 0)
                    && loaded.enableVisitorToddlerHelp == ((mask & 2) != 0)
                    && loaded.enablePrisonerToddlerHelp == ((mask & 4) != 0)
                    && loaded.enableSlaveToddlerHelp == ((mask & 8) != 0), "saved four independent keys");
            }
        });
        for (int i = 0; i < 4; i++)
        {
            int category = i;
            Test("category " + category + " gates both request entrances and cancels waiting", () =>
            {
                Pawn child, helper; Thing food; Building chair; Map map;
                if (category == 2)
                {
                    var f = new PrisonFixture(enableHelp: false);
                    child = f.Child; helper = f.Warden; food = f.Food; chair = f.Chair; map = f.Map;
                }
                else
                {
                    var f = new VisitorFixture(category == 3, enableHelp: false);
                    child = f.Child; helper = f.Helper; food = f.Food; chair = f.Chair; map = f.Map;
                    if (category == 0) { child.Faction = helper.Faction = Faction.OfPlayer; child.IsColonist = helper.IsColonist = true; helper.workSettings = new Pawn_WorkSettings(); }
                }
                child.needs.food.CurLevelPercentage = .2f;
                Check(RatDiningApi.IsEligibleToddler(child) == (category == 0), "default target classification");
                SetCategory(category, false);
                Check(EvaluateIngest(child, food).def == JobDefOf.Ingest, "child-first remains native when disabled");
                Check(!RatToddlerMealCoordinator.CanHelp(helper, child, true), "caregiver-first disabled for target");
                SetCategory(category, true);
                Job meal = EvaluateIngest(child, food);
                Check(meal.def == RDE_DefOf.RDE_ToddlerPrepareDiningRequest, "enabled target creates request");
                child.jobs.StartJob(meal); child.carryTracker.CarriedThing = food; food.Spawned = false;
                var request = map.Manager.GetForPawn(child);
                child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForDiningHelp, food, chair, request.stagingCell), keepCarryingThingOverride: true);
                map.Manager.SetState(request, RatToddlerDiningState.WaitingForHelp);
                SetCategory(category, false); Find.TickManager.TicksGame = 60; map.Manager.MapComponentTick();
                Check(!request.IsActive && !map.Manager.IsChairClaimed(chair), "disabled category releases request and chair");
                Find.TickManager.TicksGame = 120; map.Manager.MapComponentTick();
                Check(child.carryTracker.CarriedThing == food && child.CurJob.targetA.Thing == food
                    && child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "original food recovery bypasses help switch");
            });
            Test("category " + category + " closes safely while child is carried", () =>
            {
                Pawn child, helper; Thing food; Building chair; Map map; RatToddlerDiningRequest request;
                if (category == 2)
                {
                    var f = new PrisonFixture(); request = CarryPrisoner(f);
                    child = f.Child; helper = f.Warden; food = f.Food; chair = f.Chair; map = f.Map;
                }
                else if (category == 0)
                {
                    var f = new Fixture(); f.Carrying(); request = f.Request;
                    child = f.Child; helper = f.Helper; food = f.Food; chair = f.Chair; map = f.Map;
                }
                else
                {
                    var f = new VisitorFixture(category == 3); request = f.Waiting();
                    child = f.Child; helper = f.Helper; food = f.Food; chair = f.Chair; map = f.Map;
                    helper.jobs.StartJob(category == 1 ? VisitorIdle(helper) : new WorkGiver_AssistRatToddlerDining().JobOnThing(helper, child));
                    Run(Attach(new JobDriver_AssistRatToddlerDining(), helper), 0, 3);
                }
                Check(helper.carryTracker.CarriedThing == child, "carried before disable");
                SetCategory(category, false); Find.TickManager.TicksGame = 60; map.Manager.MapComponentTick();
                Check(helper.carryTracker.CarriedThing != child && child.Spawned && !request.IsActive
                    && !map.Manager.IsChairClaimed(chair), "safe placement releases chair");
                Find.TickManager.TicksGame = 120; map.Manager.MapComponentTick();
                Check(child.carryTracker.CarriedThing == food && child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor,
                    "disabled carried child continues original meal");
            });
        }
        Test("master remains authoritative over all enabled categories", () =>
        {
            for (int i = 0; i < 4; i++) SetCategory(i, true);
            var f = new VisitorFixture(); RatDiningMod.Settings.enableToddlerHelp = false;
            Check(!RatDiningApi.IsEligibleToddler(f.Child) && EvaluateIngest(f.Child, f.Food).def == JobDefOf.Ingest
                && VisitorIdle(f.Helper).def == JobDefOf.GotoWander, "master off rejects both entrances");
        });
        Test("category is determined by child rather than caregiver", () =>
        {
            var f = new VisitorFixture(true);
            RatDiningMod.Settings.enableColonistToddlerHelp = false;
            Check(RatToddlerMealCoordinator.CanHelp(f.Helper, f.Child, true), "colonist caregiver may help enabled slave child");
            RatDiningMod.Settings.enableSlaveToddlerHelp = false;
            RatDiningMod.Settings.enableColonistToddlerHelp = true;
            Check(!RatDiningApi.IsEligibleToddler(f.Child), "slave cannot inherit colonist opt-in");
        });
        Test("adult dining is independent of toddler category settings", () =>
        {
            var slave = new VisitorFixture(true, enableHelp: false);
            var visitor = new VisitorFixture(enableHelp: false);
            var prisoner = new PrisonFixture(false, enableHelp: false);
            slave.Child.Toddler = visitor.Child.Toddler = false;
            RatDiningMod.Settings.enableToddlerHelp = false;
            Check(EvaluateIngest(slave.Child, slave.Food).def == RDE_DefOf.RDE_EatUsingChairAsTable
                && EvaluateIngest(visitor.Child, visitor.Food).def == RDE_DefOf.RDE_EatUsingChairAsTable
                && EvaluateIngest(prisoner.Child, prisoner.Food).def == RDE_DefOf.RDE_EatUsingChairAsTable,
                "adult chair meals still work with all toddler help off");
        });
    }
}
