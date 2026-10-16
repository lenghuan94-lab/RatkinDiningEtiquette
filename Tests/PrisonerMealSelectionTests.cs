using System;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

internal static partial class Program
{
    // The installed JobGiver_GetFood calls this selector BEFORE constructing
    // Ingest and invoking our existing postfix (also checked against native IL).
    private static Job EvaluateAutomaticMeal(Pawn pawn, Thing food)
    {
        if (!Toils_Ingest.TryFindChairOrSpot(pawn, food, out _)) return null;
        return EvaluateIngest(pawn, food, 3);
    }

    private static void PrisonerMealSelectionTests()
    {
        Test("adult prisoner automatic preflight reaches lone-chair dining after invalid native standing search", () =>
        {
            var f = new PrisonFixture(false, false);
            Toils_Ingest.TestUseStandingSelector = true;
            Check(f.Manager.GetVanillaMeal(f.Child) == null, "no meal exists before the native preflight");
            Job selected = EvaluateAutomaticMeal(f.Child, f.Food);
            Check(selected?.def == RDE_DefOf.RDE_EatUsingChairAsTable && selected.targetB.Thing == f.Chair,
                "automatic selection reaches the existing chair resolver");
            Check(!RatDiningSpotSafety.IsSelecting(f.Child) && f.Map.reservationManager.Reservations.Count == 0,
                "search scope closed with no premature claims");
            f.Child.needs.food.CurLevelPercentage = .2f;
            f.Child.jobs.StartJob(selected);
            var toils = Attach(new JobDriver_EatUsingChairAsTable(), f.Child);
            Run(toils, 0, toils.Count - 1);
            Check(f.Child.needs.food.CurLevelPercentage > .2f, "selected meal really consumes food in the engine doubles");
        });
        Test("adult and toddler prisoners can automatically select floor food without chairs or help enabled", () =>
        {
            foreach (bool toddler in new[] { false, true })
            {
                var f = new PrisonFixture(toddler, false); f.Chair.Destroyed = true;
                Toils_Ingest.TestUseStandingSelector = true;
                Job selected = EvaluateAutomaticMeal(f.Child, f.Food);
                Check(selected?.def == JobDefOf.Ingest && selected.targetA.Thing == f.Food,
                    "same original meal selected without opting prisoner assistance in");
                Check(f.Manager.Requests.Count == 0 && !RatDiningSpotSafety.IsSelecting(f.Child),
                    "no assistance request or leaked scope");
                f.Child.needs.food.CurLevelPercentage = .2f;
                f.Child.jobs.StartJob(selected);
                f.Child.jobs.curDriver = new JobDriver_Ingest { pawn = f.Child, job = selected };
                var pickup = Toils_Haul.StartCarryThing(TargetIndex.A, reserve: false);
                pickup.actor = f.Child; pickup.initAction();
                Thing held = f.Child.carryTracker.CarriedThing;
                var move = Toils_Ingest.CarryIngestibleToChewSpot(f.Child, TargetIndex.A);
                move.actor = f.Child; move.initAction();
                var finish = Toils_Ingest.FinalizeIngest(f.Child, TargetIndex.A);
                finish.actor = f.Child; finish.initAction();
                Check(f.Child.CurJob == selected && selected.targetA.Thing == held
                    && f.Child.needs.food.CurLevelPercentage > .2f && f.Manager.Requests.Count == 0,
                    "same floor job and picked-up food consume nutrition without a new request");
            }
        });
        Test("prisoner preflight floor recovery rejects unsafe unreservable and off-map current cells", () =>
        {
            foreach (Action<PrisonFixture> alter in new Action<PrisonFixture>[] {
                f => f.Child.Danger = Danger.Deadly,
                f => f.Child.Restricted = true,
                f => f.Child.UnsafeTemperature = true,
                f => f.Map.BlockedCells.Add(f.Child.Position.x),
                f => f.Map.Rooms[f.Child.Position.x] = new Room(),
                f => f.Child.Position = new IntVec3(f.Map.CellCount),
                f => f.Warden.Reserve(f.Child.Position, JobMaker.MakeJob(JobDefOf.Wait)) })
            {
                var f = new PrisonFixture(false, false); alter(f);
                Toils_Ingest.TestUseStandingSelector = true;
                Check(!Toils_Ingest.TryFindChairOrSpot(f.Child, f.Food, out IntVec3 cell) && !cell.IsValid,
                    "unsafe fallback remains an unsuccessful selection without a grid exception");
                Check(!RatDiningSpotSafety.IsSelecting(f.Child), "failed scope also closed");
            }
        });
        Test("prisoner preflight never turns forbidden baby food or inability to self-feed into a meal", () =>
        {
            foreach (Action<PrisonFixture> alter in new Action<PrisonFixture>[] {
                f => f.Food.def.ingestible.babiesCanIngest = false,
                f => f.Child.DisallowedFood.Add(f.Food.def),
                f => f.Child.SelfFeeding = false,
                f => f.Food.Destroyed = true,
                f => f.Food.IngestibleNow = false })
            {
                var f = new PrisonFixture(true, false); f.Child.DevelopmentalStage = DevelopmentalStage.Baby; alter(f);
                Toils_Ingest.TestUseStandingSelector = true;
                Check(EvaluateAutomaticMeal(f.Child, f.Food) == null && f.Manager.Requests.Count == 0,
                    "ordinary feeding must decide; fallback cannot change the baby's diet or ability");
                Check(!RatDiningSpotSafety.IsSelecting(f.Child), "rejected food has no scope leak");
            }
        });
        Test("prisoner preflight preserves non-Ratkin slaves released captives and fully disabled behavior", () =>
        {
            foreach (Action<PrisonFixture> alter in new Action<PrisonFixture>[] {
                f => { f.Child.def = new ThingDef(); f.Child.def.ExtendedRatkin = false; },
                f => { f.Child.IsPrisonerOfColony = false; f.Child.IsSlaveOfColony = true; },
                f => f.Child.guest.Released = true,
                f => f.Child.guest.PrisonerIsSecure = false,
                f => { RatDiningMod.Settings.enableChairTable = false; RatDiningMod.Settings.enableToddlerHelp = false; } })
            {
                RatDiningMod.Settings = new RatDiningSettings();
                var f = new PrisonFixture(false, false); alter(f); Toils_Ingest.TestUseStandingSelector = true;
                bool threw = false;
                try { Toils_Ingest.TryFindChairOrSpot(f.Child, f.Food, out _); }
                catch (IndexOutOfRangeException) { threw = true; }
                Check(threw && !RatDiningSpotSafety.IsSelecting(f.Child), "unowned native failure left visible");
            }
        });
        Test("prisoner preflight propagates unrelated exceptions and restores the selection scope", () =>
        {
            var f = new PrisonFixture(false, false);
            var expected = new IndexOutOfRangeException("unrelated search exception");
            Toils_Ingest.TestFinderAction = () => { throw expected; };
            Exception observed = null;
            try { Toils_Ingest.TryFindChairOrSpot(f.Child, f.Food, out _); }
            catch (Exception failure) { observed = failure; }
            Check(observed == expected && !RatDiningSpotSafety.IsSelecting(f.Child), "same exception and scope cleanup");
            Toils_Ingest.TestFinderAction = null; Toils_Ingest.TestUseStandingSelector = true;
            Check(Toils_Ingest.TryFindChairOrSpot(f.Child, f.Food, out _), "next search unaffected by previous failure");
        });
        Test("prisoner preflight preserves the original custody scope of a running meal", () =>
        {
            var f = new PrisonFixture(false, false); Toils_Ingest.TestUseStandingSelector = true;
            bool signaled = false;
            using (RatDiningSpotSafety.SelectFor(f.Child, new IntVec3(19)))
            {
                try { Toils_Ingest.TryFindChairOrSpot(f.Child, f.Food, out _); }
                catch (RatDiningNoChewSpotException) { signaled = true; }
                Check(signaled && RatDiningSpotSafety.IsSelecting(f.Child), "outer meal owns its own recovery decision");
            }
            Check(!RatDiningSpotSafety.IsSelecting(f.Child), "outer scope restores normally");
        });
        Test("prisoner standing search outside the prison falls back within the current prison", () =>
        {
            var f = new PrisonFixture(false, false); Toils_Ingest.TestUseStandingSelector = true;
            RCellFinder.TestSpot = new IntVec3(30);
            Check(Toils_Ingest.TryFindChairOrSpot(f.Child, f.Food, out IntVec3 cell) && cell == f.Child.Position,
                "cannot plan eating in a different room");
            Check(f.Map.reservationManager.Reservations.Count == 0 && f.Manager.Requests.Count == 0,
                "preflight remains a read-only decision");
        });
        Test("prisoner automatic preflight preserves valid vanilla table seats", () =>
        {
            foreach (bool toddler in new[] { false, true })
            {
                var f = new PrisonFixture(toddler, false); Furniture(f.Map, 21, false, true);
                Toils_Ingest.TestDiningCell = new IntVec3(20);
                Check(EvaluateAutomaticMeal(f.Child, f.Food)?.def == JobDefOf.Ingest,
                    "table meal retains native job and does not become a chair meal");
                Check(f.Manager.Requests.Count == 0 && !RatDiningSpotSafety.IsSelecting(f.Child), "no side effects");
            }
        });
    }
}
