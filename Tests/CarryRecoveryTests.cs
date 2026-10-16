using System;
using System.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static RatToddlerDiningRequest CarryPrisoner(PrisonFixture f)
    {
        RatToddlerDiningRequest request = f.Waiting();
        f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
        var toils = Attach(new JobDriver_AssistRatToddlerDining(), f.Warden); Run(toils, 0, 3);
        Check(f.Warden.carryTracker.CarriedThing == f.Child, "prisoner carried with original meal");
        return request;
    }

    private static void CarryRecoveryTests()
    {
        Test("a breached prison merged with outdoors is not the original safe recovery room", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f);
            f.Warden.Position = new IntVec3(500);
            f.Prison.IsPrisonCell = false; f.Prison.ProperRoom = false; f.Map.Rooms[500] = f.Prison;
            Check(!RatDiningCustody.IsLegalRecoveryCell(f.Child, f.Warden.Position, request.custody.originCell),
                "origin and destination now share an outdoor region rather than an intact room");
            f.Warden.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 60));
            Check(f.Warden.CurJobDef == JobDefOf.BringBabyToSafety && f.Warden.carryTracker.CarriedThing == f.Child,
                "no outdoor drop or return into the breached original prison");
            Check(request.careHandoff && f.Child.carryTracker.CarriedThing == f.Food, "native care receives child and original meal");
        });
        Test("an outdoor prison marker does not qualify for routine dining or an alternative return", () =>
        {
            var f = new PrisonFixture(); f.Prison.ProperRoom = false;
            Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null,
                "prison designation does not override a breached outdoor room");
            Check(!RatDiningCustody.IsLegalRecoveryCell(f.Child, new IntVec3(20), IntVec3.Invalid),
                "alternative marked prison also must be an enclosed room");
        });
        Test("loaded active or recovering dining records yield before dropping a child held for care", () =>
        {
            foreach (bool recovering in new[] { false, true })
            {
                var f = new PrisonFixture(); var request = CarryPrisoner(f);
                if (recovering)
                {
                    f.Warden.Position = new IntVec3(500); f.Manager.StartFloorFallback(request, false);
                    f.Warden.Position = new IntVec3(10);
                }
                // A loaded current care job does not pass through StartJob's prefix.
                Job care = JobMaker.MakeJob(JobDefOf.BringBabyToSafety, f.Child);
                f.Warden.jobs.curJob = care; f.Warden.jobs.curDriver = null;
                f.Tick(60);
                Check(f.Warden.CurJob == care && f.Warden.carryTracker.CarriedThing == f.Child && !f.Child.Spawned,
                    "care ownership checked before the safe local drop");
                Check(request.careHandoff && f.Manager.Requests.Count == 0 && !f.Manager.IsChairClaimed(f.Chair),
                    "loaded dining claims retire without overriding care");
            }
        });
        Test("medical rest is not replaced by automatic recovery eating", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting(); int starts = f.Child.jobs.Starts;
            f.Child.MedicalRest = true; f.Tick(60); f.Tick(120);
            Check(!request.IsActive && f.Manager.Requests.Count == 0, "unsafe routine meal retires");
            Check(f.Child.jobs.Starts == starts && f.Child.carryTracker.CarriedThing == f.Food,
                "original food retained while native medical care chooses the next action");
        });
        Test("pending original food release prevents a second request after the short cooldown", () =>
        {
            var f = new PrisonFixture(); f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
            var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Warden); Run(toils, 0, 2);
            var request = f.Manager.GetForPawn(f.Child); Thing split = request.food;
            f.Warden.inventory.innerContainer.DropFails = true; f.Manager.StartFloorFallback(request, false);
            f.Child.jobs.curJob = null; Find.TickManager.TicksGame = 660;
            Check(!f.Manager.IsCoolingDown(f.Child) && request.recoveryPending, "short cooldown expired before total recovery deadline");
            Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null,
                "no second chair claim before the original meal retires");
            Check(!RatToddlerMealCoordinator.TryPlanPrisonerMeal(f.Warden, f.Child, out _, out _, out _, out _),
                "work scanning does not repeatedly propose a second provision");
            Check(f.Manager.Requests.Count == 1 && f.Warden.inventory.Contains(split), "only original pending meal remains");
            f.Tick(1260); Check(f.Manager.Requests.Count == 0, "bounded recovery still retires normally");
        });
        Test("a custody change retires the old meal without starting an automatic eating job", () =>
        {
            foreach (Action<PrisonFixture> change in new Action<PrisonFixture>[] {
                f => { f.Child.IsPrisonerOfColony = false; f.Child.IsColonist = true; f.Child.HostFaction = null; f.Child.Faction = Faction.OfPlayer; },
                f => { f.Child.IsPrisonerOfColony = false; f.Child.IsColonist = false; f.Child.HostFaction = null; },
                f => { f.Child.IsPrisonerOfColony = false; f.Child.IsSlaveOfColony = true; },
                f => f.Child.HostFaction = new Faction() })
            {
                var f = new PrisonFixture(); var request = f.Waiting(); int starts = f.Child.jobs.Starts;
                change(f); f.Tick(60); f.Tick(120);
                Check(!request.recoverMeal && !request.IsActive && f.Manager.Requests.Count == 0,
                    "old custody contract is not reused as a new identity's recovery meal");
                Check(f.Child.jobs.Starts == starts && f.Child.carryTracker.CarriedThing == f.Food,
                    "food preserved while the new identity's native AI owns the next choice");
            }
        });
        Test("a custody change after cancellation also prevents late recovery eating", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting(); f.Manager.StartFloorFallback(request, false);
            f.Child.IsPrisonerOfColony = false; f.Child.IsColonist = true; f.Child.HostFaction = null; f.Child.Faction = Faction.OfPlayer;
            Check(!f.Manager.TryMakeRecoveryJob(f.Child, out _), "retired snapshot is rechecked before immediate recovery prefix");
            int starts = f.Child.jobs.Starts; f.Tick(60);
            Check(f.Child.jobs.Starts == starts && f.Manager.Requests.Count == 0, "map recovery also leaves the new identity's AI alone");
        });
        Test("a custody change during return yields to native care rather than the old prison destination", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            f.Manager.StartFloorFallback(request, false); f.Tick(60);
            Check(f.Warden.CurJobDef == RDE_DefOf.RDE_ReturnDiningChild, "initial legal custody has a return");
            f.Child.HostFaction = new Faction();
            Check(!RatDiningCarryRecovery.TryMakeReturnJob(request, out _), "changed host cannot plan an old-contract return");
            f.Tick(120);
            Check(request.careHandoff && f.Warden.CurJobDef == JobDefOf.BringBabyToSafety,
                "the current return does not wait out its old destination deadline");
            Check(f.Warden.carryTracker.CarriedThing == f.Child && f.Child.carryTracker.CarriedThing == f.Food,
                "native care gets the carried child and the same meal");
        });
        Test("native release flag retires the old meal before guest status changes", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting(); int starts = f.Child.jobs.Starts;
            f.Child.guest.Released = true;
            Check(request.custody.Matches(f.Child) && !RatDiningCustody.CanDine(f.Child),
                "faction/status snapshot alone does not detect native release");
            f.Tick(60); f.Tick(120);
            Check(!request.recoverMeal && f.Manager.Requests.Count == 0 && f.Child.jobs.Starts == starts,
                "release is not interrupted by an automatic floor meal");
            Check(f.Child.carryTracker.CarriedThing == f.Food, "original food remains with its holder");
        });
        Test("release medical rest and critical hunger end an ongoing routine return", () =>
        {
            foreach (Action<PrisonFixture> emergency in new Action<PrisonFixture>[] {
                f => f.Child.guest.Released = true,
                f => f.Child.MedicalRest = true,
                f => f.Child.needs.food.CurLevelPercentage = .01f })
            {
                var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
                f.Manager.StartFloorFallback(request, false); f.Tick(60);
                var driver = new JobDriver_ReturnDiningChild(); Attach(driver, f.Warden);
                emergency(f);
                Check(!RatDiningCarryRecovery.TryMakeReturnJob(request, out _), "no routine prison return takes emergency priority");
                Check(driver.Failures.Any(fail => fail()), "ongoing return fails its next safety check");
                f.Tick(120);
                Check(request.careHandoff && f.Warden.CurJobDef == JobDefOf.BringBabyToSafety
                    && f.Warden.carryTracker.CarriedThing == f.Child && f.Child.carryTracker.CarriedThing == f.Food,
                    "native care receives both child and original food without waiting out the return deadline");
            }
        });
        Test("prisoner meal can traverse a corridor while the helper owns its legal destination", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            Check(RatDiningCustody.ValidRequest(request), "authorized carrying does not require standing inside prison each tick");
            f.Tick(60); Check(request.IsActive && f.Warden.carryTracker.CarriedThing == f.Child, "no false cancellation during transit");
        });
        Test("cancelled carry outside prison returns without dropping the child in the corridor", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            RatDiningMod.Settings.enableToddlerHelp = false; f.Tick(60);
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "chair claims immediately released");
            f.Tick(120);
            Check(f.Warden.CurJobDef == RDE_DefOf.RDE_ReturnDiningChild && !f.Child.Spawned && request.returnAttempts == 1, "one carried return");
            var returnDriver = new JobDriver_ReturnDiningChild(); var returnToils = Attach(returnDriver, f.Warden); Run(returnToils, 0, 1);
            Check(!returnDriver.Failures.Any(fail => fail()), "placement cannot fail because the child is no longer carried");
            Check(f.Child.Spawned && f.Child.Position.GetRoom(f.Map) == f.Prison && f.Child.carryTracker.CarriedThing == f.Food, "put down in prison with same food");
            f.Tick(180); Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "meal resumes even when help disabled");
            f.Warden.jobs.EndCurrentJob(JobCondition.Succeeded, false);
            Check(f.Map.reservationManager.Reservations.Count == 0, "return reservations end");
        });
        Test("real engine error waiting keeps the child's original held meal", () =>
        {
            foreach (JobCondition condition in new[] { JobCondition.Errored, JobCondition.ErroredPather })
            {
                var f = new Fixture(); f.Waiting(); Attach(new JobDriver_ToddlerWaitForDiningHelp(), f.Child);
                int starts = f.Child.jobs.Starts; f.Child.jobs.EndCurrentJob(condition);
                Check(f.Child.CurJobDef == JobDefOf.Wait && f.Child.jobs.Starts == starts + 1, "engine first starts its error wait");
                Check(f.Child.carryTracker.CarriedThing == f.Food && !f.Food.Spawned, "no error-wait food drop");
                f.Tick(60); Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, "deferred original meal recovery");
            }
        });
        Test("helper error waiting outside prison becomes a carried return", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            f.Warden.jobs.EndCurrentJob(JobCondition.ErroredPather);
            Check(f.Warden.CurJobDef == RDE_DefOf.RDE_ReturnDiningChild && f.Warden.carryTracker.CarriedThing == f.Child, "error wait replaced before unsafe drop");
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair) && request.returnAttempts == 1, "claims released once");
        });
        Test("return retries are bounded and then hand custody to Toddlers rescue", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            f.Manager.StartFloorFallback(request, false);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                f.Tick(60 + attempt * 60);
                Check(f.Warden.CurJobDef == RDE_DefOf.RDE_ReturnDiningChild && request.returnAttempts == attempt + 1, "one bounded attempt");
                f.Warden.jobs.EndCurrentJob(JobCondition.Incompletable, false);
            }
            f.Tick(240);
            Check(f.Warden.CurJobDef == JobDefOf.BringBabyToSafety && f.Warden.carryTracker.CarriedThing == f.Child, "native rescue gets carried child");
            Check(request.careHandoff && !request.recoveryPending && f.Manager.Requests.Count == 0, "no remaining dining wait");
            Check(f.Manager.GetRecoveringForPawn(f.Child) == null && !f.Manager.IsChairClaimed(f.Chair), "all dining indexes retire");
        });
        Test("a single stalled return has a finite deadline", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            f.Warden.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 250));
            var driver = new JobDriver_ReturnDiningChild(); var toils = Attach(driver, f.Warden);
            Find.TickManager.TicksGame = 600; toils[0].tickIntervalAction(600);
            Check(driver.Ended && f.Warden.carryTracker.CarriedThing == f.Child, "timeout retains carried child");
            f.Tick(1260);
            Check(f.Warden.CurJobDef == JobDefOf.BringBabyToSafety && request.careHandoff && !request.recoveryPending, "overall recovery also finite");
        });
        Test("no legal return target relinquishes dining without an unsafe direct drop", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500); f.Map.Rooms.Clear();
            f.Warden.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 250));
            Check(f.Warden.CurJobDef == JobDefOf.BringBabyToSafety && !f.Child.Spawned && f.Warden.carryTracker.CarriedThing == f.Child, "native rescue owns no-place case");
            Check(request.careHandoff && !request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "RDE does not hold a prison claim forever");
            f.Tick(60); Check(f.Manager.Requests.Count == 0, "retired record removed");
        });
        Test("removed prison designation still allows a safe emergency drop in the same room", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Prison.IsPrisonCell = false; f.Tick(60);
            Check(f.Child.Spawned && f.Child.Position.GetRoom(f.Map) == f.Prison && !request.IsActive, "same physical room safe drop");
            Check(f.Child.carryTracker.CarriedThing == f.Food, "original food retained");
        });
        Test("Toddlers' alternative safe prison placement is validated before returning", () =>
        {
            var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
            foreach (int cell in new[] { 9, 10, 11, 19, 20, 21, 110, 119, 120, 18, 118 }) f.Map.BlockedCells.Add(cell);
            f.Map.Rooms[700] = new Room { IsPrisonCell = true };
            Toddlers.BabyMoveUtility.TestBestPlace = new IntVec3(700);
            f.Warden.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 60));
            Check(f.Warden.CurJobDef == RDE_DefOf.RDE_ReturnDiningChild && f.Warden.CurJob.targetB.Cell.x == 700, "safe native candidate selected");
            var toils = Attach(new JobDriver_ReturnDiningChild(), f.Warden); Run(toils, 0, 1);
            Check(f.Child.Position.x == 700 && f.Child.Spawned, "placed in a valid new prison cell");
        });
        Test("explicit rescue and manual jobs preserve priority while dining ownership ends", () =>
        {
            foreach (bool forced in new[] { false, true })
            {
                var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
                Job care = JobMaker.MakeJob(new JobDef { defName = "OtherRescue" }, f.Child); care.playerForced = forced;
                f.Warden.jobs.StartJob(care, JobCondition.InterruptForced);
                Check(f.Warden.CurJob == care && f.Warden.carryTracker.CarriedThing == f.Child, "original care job and carried child unchanged");
                Check(request.careHandoff && !request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "old request released");
                f.Tick(60); Check(f.Warden.CurJob == care && f.Manager.Requests.Count == 0, "recovery never overrides rescue");
            }
        });
        Test("child death and helper inability retire dining into ordinary game care", () =>
        {
            foreach (bool childDead in new[] { false, true })
            {
                var f = new PrisonFixture(); var request = CarryPrisoner(f); f.Warden.Position = new IntVec3(500);
                if (childDead) f.Child.Dead = true; else f.Warden.Downed = true;
                f.Tick(60); f.Tick(120);
                Check(!request.IsActive && !request.recoveryPending && !f.Manager.IsChairClaimed(f.Chair), "no impossible dining wait");
                Check(f.Manager.Requests.Count == 0, "retire record");
            }
        });
        Test("failed inventory food release is bounded and never destroys the original split", () =>
        {
            var f = new PrisonFixture(); f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
            var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Warden); Run(toils, 0, 2);
            var request = f.Manager.GetForPawn(f.Child); Thing split = request.food;
            f.Warden.inventory.innerContainer.DropFails = true; f.Manager.StartFloorFallback(request, false); f.Tick(60);
            Check(request.recoveryPending && f.Warden.inventory.Contains(split), "temporary engine drop failure retains food");
            f.Tick(1260);
            Check(!request.recoveryPending && f.Manager.Requests.Count == 0 && f.Warden.inventory.Contains(split) && split.stackCount == 4 && !split.Destroyed, "bounded release retains original food");
        });
        Test("manual care handoff never creates an automatic recovery meal", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting(); f.Manager.HandOffCare(request);
            Check(!f.Manager.TryMakeRecoveryJob(f.Child, out _), "no food task after handoff even before the next map tick");
        });
        Test("legacy recovery defaults and new retry fields survive save values", () =>
        {
            var request = new RatToddlerDiningRequest { recoverMeal = false, careHandoff = true, recoveryStartedTick = 400, returnAttempts = 2, returnJobId = 73 };
            Scribe.mode = LoadSaveMode.Saving; request.ExposeData();
            var loaded = new RatToddlerDiningRequest(); Scribe.mode = LoadSaveMode.LoadingVars; loaded.ExposeData();
            Check(!loaded.recoverMeal && loaded.careHandoff && loaded.recoveryStartedTick == 400 && loaded.returnAttempts == 2 && loaded.returnJobId == 73, "saved limits cannot reset on load");
            Scribe_Values.Values.Clear(); var legacy = new RatToddlerDiningRequest(); legacy.ExposeData();
            Check(legacy.recoverMeal && !legacy.careHandoff && legacy.recoveryStartedTick == -1 && legacy.returnAttempts == 0, "old saves start with safe defaults");
        });
    }
}
