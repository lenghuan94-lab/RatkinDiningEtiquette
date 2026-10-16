using System;
using System.Linq;
using RatkinDiningEtiquette;
using RimWorld;
using Verse;
using Verse.AI;

// Compact follow-up to the 1.2.0 investigation. Detailed historical observations
// remain in the review document; regressions contain the full 1.2.1 fix coverage.
internal static class ToddlerInterruptionProbe
{
    private static int Main() => Program.ProbeToddlerInterruption();
}

internal static partial class Program
{
    internal static int ProbeToddlerInterruption()
    {
        try
        {
            LoadDefs();
            Test("ordinary chair meal passes guards before every native-shaped transition", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Food.stackCount = 4;
                f.Child.Position = f.Chair.Position;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair), keepCarryingThingOverride: true);
                var driver = new JobDriver_ToddlerEatAtChair(); var toils = Attach(driver, f.Child);
                foreach (Toil toil in toils)
                {
                    Check(!driver.Failures.Any(fail => fail()), "global guards before " + toil.Name);
                    toil.initAction?.Invoke();
                }
                Check(!f.Request.IsActive && !f.Manager.IsCoolingDown(f.Child), "normal completed meal has no failure cooldown");
                Console.WriteLine("OBSERVED normal instant finalization does not deterministically fail in the current doubles.");
            });
            Test("map reconciliation closes consumed-but-unclosed chair food as completed", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Food.stackCount = 4;
                f.Child.Position = f.Chair.Position;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair), keepCarryingThingOverride: true);
                var toils = Attach(new JobDriver_ToddlerEatAtChair(), f.Child); Run(toils, 0, 3);
                Check(f.Food.Destroyed && f.Request.IsActive, "meal consumed; request not yet closed");
                f.Tick(60);
                Check(f.Request.state == RatToddlerDiningState.Completed && !f.Manager.IsCoolingDown(f.Child), "actual consumed-food marker closes the meal");
                Console.WriteLine("OBSERVED forced reconciliation between finalization and completion no longer produces FloorFallback/cooldown.");
                Console.WriteLine("LIMIT normal RimWorld instant toils usually complete synchronously; this probe alone does not establish that timing in the player's game.");
            });
            Test("lost help eligibility before consuming food continues the original food on the floor", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Child.Position = f.Chair.Position;
                f.Child.needs.food.CurLevelPercentage = .191f;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair), keepCarryingThingOverride: true);
                var driver = new JobDriver_ToddlerEatAtChair(); var toils = Attach(driver, f.Child); Run(toils, 0, 2);
                RatDiningMod.Settings.enableToddlerHelp = false;
                Check(!RatDiningApi.IsEligibleToddler(f.Child) && !RatDiningApi.IsCriticalHunger(f.Child), "false eligibility is independent of hunger threshold");
                Check(driver.Failures.Any(fail => fail()), "native global guard would terminate chewing");
                driver.Cleanup(JobCondition.Incompletable);
                Job floor = driver.GetFinalizerJob(JobCondition.Incompletable);
                Check(floor?.def == RDE_DefOf.RDE_ToddlerEatHeldFoodOnFloor && floor.targetA.Thing == f.Food
                    && f.Manager.IsCoolingDown(f.Child), "same-food floor recovery and cooldown");
                Console.WriteLine("OBSERVED canFeedSelf=True/eligible=False/critical=False/cooldown with deliberate help disable; this is a cause-shape probe, not evidence the player changed this setting.");
            });
            Test("finalized meal cleans up despite eligibility changes after actual consumption", () =>
            {
                var f = new Fixture(); f.Waiting(); f.Food.stackCount = 4;
                f.Child.Position = f.Chair.Position;
                f.Child.jobs.StartJob(JobMaker.MakeJob(RDE_DefOf.RDE_ToddlerEatAtChair, f.Food, f.Chair), keepCarryingThingOverride: true);
                var driver = new JobDriver_ToddlerEatAtChair(); var toils = Attach(driver, f.Child); Run(toils, 0, 3);
                RatDiningMod.Settings.enableToddlerHelp = false;
                Check(!driver.Failures.Any(fail => fail()), "completed meal has an actual-consumption cleanup exemption");
                Console.WriteLine("OBSERVED cleanup distinguishes completed meals from recoverable unconsumed meals.");
            });
            Console.WriteLine("PASS 4 investigation probes. These validate local control-flow observations, not a complete reproduction of the player's failure.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
