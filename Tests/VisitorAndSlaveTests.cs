using System;
using System.Reflection;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

internal static partial class Program
{
    private sealed class VisitorFixture
    {
        internal readonly Map Map = new Map();
        internal readonly Faction Faction = new Faction();
        internal readonly Pawn Child;
        internal readonly Pawn Helper;
        internal readonly Thing Food;
        internal readonly Building Chair;
        internal MapComponent_RatToddlerDining Manager => Map.Manager;
        internal VisitorFixture(bool slave = false, bool enableHelp = true)
        {
            if (enableHelp)
            {
                if (slave) RatDiningMod.Settings.enableSlaveToddlerHelp = true;
                else RatDiningMod.Settings.enableVisitorToddlerHelp = true;
            }
            Map.Manager = new MapComponent_RatToddlerDining(Map);
            Child = new Pawn { Map = Map, Faction = slave ? Faction.OfPlayer : Faction, IsColonist = slave,
                IsSlaveOfColony = slave, DevelopmentalStage = DevelopmentalStage.Baby };
            Helper = new Pawn { Map = Map, Faction = Child.Faction, IsColonist = slave, Toddler = false,
                workSettings = slave ? new Pawn_WorkSettings() : null };
            Child.needs.food.CurLevelPercentage = .2f;
            Food = new Thing { Map = Map, stackCount = 10 };
            Chair = Furniture(Map, 20, true, false);
            Map.mapPawns.AllPawnsSpawned.Add(Child); Map.mapPawns.AllPawnsSpawned.Add(Helper);
            FoodUtility.BabyFood = Food;
        }
        internal RatToddlerDiningRequest Waiting()
        {
            Job meal = EvaluateIngest(Child, Food);
            Check(meal.def == RDE_DefOf.RDE_ToddlerPrepareDiningRequest, "child-first entrance selected");
            Child.jobs.StartJob(meal);
            Child.carryTracker.CarriedThing = Food; Food.Spawned = false;
            var request = Manager.GetForPawn(Child);
            Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForDiningHelp, Food, Chair, request.stagingCell),
                keepCarryingThingOverride: true);
            Manager.SetState(request, RatToddlerDiningState.WaitingForHelp);
            return request;
        }
    }

    private static Job VisitorIdle(Pawn pawn, Job original = null)
    {
        object[] args = { pawn, original ?? JobMaker.MakeJob(JobDefOf.GotoWander, new IntVec3(30)) };
        typeof(JobGiver_Wander_VisitorDining).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        return (Job)args[1];
    }

    private static void VisitorAndSlaveTests()
    {
        Test("visitor recovery return survives idle reevaluation with help disabled", () =>
        {
            foreach (bool master in new[] { false, true })
            {
                var f = new VisitorFixture(); var request = BeginVisitorReturn(f, master);
                Job current = f.Helper.CurJob;
                Check(VisitorIdle(f.Helper) == current, "idle reevaluation retains exact return job");
                Check(request.returnAttempts == 1 && f.Helper.carryTracker.CarriedThing == f.Child,
                    "routine wandering does not restart or exhaust safety returns");
                var toils = Attach(new JobDriver_ReturnDiningChild(), f.Helper); Run(toils, 0, 1);
                Find.TickManager.TicksGame = 180; f.Manager.MapComponentTick();
                Check(f.Child.Spawned && f.Child.carryTracker.CarriedThing == f.Food
                    && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor,
                    "safe return resumes same food even with its category or master disabled");
            }
        });
        Test("visitor recovery return yields to care and invalid destinations", () =>
        {
            foreach (Action<VisitorFixture> change in new Action<VisitorFixture>[] {
                f => f.Child.MedicalRest = true,
                f => f.Helper.MedicalRest = true,
                f => f.Helper.CurJob.SetTarget(TargetIndex.B, new IntVec3(50000)),
                f => f.Map.BlockedCells.Add(f.Helper.CurJob.targetB.Cell.x),
                f => f.Helper.CurJob.SetTarget(TargetIndex.C, new IntVec3(99)),
                f => f.Child.Faction = new Faction() })
            {
                RatDiningMod.Settings.enableToddlerHelp = true;
                var f = new VisitorFixture(); BeginVisitorReturn(f, false); change(f);
                Job idle = JobMaker.MakeJob(JobDefOf.GotoWander, new IntVec3(30));
                Check(VisitorIdle(f.Helper, idle) == idle, "unsafe, mismatched or medical return not pinned");
            }
            RatDiningMod.Settings.enableToddlerHelp = true;
            var valid = new VisitorFixture(); BeginVisitorReturn(valid, false);
            Job care = JobMaker.MakeJob(JobDefOf.BringBabyToSafety, valid.Child);
            Check(VisitorIdle(valid.Helper, care) == care, "higher-priority care remains untouched");
        });
        Test("visitor recovery return reevaluation retains its overall deadline", () =>
        {
            var f = new VisitorFixture(); var request = BeginVisitorReturn(f, false);
            Find.TickManager.TicksGame = request.recoveryStartedTick + RatDiningCarryRecovery.RecoveryTimeoutTicks;
            Job idle = JobMaker.MakeJob(JobDefOf.GotoWander, new IntVec3(30));
            Check(VisitorIdle(f.Helper, idle) == idle, "expired recovery is not pinned indefinitely");
        });
        Test("visitor idle reevaluation keeps the exact claimed helper job", () =>
        {
            foreach (bool carried in new[] { false, true })
            {
                var f = new VisitorFixture(); var request = f.Waiting();
                f.Helper.jobs.StartJob(VisitorIdle(f.Helper));
                Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, carried ? 3 : 0);
                Job current = f.Helper.CurJob; int starts = f.Helper.jobs.Starts;
                Check(VisitorIdle(f.Helper) == current && VisitorIdle(f.Helper, JobMaker.MakeJob(JobDefOf.Wait_Wander, 60)) == current,
                    "native re-evaluation sees same job object, not a fresh wander or assist");
                Check(f.Helper.jobs.Starts == starts && request.helper == f.Helper && request.retryCount == 0,
                    "no job restart, repeated claim, or artificial helper failure");
            }
        });
        Test("visitor idle reevaluation keeps its original floor recovery but yields to medical care", () =>
        {
            var f = new VisitorFixture(); var request = f.Waiting();
            Find.TickManager.TicksGame = 600; f.Manager.MapComponentTick();
            Find.TickManager.TicksGame = 660; f.Manager.MapComponentTick();
            Check(!request.IsActive && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor,
                "failed request has handed off the original meal");
            Check(VisitorIdle(f.Child) == f.Child.CurJob && f.Child.CurJob.targetA.Thing == f.Food,
                "routine wander does not drop or replace recovery food");
            f.Child.MedicalRest = true;
            Job idle = JobMaker.MakeJob(JobDefOf.GotoWander, new IntVec3(30));
            Check(VisitorIdle(f.Child, idle) == idle, "medical need does not pin the floor meal");
        });
        Test("visitor idle reevaluation keeps provision during pickup and child carrying", () =>
        {
            foreach (bool carried in new[] { false, true })
            {
                var f = new VisitorFixture(); f.Helper.jobs.StartJob(VisitorIdle(f.Helper));
                var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper);
                Run(toils, 0, carried ? 6 : 0);
                var request = f.Manager.GetForPawn(f.Child); Job current = f.Helper.CurJob;
                Check(VisitorIdle(f.Helper) == current && request.helperJobId == current.loadID,
                    "provided meal retains its saved job identity at both phases");
                Check(request.IsActive && f.Manager.IsChairClaimed(f.Chair), "meal ownership retained");
            }
        });
        Test("visitor idle reevaluation keeps child preparation waiting and chair eating", () =>
        {
            var f = new VisitorFixture();
            f.Child.jobs.StartJob(EvaluateIngest(f.Child, f.Food));
            Check(VisitorIdle(f.Child) == f.Child.CurJob, "preparation retained");
            var request = f.Manager.GetForPawn(f.Child);
            f.Child.carryTracker.CarriedThing = f.Food; f.Food.Spawned = false;
            f.Manager.SetState(request, RatToddlerDiningState.WaitingForHelp);
            f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerWaitForDiningHelp, f.Food, f.Chair, request.stagingCell),
                keepCarryingThingOverride: true);
            Check(VisitorIdle(f.Child) == f.Child.CurJob, "child-first waiting retained");
            f.Helper.jobs.StartJob(VisitorIdle(f.Helper));
            var assist = Attach(new JobDriver_AssistRatToddlerDining(), f.Helper); Run(assist, 0, assist.Count - 1);
            Check(VisitorIdle(f.Child) == f.Child.CurJob && f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair,
                "seated meal retained rather than wandering away");
            var supplied = new VisitorFixture(); supplied.Helper.jobs.StartJob(VisitorIdle(supplied.Helper));
            Run(Attach(new JobDriver_ProvideRatToddlerDiningMeal(), supplied.Helper), 0, 0);
            Check(VisitorIdle(supplied.Child) == supplied.Child.CurJob
                && supplied.Child.CurJobDef == RDE_DefOf.RDE_ToddlerWaitForMeal, "provided-meal waiting retained");
        });
        Test("visitor reevaluation yields to changed duties unsafe food and higher-priority results", () =>
        {
            foreach (Action<VisitorFixture> change in new Action<VisitorFixture>[] {
                f => f.Helper.Faction = new Faction(), f => f.Child.MedicalRest = true,
                f => f.Food.def.ingestible.babiesCanIngest = false,
                f => f.Helper.Lord = new Lord { CurLordToil = new object() },
                f => f.Helper.CurJob.SetTarget(TargetIndex.B, new Building { Map = f.Map }) })
            {
                var f = new VisitorFixture(); f.Waiting(); f.Helper.jobs.StartJob(VisitorIdle(f.Helper));
                Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 0); change(f);
                Job idle = JobMaker.MakeJob(JobDefOf.GotoWander, new IntVec3(30));
                Check(VisitorIdle(f.Helper, idle) == idle, "invalid owned meal not pinned to current job");
            }
            var valid = new VisitorFixture(); valid.Waiting(); valid.Helper.jobs.StartJob(VisitorIdle(valid.Helper));
            Run(Attach(new JobDriver_AssistRatToddlerDining(), valid.Helper), 0, 0);
            Job urgent = JobMaker.MakeJob(JobDefOf.BringBabyToSafety, valid.Child);
            Check(VisitorIdle(valid.Helper, urgent) == urgent, "higher-priority care result untouched despite owned request");
        });
        Test("visitor non-null uninitialized NPC work tracker remains untouched", () =>
        {
            var f = new VisitorFixture(); f.Helper.workSettings = new Pawn_WorkSettings { Initialized = false, Childcare = false };
            var request = f.Waiting(); Job assist = VisitorIdle(f.Helper);
            Check(assist.def == RDE_DefOf.RDE_AssistRatToddlerDining && !f.Helper.workSettings.Initialized,
                "native NPC tracker not queried or initialized");
            f.Helper.jobs.StartJob(assist); Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 0);
            Check(request.helper == f.Helper && !f.Helper.workSettings.Initialized, "claim retains NPC settings");
        });
        Test("visitor medical or ordered foreign feeding retires the chair request", () =>
        {
            foreach (bool medical in new[] { false, true })
            {
                var f = new VisitorFixture(); var request = f.Waiting();
                f.Helper.jobs.StartJob(VisitorIdle(f.Helper)); Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 0);
                var other = new Pawn { Map = f.Map, Toddler = false };
                Job feeding = medical ? JobMaker.MakeJob(JobDefOf.FeedPatient, f.Food, f.Child)
                    : JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
                feeding.playerForced = !medical;
                Check(!RatToddlerMealCoordinator.TryRedirectFeeding(other, ref feeding) && !request.IsActive,
                    "emergency/manual care preserved and meal retired");
                Check(!f.Manager.IsChairClaimed(f.Chair) && other.CanReserve(f.Child), "care claims released");
            }
        });
        Test("visitor caregiver-first planning does not interrupt child combat or care", () =>
        {
            foreach (JobDef def in new[] { new JobDef { defName = "Attack" }, JobDefOf.BringBabyToSafety, JobDefOf.Ingest })
            {
                var f = new VisitorFixture(); f.Child.jobs.StartJob(JobMaker.MakeJob(def));
                Check(VisitorIdle(f.Helper).def == JobDefOf.GotoWander && f.Manager.GetForPawn(f.Child) == null,
                    "active non-idle child behavior retained");
            }
        });
        Test("visitor waiting timeout preserves its original food and bounds retry", () =>
        {
            var f = new VisitorFixture(); var request = f.Waiting();
            Find.TickManager.TicksGame = 600; f.Manager.MapComponentTick();
            Check(EvaluateIngest(f.Child, new Thing { Map = f.Map }).targetA.Thing == f.Food,
                "pending fallback keeps food rather than selecting a different meal");
            Find.TickManager.TicksGame = 660; f.Manager.MapComponentTick();
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair)
                && f.Child.carryTracker.CarriedThing == f.Food, "finite timeout and original meal");
            Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor && f.Child.CurJob.targetA.Thing == f.Food,
                "recovery starts the original held meal");
        });
        Test("visitor and slave loaded waiting indexes retain custody and faction rules", () =>
        {
            foreach (bool slave in new[] { false, true })
            {
                var f = new VisitorFixture(slave); var request = f.Waiting();
                Scribe.mode = LoadSaveMode.Saving; request.custody.ExposeData();
                var loaded = new RatDiningCustodySnapshot(); Scribe.mode = LoadSaveMode.LoadingVars; loaded.ExposeData();
                Scribe.mode = LoadSaveMode.Inactive;
                Check(loaded.Matches(f.Child) && loaded.slave == slave, "saved identity fields retained");
                Scribe.mode = LoadSaveMode.PostLoadInit; f.Manager.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
                Check(f.Manager.GetForPawn(f.Child) == request && f.Manager.IsChairClaimed(f.Chair), "indexes rebuilt");
                Check(new WorkGiver_AssistRatToddlerDining().HasJobOnThing(f.Helper, f.Child), "valid same faction still eligible");
                f.Child.Faction = new Faction();
                Check(!loaded.Matches(f.Child) && !RatDiningCustody.ValidRequest(request), "loaded identity changes invalidate old contract");
            }
        });
        Test("slave exit job invalidates secure meals before next faction update", () =>
        {
            var f = new VisitorFixture(true); var request = f.Waiting();
            f.Child.jobs.StartJob(new Job { def = JobDefOf.GotoWander, exitMapOnArrival = true });
            Check(!RatDiningCustody.CanDine(f.Child) && !RatDiningCustody.ValidRequest(request),
                "native secure check rejects pending map exit");
            Find.TickManager.TicksGame = 60; f.Manager.MapComponentTick();
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "slave exit releases chair");
        });
        Test("visitor and slave carried medical emergency retains native care ownership", () =>
        {
            foreach (bool slave in new[] { false, true })
            {
                var f = new VisitorFixture(slave); var request = f.Waiting();
                Job assist = slave ? new WorkGiver_AssistRatToddlerDining().JobOnThing(f.Helper, f.Child) : VisitorIdle(f.Helper);
                f.Helper.jobs.StartJob(assist); Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 3);
                Check(f.Helper.carryTracker.CarriedThing == f.Child, "carried at emergency handoff");
                f.Child.MedicalRest = true;
                Job care = JobMaker.MakeJob(JobDefOf.BringBabyToSafety, f.Child); f.Helper.jobs.StartJob(care);
                Check(f.Helper.CurJob == care && f.Helper.carryTracker.CarriedThing == f.Child
                    && request.careHandoff && !request.IsActive && !f.Manager.IsChairClaimed(f.Chair),
                    "care receives carried child without a competing dining recovery");
            }
        });
        Test("friendly visitor adult uses a lone chair and keeps native table meals", () =>
        {
            var f = new VisitorFixture(); f.Child.Toddler = false; f.Child.DevelopmentalStage = DevelopmentalStage.Adult;
            Check(EvaluateIngest(f.Child, f.Food).def == RDE_DefOf.RDE_EatUsingChairAsTable, "visitor adult entrance");
            Furniture(f.Map, 21, false, true);
            Check(EvaluateIngest(f.Child, f.Food).def == JobDefOf.Ingest, "adjacent table remains native");
        });
        Test("visitor child-first NPC idle scheduling completes the original meal", () =>
        {
            var f = new VisitorFixture(); var request = f.Waiting();
            Job assist = VisitorIdle(f.Helper);
            Check(assist.def == RDE_DefOf.RDE_AssistRatToddlerDining && request.helper == null, "idle scan plans without claiming");
            f.Helper.jobs.StartJob(assist);
            var toils = Attach(new JobDriver_AssistRatToddlerDining(), f.Helper); Run(toils, 0, toils.Count - 1);
            Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair && f.Child.carryTracker.CarriedThing == f.Food,
                "same faction places child with original food");
            var eat = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Run(eat, 0, eat.Count - 1);
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "finished meal releases index");
        });
        Test("visitor caregiver-first NPC idle planning claims only at StartJob", () =>
        {
            var f = new VisitorFixture();
            Job provide = VisitorIdle(f.Helper, JobMaker.MakeJob(JobDefOf.Wait_Wander, 60));
            Check(provide.def == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal && f.Manager.GetForPawn(f.Child) == null,
                "native idle result supplies planning entrance without side effects");
            f.Helper.jobs.StartJob(provide);
            var request = f.Manager.GetForPawn(f.Child);
            Check(request?.helper == f.Helper, "StartJob revalidates and owns meal");
            var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper); Run(toils, 0, toils.Count - 1);
            Check(f.Child.CurJobDef == RDE_DefOf.RDE_ToddlerEatAtChair && f.Helper.carryTracker.CarriedThing == null,
                "NPC provision safely places child");
            var eat = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Run(eat, 0, eat.Count - 1);
            Check(!request.IsActive && f.Child.needs.food.CurLevelPercentage > .2f, "provided food consumed");
        });
        Test("visitor assistance rejects player and unrelated friendly faction caregivers", () =>
        {
            var f = new VisitorFixture(); var request = f.Waiting();
            foreach (Faction faction in new[] { Faction.OfPlayer, new Faction() })
            {
                var other = new Pawn { Map = f.Map, Faction = faction, Toddler = false };
                Check(!RatToddlerMealCoordinator.CanHelp(other, f.Child, true)
                    && !new WorkGiver_AssistRatToddlerDining().HasJobOnThing(other, f.Child), "cross faction rejected");
            }
            Check(request.IsActive && request.helper == null, "no foreign claim");
        });
        Test("foreign feeding is not suppressed by an already claimed visitor request", () =>
        {
            var f = new VisitorFixture(); var request = f.Waiting();
            f.Helper.jobs.StartJob(VisitorIdle(f.Helper)); Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 0);
            var colonist = new Pawn { Map = f.Map, Toddler = false };
            Job feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
            Check(!RatToddlerMealCoordinator.TryRedirectFeeding(colonist, ref feeding) && feeding.def == JobDefOf.BottleFeedBaby,
                "native cross-faction feeding untouched");
            Check(request.helper == f.Helper, "same faction owns claim");
        });
        Test("visitor native bottle-feed entry requires the child's actual faction", () =>
        {
            var f = new VisitorFixture(); Job feed = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
            f.Helper.jobs.StartJob(feed);
            Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ProvideRatToddlerDiningMeal, "same-faction native feeding redirected");
        });
        Test("visitor unavailable duty or hostile identity keeps vanilla eating and idle jobs", () =>
        {
            foreach (Action<VisitorFixture> change in new Action<VisitorFixture>[] {
                f => f.Faction.Hostile = true, f => f.Map.IsPlayerHome = false,
                f => f.Child.guest.Released = true, f => f.Child.IsPrisonerOfColony = true,
                f => f.Child.HostFaction = new Faction(), f => f.Child.FormingCaravan = true,
                f => f.Child.Lord = new Lord { CurLordToil = new object() },
                f => f.Child.mindState.duty = new PawnDuty(new DutyDef()),
                f => f.Child.jobs.StartJob(new Job { def = JobDefOf.GotoWander, exitMapOnArrival = true }) })
            {
                var f = new VisitorFixture(); change(f);
                Check(EvaluateIngest(f.Child, f.Food).def == JobDefOf.Ingest && VisitorIdle(f.Helper).def == JobDefOf.GotoWander,
                    "original behavior for ineligible visitor");
            }
        });
        Test("visitor rest duty is supported but departure invalidates an existing meal", () =>
        {
            var f = new VisitorFixture(); var lord = new Lord { CurLordToil = new LordToil_DefendPoint() };
            f.Child.Lord = f.Helper.Lord = lord;
            f.Child.mindState.duty = f.Helper.mindState.duty = new PawnDuty(DutyDefOf.Defend);
            var request = f.Waiting(); Check(VisitorIdle(f.Helper).def == RDE_DefOf.RDE_AssistRatToddlerDining, "rest duty participates");
            lord.CurLordToil = new object(); Find.TickManager.TicksGame = 60; f.Manager.MapComponentTick();
            Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "departure releases meal ownership");
            Check(f.Child.CurJob?.def != RDE_DefOf.RDE_ToddlerPrepareDiningRequest, "no fresh dining cycle");
        });
        Test("visitor idle hook preserves forced exit trade combat and urgent jobs", () =>
        {
            var f = new VisitorFixture(); f.Waiting();
            foreach (Job original in new[] { JobMaker.MakeJob(JobDefOf.Ingest, f.Food),
                JobMaker.MakeJob(JobDefOf.FeedPatient, f.Food, f.Child), new Job { def = new JobDef { defName = "TradeOrFight" } },
                new Job { def = JobDefOf.GotoWander, playerForced = true },
                new Job { def = JobDefOf.GotoWander, exitMapOnArrival = true } })
                Check(VisitorIdle(f.Helper, original) == original, "higher priority result preserved");
        });
        Test("visitor helper safety and parenting restrictions apply without player work settings", () =>
        {
            foreach (Action<VisitorFixture> change in new Action<VisitorFixture>[] {
                f => f.Helper.MedicalRest = true, f => f.Helper.Downed = true,
                f => f.Helper.needs.food.CurLevelPercentage = .1f, f => f.Helper.needs.rest.CurLevelPercentage = .05f,
                f => f.Helper.DisabledWork.Add(WorkTypeDefOf.Childcare),
                f => f.Helper.workSettings = new Pawn_WorkSettings { Childcare = false },
                f => RatDiningMod.Settings.allowNonParents = false,
                f => f.Helper.Position = new IntVec3(9000), f => f.Helper.CanReachTargets = false,
                f => f.Helper.carryTracker.CarriedThing = new Thing(),
                f => RatDiningMod.Settings.enableToddlerHelp = false })
            {
                RatDiningMod.Settings = new RatDiningSettings(); var f = new VisitorFixture(); f.Waiting(); change(f);
                Check(VisitorIdle(f.Helper).def == JobDefOf.GotoWander, "unsafe or disallowed helper stays idle");
            }
        });
        Test("visitor full child medical need and forbidden baby food never start provision", () =>
        {
            foreach (Action<VisitorFixture> change in new Action<VisitorFixture>[] {
                f => f.Child.needs.food.CurLevelPercentage = .7f,
                f => f.Child.needs.food.CurLevelPercentage = .01f, f => f.Child.MedicalRest = true,
                f => f.Child.SelfFeeding = false, f => f.Food.def.ingestible.babiesCanIngest = false,
                f => f.Child.DisallowedFood.Add(f.Food.def) })
            {
                var f = new VisitorFixture(); change(f);
                Check(VisitorIdle(f.Helper).def == JobDefOf.GotoWander && f.Manager.GetForPawn(f.Child) == null,
                    "native food and emergency care remain available");
            }
        });
        Test("visitor planned provision rechecks identity before claiming", () =>
        {
            var f = new VisitorFixture(); Job planned = VisitorIdle(f.Helper);
            f.Helper.Faction = new Faction(); f.Helper.jobs.StartJob(planned);
            Check(f.Helper.CurJobDef == JobDefOf.Wait && f.Manager.GetForPawn(f.Child) == null, "changed helper cannot claim stale plan");
        });
        Test("visitor two NPC caregivers compete for one indexed meal", () =>
        {
            var f = new VisitorFixture(); var request = f.Waiting();
            var other = new Pawn { Map = f.Map, Faction = f.Faction, IsColonist = false, Toddler = false, workSettings = null };
            Job first = VisitorIdle(f.Helper), second = VisitorIdle(other);
            f.Helper.jobs.StartJob(first); Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 0);
            other.jobs.StartJob(second); var driver = new JobDriver_AssistRatToddlerDining { pawn = other, job = other.CurJob };
            Check(!driver.TryMakePreToilReservations(false) && request.helper == f.Helper, "second helper cannot steal claim");
        });
        Test("visitor custody change while carried safely drops and releases the old caregiver", () =>
        {
            foreach (bool helperChange in new[] { false, true })
            {
                var f = new VisitorFixture(); var request = f.Waiting(); f.Helper.jobs.StartJob(VisitorIdle(f.Helper));
                Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 0);
                f.Manager.SetState(request, RatToddlerDiningState.BeingCarried); f.Helper.carryTracker.Take(f.Child, 1);
                if (helperChange) f.Helper.Faction = new Faction(); else f.Child.Faction = Faction.OfPlayer;
                Find.TickManager.TicksGame = 60; f.Manager.MapComponentTick();
                Find.TickManager.TicksGame = 120; f.Manager.MapComponentTick();
                Check(f.Helper.carryTracker.CarriedThing == null && f.Child.Spawned
                    && (helperChange ? request.helper == null && request.IsActive : !request.IsActive),
                    "identity change cannot strand carried visitor");
                Check((helperChange || !f.Manager.IsChairClaimed(f.Chair)) && f.Child.carryTracker.CarriedThing == f.Food,
                    "original food and safe drop, valid child may retry with its own faction");
            }
        });
        Test("slave toddler and adult keep independent-chair entrances", () =>
        {
            var f = new VisitorFixture(true); var request = f.Waiting();
            Check(new WorkGiver_AssistRatToddlerDining().HasJobOnThing(f.Helper, f.Child), "colony childcare handles secure slave toddler");
            f.Manager.Complete(request); f.Child.Toddler = false; f.Child.DevelopmentalStage = DevelopmentalStage.Adult;
            Check(EvaluateIngest(f.Child, f.Food).def == RDE_DefOf.RDE_EatUsingChairAsTable, "secure slave adult entrance");
        });
        Test("slave caregiver-first bottle feeding completes with baby-compatible food", () =>
        {
            var f = new VisitorFixture(true); f.Helper.jobs.StartJob(JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food));
            var request = f.Manager.GetForPawn(f.Child);
            var toils = Attach(new JobDriver_ProvideRatToddlerDiningMeal(), f.Helper); Run(toils, 0, toils.Count - 1);
            var eat = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Run(eat, 0, eat.Count - 1);
            Check(!request.IsActive && f.Child.needs.food.CurLevelPercentage > .2f, "slave provided meal completes");
        });
        Test("slave release rebellion medical and custody changes retire existing meals", () =>
        {
            foreach (Action<VisitorFixture> change in new Action<VisitorFixture>[] {
                f => f.Child.guest.Released = true, f => f.Child.guest.SlaveIsSecure = false,
                f => f.Child.InMentalState = true, f => f.Child.MedicalRest = true,
                f => f.Child.IsSlaveOfColony = false,
                f => { f.Child.IsSlaveOfColony = false; f.Child.IsPrisonerOfColony = true; f.Child.HostFaction = Faction.OfPlayer; } })
            {
                var f = new VisitorFixture(true); var request = f.Waiting(); change(f);
                Find.TickManager.TicksGame = 60; f.Manager.MapComponentTick();
                Check(!request.IsActive && !f.Manager.IsChairClaimed(f.Chair), "old slave contract retired");
                Check(f.Child.carryTracker.CarriedThing == f.Food, "original food preserved");
            }
        });
        Test("unsecured slave caregiver cannot claim child or redirect native feeding", () =>
        {
            var f = new VisitorFixture(true); var request = f.Waiting();
            f.Helper.IsSlaveOfColony = true; f.Helper.guest.SlaveIsSecure = false;
            Check(!RatToddlerMealCoordinator.CanHelp(f.Helper, f.Child, true), "helper custody checked even before native colonist flag updates");
            Job feeding = JobMaker.MakeJob(JobDefOf.BottleFeedBaby, f.Child, f.Food);
            Check(!RatToddlerMealCoordinator.TryRedirectFeeding(f.Helper, ref feeding) && request.helper == null,
                "unsecured helper cannot acquire meal");
        });
    }

    private static RatToddlerDiningRequest BeginVisitorReturn(VisitorFixture f, bool disableMaster)
    {
        var request = f.Waiting(); f.Helper.jobs.StartJob(VisitorIdle(f.Helper));
        Run(Attach(new JobDriver_AssistRatToddlerDining(), f.Helper), 0, 3);
        f.Helper.Position = new IntVec3(500);
        f.Map.BlockedCells.Add(500);
        foreach (IntVec3 direction in GenAdj.CardinalDirections) f.Map.BlockedCells.Add((f.Helper.Position + direction).x);
        if (disableMaster) RatDiningMod.Settings.enableToddlerHelp = false;
        else RatDiningMod.Settings.enableVisitorToddlerHelp = false;
        Find.TickManager.TicksGame = 60; f.Manager.MapComponentTick();
        Find.TickManager.TicksGame = 120; f.Manager.MapComponentTick();
        Check(f.Helper.CurJobDef == RDE_DefOf.RDE_ReturnDiningChild && request.returnAttempts == 1,
            "cancelled visitor carry starts bounded safety return when local placement fails");
        return request;
    }
}
