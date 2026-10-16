using System;
using System.Linq;
using System.Reflection;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private sealed class PrisonFixture
    {
        internal readonly Map Map = new Map();
        internal readonly Pawn Child;
        internal readonly Pawn Warden;
        internal readonly Thing Food;
        internal readonly Building Chair;
        internal readonly Room Prison = new Room { IsPrisonCell = true };
        internal MapComponent_RatToddlerDining Manager => Map.Manager;
        internal PrisonFixture(bool toddler = true, bool enableHelp = true)
        {
            if (enableHelp) RatDiningMod.Settings.enablePrisonerToddlerHelp = true;
            Map.Manager = new MapComponent_RatToddlerDining(Map);
            Child = new Pawn { Map = Map, IsColonist = false, IsPrisonerOfColony = true, HostFaction = Faction.OfPlayer,
                Faction = new Faction(), Toddler = toddler };
            Warden = new Pawn { Map = Map, Toddler = false };
            Food = new Thing { Map = Map, stackCount = 10 };
            Chair = Furniture(Map, 20, true, false);
            foreach (int cell in new[] { 9, 10, 11, 19, 20, 21, 110, 119, 120 }) Map.Rooms[cell] = Prison;
            Map.mapPawns.AllPawnsSpawned.Add(Child);
            Map.mapPawns.SlavesAndPrisonersOfColonySpawned.Add(Child);
            FoodUtility.BabyFood = Food;
        }
        internal RatToddlerDiningRequest Waiting()
        {
            var request = Manager.Create(Child, Food, Chair, null, new IntVec3(19));
            Check(request != null, "legal prisoner request");
            Child.carryTracker.CarriedThing = Food; Food.Spawned = false;
            Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForDiningHelp, Food, Chair, new IntVec3(19)), keepCarryingThingOverride: true);
            Manager.SetState(request, RatToddlerDiningState.WaitingForHelp);
            return request;
        }
        internal void Tick(int tick) { Find.TickManager.TicksGame = tick; Manager.MapComponentTick(); }
    }

    private static Job EvaluateIngest(Pawn pawn, Thing food, int count = 4)
    {
        var original = JobMaker.MakeJob(JobDefOf.Ingest, food); original.count = count;
        object[] args = { pawn, original };
        typeof(JobGiver_GetFood_Patch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        return (Job)args[1];
    }

    private static bool EvaluateBabyPlay(Pawn child, bool forced = false, bool nativeResult = true)
    {
        object[] arguments = { child, forced, nativeResult };
        typeof(WorkGiver_PlayWithBaby_DeferDiningRequest).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, arguments);
        return (bool)arguments[2];
    }

    private static void CustodyAndSafetyTests()
    {
        Test("active prisoner meal selection respects its caregiver's existing chair reservation", () =>
        {
            foreach (bool provide in new[] { false, true })
            {
                var f = new PrisonFixture(); f.Child.needs.food.CurLevelPercentage = .2f;
                if (!provide) f.Waiting();
                f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
                JobDriver driver = provide ? (JobDriver)new JobDriver_ProvideRatToddlerDiningMeal() : new JobDriver_AssistRatToddlerDining();
                Run(Attach(driver, f.Warden), 0, 0);
                var request = f.Manager.GetForPawn(f.Child);
                Check(!f.Child.CanReserve(f.Chair) && request.helper == f.Warden, "approved caregiver owns this chair reservation");
                Job selected = EvaluateIngest(f.Child, f.Food);
                Check(selected.def == (provide ? RDE_DefOf.RDE_ToddlerWaitForMeal : RDE_DefOf.RDE_ToddlerWaitForDiningHelp)
                    && request.IsActive && f.Manager.GetForPawn(f.Child) == request && f.Manager.IsChairClaimed(f.Chair),
                    "same request remains active while the helper has reserved its destination");
                Check(selected.targetA.Thing == request.food && selected.targetB.Thing == f.Chair,
                    "child waits for the same original meal and destination");
            }
        });
        Test("prisoner food selection checks a stale request before querying its staging grid", () =>
        {
            foreach (IntVec3 cell in new[] { IntVec3.Invalid, new IntVec3(10000) })
            {
                var f = new PrisonFixture(); f.Child.needs.food.CurLevelPercentage = .2f;
                var request = f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
                request.stagingCell = cell;
                Job selected = EvaluateIngest(f.Child, f.Food);
                Check(selected.def == JobDefOf.Ingest && !request.IsActive && !f.Manager.IsChairClaimed(f.Chair),
                    "invalid old request retires before native reservation query");
                Check(f.Food.Spawned && f.Food.stackCount == 10, "original food untouched");
            }
        });
        Test("prisoner food selection does not revive a request after safety or settings change", () =>
        {
            foreach (Action<PrisonFixture> change in new Action<PrisonFixture>[] {
                f => f.Child.guest.Released = true,
                f => f.Child.MedicalRest = true,
                f => RatDiningMod.Settings.enableToddlerHelp = false })
            {
                RatDiningMod.Settings.enableToddlerHelp = true;
                var f = new PrisonFixture(); f.Child.needs.food.CurLevelPercentage = .2f;
                var request = f.Waiting(); change(f);
                Check(EvaluateIngest(f.Child, f.Food).def == JobDefOf.Ingest && !request.IsActive,
                    "current safety takes precedence over old waiting state");
                Check(f.Child.carryTracker.CarriedThing == f.Food && !f.Manager.IsChairClaimed(f.Chair), "food preserved and chair released");
            }
        });
        Test("invalid prisoner provision food no longer defers native feeding or play", () =>
        {
            foreach (bool policy in new[] { false, true })
            {
                var f = new PrisonFixture(); f.Child.DevelopmentalStage = DevelopmentalStage.Baby;
                f.Child.needs.food.CurLevelPercentage = .2f;
                f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
                Check(RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(f.Child), "valid provision owns this meal");
                if (policy) f.Child.DisallowedFood.Add(f.Food.def); else f.Food.def.ingestible.babiesCanIngest = false;
                Check(!RatToddlerMealCoordinator.ShouldDeferAutomaticFeeding(f.Child) && EvaluateBabyPlay(f.Child),
                    "invalid provision cannot suppress native care before next map tick");
            }
        });
        Test("owned vanilla fallback rechecks baby food policy and pending prisoner release", () =>
        {
            foreach (Action<PrisonFixture> change in new Action<PrisonFixture>[] {
                f => f.Food.def.ingestible.babiesCanIngest = false,
                f => f.Child.DisallowedFood.Add(f.Food.def),
                f => f.Child.guest.Released = true })
            {
                var f = new PrisonFixture(); f.Child.DevelopmentalStage = DevelopmentalStage.Baby;
                Job job = JobMaker.MakeJob(JobDefOf.Ingest, f.Food);
                f.Child.jobs.StartJob(job); f.Manager.TrackVanillaMeal(f.Child, job);
                f.Child.carryTracker.CarriedThing = f.Food; f.Food.Spawned = false;
                var driver = new JobDriver_Ingest { pawn = f.Child, job = job }; f.Child.jobs.curDriver = driver;
                change(f);
                var move = Toils_Ingest.CarryIngestibleToChewSpot(f.Child, TargetIndex.A); move.actor = f.Child; move.initAction();
                Check(driver.Ended && !f.Food.Destroyed && f.Manager.Requests.Count == 0,
                    "ineligible fallback ends without eating or creating a request");
                Check(!RatDiningSpotSafety.IsSelecting(f.Child), "fallback scope disposed after rejection");
                f.Tick(60); Check(f.Manager.GetVanillaMeal(f.Child) == null, "ended context removed");
            }
        });
        Test("prisoner preparation rejects off-map job staging before any grid reservation", () =>
        {
            var f = new PrisonFixture(); var request = f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
            Job job = JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerPrepareDiningRequest, f.Food, f.Chair, new IntVec3(f.Map.CellCount));
            f.Child.jobs.StartJob(job); var driver = new JobDriver_ToddlerPrepareDiningRequest { pawn = f.Child, job = job };
            Check(!driver.TryMakePreToilReservations(false), "invalid target rejected before native grid access");
            Check(!request.IsActive && f.Manager.GetForPawn(f.Child) == null && f.Map.reservationManager.Reservations.Count == 0,
                "failed request and partial food reservation released");
            Check(f.Food.Spawned && f.Food.stackCount == 10, "original ground meal preserved");
        });
        Test("prisoner waiting rejects invalid and off-map saved staging without losing its meal", () =>
        {
            foreach (IntVec3 cell in new[] { IntVec3.Invalid, new IntVec3(10000), new IntVec3(110) })
            {
                var f = new PrisonFixture(); var request = f.Waiting(); Job job = f.Child.CurJob; job.SetTarget(TargetIndex.C, cell);
                var driver = new JobDriver_ToddlerWaitForDiningHelp { pawn = f.Child, job = job };
                Check(!driver.TryMakePreToilReservations(false), "staging must match the bounded request");
                Check(!request.IsActive && f.Map.reservationManager.Reservations.Count == 0 && f.Child.carryTracker.CarriedThing == f.Food,
                    "chair released and original held meal preserved");
            }
        });
        Test("prisoner child preflight rejects a different job food reference", () =>
        {
            foreach (bool waiting in new[] { false, true })
            {
                var f = new PrisonFixture(); var request = waiting ? f.Waiting()
                    : f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
                var other = new Thing { Map = f.Map };
                Job job = JobMaker.MakeJob(waiting ? RDE_DefOf.RDE_ToddlerWaitForDiningHelp : RDE_DefOf.RDE_ToddlerPrepareDiningRequest,
                    other, f.Chair, new IntVec3(19));
                f.Child.jobs.StartJob(job, keepCarryingThingOverride: true);
                JobDriver driver = waiting ? (JobDriver)new JobDriver_ToddlerWaitForDiningHelp() : new JobDriver_ToddlerPrepareDiningRequest();
                driver.pawn = f.Child; driver.job = job;
                Check(!driver.TryMakePreToilReservations(false) && !request.IsActive && f.Map.reservationManager.Reservations.Count == 0,
                    "stale food cannot hijack a bound request");
            }
        });
        Test("loaded prisoner provider preflight rejects a different chair", () =>
        {
            var f = new PrisonFixture(); f.Child.needs.food.CurLevelPercentage = .2f;
            Job job = new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child);
            f.Warden.jobs.StartJob(job); var request = f.Manager.GetForPawn(f.Child); Check(request != null, "provision request started");
            job.SetTarget(TargetIndex.B, Furniture(f.Map, 30, true, false));
            var driver = new JobDriver_ProvideRatToddlerDiningMeal { pawn = f.Warden, job = job };
            Check(!driver.TryMakePreToilReservations(false) && !request.IsActive && f.Map.reservationManager.Reservations.Count == 0,
                "mismatched job cannot reserve or place child outside its request chair");
        });
        Test("prisoner helper guards reject a changed target before carrying or placing", () =>
        {
            foreach (bool provide in new[] { false, true })
            {
                var f = new PrisonFixture(); if (!provide) f.Waiting(); else f.Child.needs.food.CurLevelPercentage = .2f;
                f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
                JobDriver driver = provide ? (JobDriver)new JobDriver_ProvideRatToddlerDiningMeal() : new JobDriver_AssistRatToddlerDining();
                var toils = Attach(driver, f.Warden); Run(toils, 0, 0);
                Check(!driver.Failures.Any(check => check()), "valid helper remains active");
                f.Warden.CurJob.SetTarget(TargetIndex.B, Furniture(f.Map, 30, true, false));
                Check(driver.Failures.Any(check => check()), "changed chair fails before action");
                driver.EndJobWith(JobCondition.Incompletable); f.Tick(60);
                Check(f.Manager.GetForPawn(f.Child)?.helper != f.Warden && f.Map.reservationManager.Reservations.Count == 0,
                    "helper unbound and reservations released; a valid child may resume waiting");
            }
        });
        Test("loaded provided-meal wait rejects a chair or food outside its bound request", () =>
        {
            foreach (TargetIndex target in new[] { TargetIndex.A, TargetIndex.B })
            {
                var f = new PrisonFixture(); f.Child.needs.food.CurLevelPercentage = .2f;
                f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
                var helper = new JobDriver_ProvideRatToddlerDiningMeal(); Run(Attach(helper, f.Warden), 0, 0);
                var request = f.Manager.GetForPawn(f.Child); Job job = f.Child.CurJob;
                job.SetTarget(target, target == TargetIndex.A ? (Thing)new Thing { Map = f.Map } : Furniture(f.Map, 30, true, false));
                var driver = new JobDriver_ToddlerWaitForMeal { pawn = f.Child, job = job };
                Check(!driver.TryMakePreToilReservations(false) && !request.IsActive,
                    "loaded waiting targets validated before toils are installed");
                Check(f.Map.reservationManager.Reservations.Count == 0 && f.Food.stackCount == 10,
                    "provider reservations released and original food untouched");
            }
        });
        Test("active prisoner preparation and waiting reject staging changes before pathing", () =>
        {
            foreach (bool waiting in new[] { false, true })
            {
                var f = new PrisonFixture(); var request = waiting ? f.Waiting()
                    : f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19));
                if (!waiting) f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerPrepareDiningRequest,
                    f.Food, f.Chair, new IntVec3(19)));
                JobDriver driver = waiting ? (JobDriver)new JobDriver_ToddlerWaitForDiningHelp() : new JobDriver_ToddlerPrepareDiningRequest();
                Attach(driver, f.Child); Check(!driver.Failures.Any(check => check()), "valid staging accepted");
                f.Child.CurJob.SetTarget(TargetIndex.C, new IntVec3(f.Map.CellCount));
                Check(driver.Failures.Any(check => check()), "invalid current staging fails before path action");
                driver.EndJobWith(JobCondition.Incompletable);
                Check(!request.IsActive && f.Map.reservationManager.Reservations.Count == 0, "request and reservations released");
            }
        });
        Test("baby prisoner cannot start a chair request for adult-only food", () =>
        {
            var f = new PrisonFixture(); f.Child.DevelopmentalStage = DevelopmentalStage.Baby;
            f.Child.needs.food.CurLevelPercentage = .2f; f.Food.def.ingestible.babiesCanIngest = false;
            Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null, "baby food rule at public request entrance");
            Check(EvaluateIngest(f.Child, f.Food).def == JobDefOf.Ingest && f.Manager.Requests.Count == 0,
                "bad upstream proposal remains native without a dining claim");
            Check(!new WorkGiver_Warden_AssistRatToddlerDining().HasJobOnThing(f.Warden, f.Child),
                "adult getter cannot make adult-only food suitable for the baby");
            Job feed = JobMaker.MakeJob(JobDefOf.FeedPatient, f.Food, f.Child);
            feed.workGiverDef = new WorkGiverDef { giverClass = typeof(WorkGiver_Warden_Feed) };
            f.Warden.jobs.StartJob(feed);
            Check(f.Warden.CurJob == feed && f.Manager.Requests.Count == 0, "invalid food is not redirected to our self-feeding flow");
        });
        Test("a valid baby food need not be named BabyFood", () =>
        {
            var f = new PrisonFixture(); f.Child.DevelopmentalStage = DevelopmentalStage.Baby;
            f.Food.def.defName = "CompatibleBabyMilk";
            Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) != null,
                "native food permission supports milk or other compatible food without a name whitelist");
        });
        Test("changed baby food permission retires the request without forcing an invalid recovery meal", () =>
        {
            foreach (bool policyChange in new[] { false, true })
            {
                var f = new PrisonFixture(); f.Child.DevelopmentalStage = DevelopmentalStage.Baby;
                var request = f.Waiting(); int starts = f.Child.jobs.Starts;
                if (policyChange) f.Child.DisallowedFood.Add(f.Food.def);
                else f.Food.def.ingestible.babiesCanIngest = false;
                f.Tick(60); f.Tick(120);
                Check(!request.IsActive && f.Manager.Requests.Count == 0 && !f.Manager.IsChairClaimed(f.Chair),
                    "food permission is rechecked while waiting");
                Check(f.Child.jobs.Starts == starts && f.Child.carryTracker.CarriedThing == f.Food,
                    "original food retained without an invalid floor job");
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor, f.Food), keepCarryingThingOverride: true);
                Check(!new JobDriver_ToddlerEatHeldFoodOnFloor { pawn = f.Child, job = f.Child.CurJob }.TryMakePreToilReservations(false),
                    "saved floor job validates the actual eater too");
            }
        });
        Test("automatic native play defers a prisoner's active safe meal and resumes after cancellation", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting();
            Check(!EvaluateBabyPlay(f.Child), "routine play cannot steal a held meal");
            Check(f.Manager.GetForPawn(f.Child) == request && f.Child.carryTracker.CarriedThing == f.Food,
                "work scan changes no request or food ownership");
            Check(EvaluateBabyPlay(f.Child, forced: true), "manual play order remains available");
            f.Manager.DebugCancel(f.Child);
            Check(EvaluateBabyPlay(f.Child), "retired request no longer restricts play");
        });
        Test("automatic play guard applies to colony meals without inventing a native play job", () =>
        {
            var f = new PrisonFixture();
            f.Child.IsPrisonerOfColony = false; f.Child.IsColonist = true;
            f.Child.Faction = Faction.OfPlayer; f.Child.HostFaction = null;
            f.Waiting();
            Check(!EvaluateBabyPlay(f.Child), "colony child also finishes its meal first");
            Check(!EvaluateBabyPlay(f.Child, nativeResult: false), "native rejection remains false");
        });
        Test("native play and emergency care stay unchanged outside safe Ratkin requests", () =>
        {
            var f = new PrisonFixture();
            Check(EvaluateBabyPlay(f.Child), "no active request is unchanged");
            f.Waiting();
            f.Child.needs.food.CurLevelPercentage = .01f;
            Check(EvaluateBabyPlay(f.Child), "critical hunger is not protected by routine meal deferral");
            f.Child.needs.food.CurLevelPercentage = .5f; f.Child.MedicalRest = true;
            Check(EvaluateBabyPlay(f.Child), "medical care remains available");
            f.Child.MedicalRest = false; f.Child.UnsafeTemperature = true;
            Check(EvaluateBabyPlay(f.Child), "unsafe location remains available to care");
            f.Child.UnsafeTemperature = false; f.Child.def = new ThingDef { defName = "Human" };
            Check(EvaluateBabyPlay(f.Child), "other races are untouched even with a stale index");
        });
        Test("prisoner self-food entry uses a chair and staging cell in its prison room", () =>
        {
            var f = new PrisonFixture();
            f.Child.needs.food.CurLevelPercentage = .2f;
            Job job = EvaluateIngest(f.Child, f.Food);
            Check(job.def == RDE_DefOf.RDE_ToddlerPrepareDiningRequest && job.count == 4, "prisoner can request help");
            var request = f.Manager.GetForPawn(f.Child);
            Check(request.custody.prisoner && request.custody.originCell == f.Child.Position, "original custody captured");
            Check(job.targetB.Thing == f.Chair && job.targetC.Cell.GetRoom(f.Map) == f.Prison, "same room only");
        });
        Test("satiated captive food proposals create no help loop or chair claim", () =>
        {
            foreach (float level in new[] { .3f, .9f, 1f })
            {
                var f = new PrisonFixture(); f.Child.needs.food.CurLevelPercentage = level;
                Job proposal = EvaluateIngest(f.Child, f.Food);
                Check(proposal.def == JobDefOf.Ingest && proposal.targetA.Thing == f.Food,
                    "ordinary eating proposal remains unchanged at the native hunger boundary");
                Check(f.Manager.Requests.Count == 0 && !f.Manager.IsChairClaimed(f.Chair),
                    "no help request competes with normal care while satiated");
            }
        });
        Test("prisoner requests belong to Warden work and not Childcare", () =>
        {
            var f = new PrisonFixture(); f.Waiting(); f.Warden.workSettings.Childcare = false;
            var childcare = new WorkGiver_AssistRatToddlerDining(); var warden = new WorkGiver_Warden_AssistRatToddlerDining();
            Check(!childcare.HasJobOnThing(f.Warden, f.Child) && !childcare.PotentialWorkThingsGlobal(f.Warden).Any(), "no childcare stealing");
            Check(warden.HasJobOnThing(f.Warden, f.Child) && warden.PotentialWorkThingsGlobal(f.Warden).Contains(f.Child), "warden route available");
            f.Warden.workSettings.Warden = false;
            Check(!warden.HasJobOnThing(f.Warden, f.Child), "warden assignment respected");
        });
        Test("prisoner caregivers respect incapable Warden work and parent settings", () =>
        {
            var f = new PrisonFixture(); f.Waiting(); var giver = new WorkGiver_Warden_AssistRatToddlerDining();
            f.Warden.DisabledWork.Add(WorkTypeDefOf.Warden); Check(!giver.HasJobOnThing(f.Warden, f.Child), "disabled work");
            f.Warden.DisabledWork.Clear(); RatDiningMod.Settings.allowNonParents = false;
            Check(!giver.HasJobOnThing(f.Warden, f.Child), "parent setting");
            f.Child.relations.Parents.Add(f.Warden); Check(giver.HasJobOnThing(f.Warden, f.Child), "warden parent can help");
        });
        Test("prisoner scans plan meals without taking claims or splitting food", () =>
        {
            var f = new PrisonFixture(); var giver = new WorkGiver_Warden_AssistRatToddlerDining();
            Check(giver.HasJobOnThing(f.Warden, f.Child), "fresh meal planned");
            Job job = giver.JobOnThing(f.Warden, f.Child);
            Check(job.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && job.count == 4, "planned supply job");
            Check(f.Manager.Requests.Count == 0 && f.Map.reservationManager.Reservations.Count == 0 && f.Food.stackCount == 10, "read-only scan");
            f.Warden.jobs.StartJob(job);
            Check(f.Manager.GetForPawn(f.Child)?.helper == f.Warden, "claim on actual start");
        });
        Test("prisoner provided meal completes through carrying and normal nutrition", () =>
        {
            var f = new PrisonFixture(); var giver = new WorkGiver_Warden_AssistRatToddlerDining();
            f.Warden.jobs.StartJob(giver.JobOnThing(f.Warden, f.Child));
            var driver = new JobDriver_ProvideRatToddlerDiningMeal(); var toils = Attach(driver, f.Warden);
            foreach (Toil toil in toils) { Check(!driver.Failures.Any(fail => fail()), "prisoner provision stays legal"); toil.initAction?.Invoke(); }
            Check(f.Child.Spawned && f.Child.Position == f.Chair.Position && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair, "legal placement");
            var eat = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Run(eat, 0, eat.Count - 1);
            Check(f.Child.needs.food.CurLevelPercentage == 1f && f.Food.stackCount == 6, "four raw portions consumed");
            Check(!f.Manager.IsChairClaimed(f.Chair), "completed claim released");
        });
        Test("warden provision accepts prison food proper for its captive recipient, not the getter", () =>
        {
            var f = new PrisonFixture(); f.Food.ImproperTo.Add(f.Warden);
            Check(!RatToddlerDiningUtility.TryGetPickupCount(f.Warden, f.Food, 4, out _),
                "ordinary getter still cannot take a prison meal for itself");
            var giver = new WorkGiver_Warden_AssistRatToddlerDining();
            Job job = giver.JobOnThing(f.Warden, f.Child);
            Check(job?.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, "actual fresh warden scan plans recipient meal");
            f.Warden.jobs.StartJob(job);
            var driver = new JobDriver_ProvideRatToddlerDiningMeal(); var toils = Attach(driver, f.Warden);
            foreach (Toil toil in toils) { Check(!driver.Failures.Any(fail => fail()), "recipient pickup remains valid through supply"); toil.initAction?.Invoke(); }
            Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair, "prison food reaches the child's chair");
            var eat = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Run(eat, 0, eat.Count - 1);
            Check(f.Child.needs.food.CurLevelPercentage == 1f && f.Food.stackCount == 6, "recipient consumes exactly four portions");
            Check(!f.Manager.IsChairClaimed(f.Chair), "claim released after completed provision");
        });
        Test("recipient prison-food permission retains race, work, forbidden and reservation checks", () =>
        {
            foreach (Action<PrisonFixture> alter in new Action<PrisonFixture>[] {
                f => f.Child.IsPrisonerOfColony = false,
                f => f.Child.def = new ThingDef { defName = "Human" },
                f => f.Warden.workSettings.Warden = false,
                f => f.Food.Forbidden = true,
                f => f.Food.Improper = true,
                f => f.Map.reservationManager.Reserve(new Pawn { Map = f.Map }, JobMaker.MakeJob(JobDefOf.Ingest), f.Food, 10, 10, false) })
            {
                var f = new PrisonFixture(); f.Food.ImproperTo.Add(f.Warden); alter(f);
                Check(!RatToddlerDiningUtility.TryGetPickupCount(f.Warden, f.Food, 4, out _, f.Child),
                    "recipient context cannot bypass ordinary safety or ownership");
                Check(f.Manager.Requests.Count == 0, "pickup checks acquire no dining request");
            }
        });
        Test("competing wardens cannot start two planned meals", () =>
        {
            var f = new PrisonFixture(); var other = new Pawn { Map = f.Map, Toddler = false };
            var giver = new WorkGiver_Warden_AssistRatToddlerDining();
            Job first = giver.JobOnThing(f.Warden, f.Child), second = giver.JobOnThing(other, f.Child);
            f.Warden.jobs.StartJob(first); other.jobs.StartJob(second);
            Check(f.Warden.CurJob == first && other.CurJobDef == JobDefOf.Wait && f.Manager.Requests.Count == 1, "start race bounded");
        });
        Test("prisoner assistance rejects unsecured forbidden starving and medical cases", () =>
        {
            foreach (Action<PrisonFixture> alter in new Action<PrisonFixture>[] {
                f => f.Child.guest.PrisonerIsSecure = false, f => f.Child.guest.CanBeBroughtFood = false,
                f => f.Child.Forbidden = true, f => f.Child.needs.food.CurLevelPercentage = .01f,
                f => f.Child.MedicalRest = true, f => f.Child.SelfFeeding = false,
                f => f.Child.FormingCaravan = true, f => f.Child.HostFaction = new Faction() })
            {
                var f = new PrisonFixture(); alter(f);
                Check(!new WorkGiver_Warden_AssistRatToddlerDining().HasJobOnThing(f.Warden, f.Child), "routine help rejected");
                Check(f.Manager.Requests.Count == 0, "no stale claims");
            }
        });
        Test("prisoner chair cannot be chosen in another cell or outside prison", () =>
        {
            foreach (Room room in new[] { new Room { IsPrisonCell = true }, new Room(), null })
            {
                var f = new PrisonFixture(); f.Map.Rooms[20] = room;
                Check(!RatDiningSpotFinder.IsUsableChair(f.Child, f.Chair), "outside origin room");
                Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(19)) == null, "request excluded");
            }
        });
        Test("prisoner staging cells and manual reassignment remain in its original room", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting(); var outside = Furniture(f.Map, 30, true, false);
            Check(!f.Manager.TryReassignChair(request, outside, new IntVec3(29)), "illegal manual chair");
            f.Manager.Complete(request);
            Check(f.Manager.Create(f.Child, f.Food, f.Chair, null, new IntVec3(29)) == null, "illegal staging");
        });
        Test("prisoner release recruitment enslavement and host changes retire old requests", () =>
        {
            foreach (Action<PrisonFixture> alter in new Action<PrisonFixture>[] {
                f => f.Child.IsPrisonerOfColony = false,
                f => { f.Child.IsPrisonerOfColony = false; f.Child.IsColonist = true; f.Child.HostFaction = null; f.Child.Faction = Faction.OfPlayer; },
                f => { f.Child.IsPrisonerOfColony = false; f.Child.IsSlaveOfColony = true; },
                f => f.Child.HostFaction = new Faction(), f => f.Child.Faction = new Faction(),
                f => f.Prison.IsPrisonCell = false, f => f.Child.guest.CanBeBroughtFood = false })
            {
                var f = new PrisonFixture(); var request = f.Waiting(); alter(f); f.Tick(60);
                Check(!request.IsActive && f.Manager.GetForPawn(f.Child) == null && !f.Manager.IsChairClaimed(f.Chair), "retire and release");
                Check(f.Child.carryTracker.CarriedThing == f.Food, "original meal preserved");
            }
        });
        Test("cancelled prisoner carrying drops inside its cell and preserves the meal", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting();
            f.Warden.jobs.StartJob(new WorkGiver_Warden_AssistRatToddlerDining().JobOnThing(f.Warden, f.Child));
            var toils = Attach(new JobDriver_AssistRatToddlerDining(), f.Warden); Run(toils, 0, 3);
            Check(f.Warden.carryTracker.CarriedThing == f.Child, "carried");
            f.Warden.workSettings.Warden = false; f.Tick(60);
            Check(f.Child.Spawned && f.Child.Position.GetRoom(f.Map) == f.Prison && f.Child.carryTracker.CarriedThing == f.Food, "safe same-cell drop");
            Check(f.Map.reservationManager.Reservations.All(r => r.Pawn != f.Warden), "old helper reservations released");
        });
        Test("medical FeedPatient target layout is preserved and ends chair assistance", () =>
        {
            var f = new PrisonFixture(); var request = f.Waiting();
            Job medical = JobMaker.MakeJob(JobDefOf.FeedPatient, f.Food, f.Child);
            f.Warden.jobs.StartJob(medical);
            Check(f.Warden.CurJob == medical && RatToddlerMealCoordinator.FeedingTarget(medical) == f.Child, "A is food and B is patient");
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "medical care wins");
        });
        Test("automatic warden FeedPatient uses its correct targets when safely redirected", () =>
        {
            var f = new PrisonFixture(); Job job = JobMaker.MakeJob(JobDefOf.FeedPatient, f.Food, f.Child);
            job.workGiverDef = new WorkGiverDef { giverClass = typeof(WorkGiver_Warden_Feed) };
            f.Warden.jobs.StartJob(job);
            Check(f.Warden.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && f.Warden.CurJob.targetA.Pawn == f.Child
                && f.Warden.CurJob.targetC.Thing == f.Food, "warden feed is recognized");
        });
        Test("secure slave meal works while a non-colonist with player faction remains excluded", () =>
        {
            var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
            var child = new Pawn { Map = map, IsColonist = false, IsSlaveOfColony = true }; var food = new Thing { Map = map };
            var chair = Furniture(map, 20, true, false);
            RatDiningMod.Settings.enableSlaveToddlerHelp = true;
            Check(map.Manager.Create(child, food, chair, null, new IntVec3(19)) != null, "secure slave allowed");
            map.Manager.Complete(map.Manager.GetForPawn(child)); child.guest.SlaveIsSecure = false;
            Check(map.Manager.Create(child, food, chair, null, new IntVec3(19)) == null, "unsecured slave excluded");
            child.IsSlaveOfColony = false;
            Check(EvaluateIngest(child, food).def == JobDefOf.Ingest, "unsupported non-colonist player-faction pawn keeps original AI");
        });
        Test("adult prisoner raw meal preserves requested portions and actual nutrition", () =>
        {
            var f = new PrisonFixture(false); f.Child.needs.food.MaxLevel = 1f;
            Job job = JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, f.Food, f.Chair, new IntVec3(19)); job.count = 5;
            f.Child.jobs.StartJob(job); var toils = Attach(new JobDriver_EatUsingChairAsTable(), f.Child); Run(toils, 0, toils.Count - 1);
            Check(f.Food.stackCount == 5 && f.Child.needs.food.CurLevelPercentage == 1f, "five portions fill need");
        });
        Test("adult pickup clamps shared reservation and carrying capacity", () =>
        {
            var f = new PrisonFixture(false); f.Child.carryTracker.Capacity = 3;
            Job job = JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, f.Food, f.Chair, new IntVec3(19)); job.count = 8;
            f.Child.jobs.StartJob(job); var driver = new JobDriver_EatUsingChairAsTable(); var toils = Attach(driver, f.Child);
            Check(job.count == 3, "capacity clamped"); Run(toils, 0, toils.Count - 1);
            Check(f.Food.stackCount == 7 && f.Child.needs.food.CurLevelPercentage < 1f, "no fabricated full need from one portion");
        });
        Test("invalid preflight chew spot becomes a finite floor mode without grid access", () =>
        {
            var map = new Map(); var pawn = new Pawn { Map = map }; var food = new Thing { Map = map };
            Toils_Ingest.TestUseStandingSelector = true;
            Check(RatDiningModeResolver.TryResolve(pawn, food, out RatDiningMode mode, out _, out _, out _) && mode == RatDiningMode.FloorFallback, "sentinel caught before grid");
            Check(!RatDiningSpotSafety.IsSelecting(pawn), "scope disposed");
        });
        Test("invalid owned vanilla chew spot continues the same held meal", () =>
        {
            var f = new PrisonFixture(); Job job = JobMaker.MakeJob(JobDefOf.Ingest, f.Food);
            f.Child.jobs.StartJob(job); f.Manager.TrackVanillaMeal(f.Child, job);
            f.Child.carryTracker.CarriedThing = f.Food; f.Food.Spawned = false;
            f.Child.jobs.curDriver = new JobDriver_Ingest { pawn = f.Child, job = job };
            var move = Toils_Ingest.CarryIngestibleToChewSpot(f.Child, TargetIndex.A); move.actor = f.Child; move.initAction();
            Check(f.Child.CurJob == job && f.Child.carryTracker.CarriedThing == f.Food && f.Child.Position.x == 10, "same food same job current-cell path");
            Check(!RatDiningSpotSafety.IsSelecting(f.Child), "runtime scope disposed");
        });
        Test("unowned normal meals keep their original exception visible", () =>
        {
            var f = new PrisonFixture(); f.Child.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Ingest, f.Food));
            var move = Toils_Ingest.CarryIngestibleToChewSpot(f.Child, TargetIndex.A); move.actor = f.Child;
            bool threw = false; try { move.initAction(); } catch (IndexOutOfRangeException) { threw = true; }
            Check(threw, "unowned job is not intercepted");
        });
        Test("unexpected other-mod exceptions are never swallowed for an owned meal", () =>
        {
            var f = new PrisonFixture(); Job job = JobMaker.MakeJob(JobDefOf.Ingest, f.Food);
            f.Child.jobs.StartJob(job); f.Manager.TrackVanillaMeal(f.Child, job);
            Toils_Ingest.TestCarryAction = () => { throw new IndexOutOfRangeException("other mod"); };
            var move = Toils_Ingest.CarryIngestibleToChewSpot(f.Child, TargetIndex.A); move.actor = f.Child;
            bool threw = false; try { move.initAction(); } catch (IndexOutOfRangeException e) { threw = e.Message == "other mod"; }
            Check(threw && !RatDiningSpotSafety.IsSelecting(f.Child), "original exception preserved and scope reset");
        });
        Test("owned adjacent-surface lookup handles off-map neighbors", () =>
        {
            var map = new Map(); map.Manager = new MapComponent_RatToddlerDining(map);
            var pawn = new Pawn { Map = map, Position = new IntVec3(0) }; var food = new Thing { Map = map };
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, food); pawn.jobs.StartJob(job); map.Manager.TrackVanillaMeal(pawn, job);
            pawn.jobs.curDriver = new JobDriver_Ingest { pawn = pawn, job = job };
            var scan = Toils_Ingest.FindAdjacentEatSurface(TargetIndex.B, TargetIndex.A); scan.actor = pawn; scan.initAction();
            Furniture(map, 1, false, true); scan.initAction(); Check(job.targetB.Cell.x == 1, "in-bounds surface retained");
        });
        Test("loaded and completed vanilla contexts reconcile once without furniture scanning", () =>
        {
            var f = new PrisonFixture(); var job = JobMaker.MakeJob(JobDefOf.Ingest, f.Food); f.Child.jobs.StartJob(job);
            f.Manager.FinalizeInit(); Check(f.Manager.GetVanillaMeal(f.Child)?.jobId == job.loadID, "old save meal registered");
            f.Child.jobs.EndCurrentJob(JobCondition.Succeeded, false); f.Tick(60);
            Check(f.Manager.GetVanillaMeal(f.Child) == null, "finished context removed");
        });
        Test("resumed adult chair meal does not pick up its held food twice", () =>
        {
            var f = new PrisonFixture(false); f.Child.carryTracker.CarriedThing = f.Food; f.Food.Spawned = false;
            Job job = JobMaker.MakeJob(RDE_DefOf.RDE_EatUsingChairAsTable, f.Food, f.Chair, new IntVec3(19)); job.count = 4;
            f.Child.jobs.StartJob(job); var toils = Attach(new JobDriver_EatUsingChairAsTable(), f.Child); Run(toils, 0, 1);
            Check(f.Child.carryTracker.CarriedThing == f.Food && f.Food.stackCount == 10, "held pickup preserved");
        });
    }
}
