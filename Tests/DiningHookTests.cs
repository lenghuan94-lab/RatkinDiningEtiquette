using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

// Checks the real installed API contracts without executing a Unity game,
// changing dependencies, or substituting game types with engine doubles.
internal static class DiningHookTests
{
    private static readonly string Managed = Directory.Exists(@"D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed")
        ? @"D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".tools", "dining-audit-2026-10-03", "bin", "Release", "net472"));
    private static readonly string Harmony = Directory.Exists(@"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.6\Assemblies")
        ? @"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.6\Assemblies"
        : @"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies";
    private const string Toddlers = @"D:\SteamLibrary\steamapps\workshop\content\294100\2903359152\1.6\Assemblies";
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolution) =>
        {
            foreach (string directory in new[] { Managed, Harmony, Toddlers })
            {
                string path = Path.Combine(directory, new AssemblyName(resolution.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try
        {
            Assembly game = Assembly.LoadFrom(Path.Combine(Managed, "Assembly-CSharp.dll"));
            Version gameVersion = game.GetName().Version;
            Require(gameVersion.Major == 1 && gameVersion.Minor == 6, "diagnostic requires RimWorld 1.6 references");
            Console.WriteLine("Reference assembly " + gameVersion + ": " + Managed);
            string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            Assembly mod = Assembly.LoadFrom(Path.Combine(repository, "1.6", "Assemblies", "RatkinDiningEtiquette.dll"));
            CheckHook(game, mod, "RCellFinder_ScopedDiningResult", "RimWorld.RCellFinder", "SpotToChewStandingNear", "Verse.IntVec3");
            CheckHook(game, mod, "JobGiver_Wander_VisitorDining", "Verse.AI.JobGiver_Wander", "TryGiveJob", "Verse.AI.Job");
            CheckHook(game, mod, "Toils_Ingest_ProtectOwnedDiningSpot", "RimWorld.Toils_Ingest", "CarryIngestibleToChewSpot", "Verse.AI.Toil");
            CheckHook(game, mod, "Toils_Ingest_ProtectOwnedDiningMapEdge", "RimWorld.Toils_Ingest", "FindAdjacentEatSurface", "Verse.AI.Toil");
            CheckHook(game, mod, "Toils_Ingest_ProtectOwnedDiningFinalization", "RimWorld.Toils_Ingest", "FinalizeIngest", "Verse.AI.Toil");
            Require(game.GetType("RimWorld.WorkGiver_Warden").IsAssignableFrom(mod.GetType("RatkinDiningEtiquette.WorkGiver_Warden_AssistRatToddlerDining")), "Warden inheritance contract");
            Require(game.GetType("RimWorld.WorkGiver_Warden_Feed") != null, "Installed warden feeding class");
            MethodInfo finalize = mod.GetType("RatkinDiningEtiquette.MapComponent_RatToddlerDining").GetMethod("FinalizeInit");
            Require(finalize.GetBaseDefinition().DeclaringType == game.GetType("Verse.MapComponent"), "FinalizeInit must override the real map lifecycle method");
            Assembly toddlers = Assembly.LoadFrom(Path.Combine(Toddlers, "Toddlers.dll"));
            Require(toddlers.GetType("Toddlers.BabyMoveUtility").GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Any(m => m.Name == "BestPlaceForBaby" && m.GetParameters().Length == 3 && m.GetParameters()[2].IsOptional), "Native safety candidate API");
            Console.WriteLine("PASS dining patch parameter/return contracts, Warden inheritance and map lifecycle against RimWorld 1.6 reference assemblies.");
            CheckNativeJobContinuation(game);
            CheckIngestionObserver(game, mod);
            if (args.Contains("--harmony")) CheckHarmonyRegistration(game, mod, toddlers);
            Console.WriteLine("LIMIT: complete meal actions, real pathfinding and save/load still require a running game.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckNativeJobContinuation(Assembly game)
    {
        // Execute the installed tracker's pure decision branch. No Pawn, job
        // driver, full ThinkTree, Unity state or unique-ID service is initialized.
        Type trackerType = game.GetType("Verse.AI.Pawn_JobTracker", true);
        Type jobType = game.GetType("Verse.AI.Job", true);
        Type resultType = game.GetType("Verse.AI.ThinkResult", true);
        object tracker = FormatterServices.GetUninitializedObject(trackerType);
        object current = FormatterServices.GetUninitializedObject(jobType);
        object idle = FormatterServices.GetUninitializedObject(jobType);
        FieldInfo definition = jobType.GetField("def");
        definition.SetValue(current, FormatterServices.GetUninitializedObject(definition.FieldType));
        definition.SetValue(idle, FormatterServices.GetUninitializedObject(definition.FieldType));
        trackerType.GetField("curJob").SetValue(tracker, current);
        MethodInfo shouldStart = trackerType.GetMethod("ShouldStartJobFromThinkTree", BindingFlags.Instance | BindingFlags.NonPublic);
        object continuing = Activator.CreateInstance(resultType, new object[] { current, null, null, false });
        object wandering = Activator.CreateInstance(resultType, new object[] { idle, null, null, false });
        Require(!(bool)shouldStart.Invoke(tracker, new[] { continuing }), "Installed tracker must keep the same current job reference");
        Require((bool)shouldStart.Invoke(tracker, new[] { wandering }), "A different idle job really would interrupt the current job");
        Console.WriteLine("PASS installed job tracker keeps the exact current job reference and replaces it with a different idle result (pure offline decision branch).");
    }

    private static void CheckIngestionObserver(Assembly game, Assembly mod)
    {
        Type pawnType = game.GetType("Verse.Pawn", true);
        Type thingType = game.GetType("Verse.Thing", true);
        Type jobType = game.GetType("Verse.AI.Job", true);
        Type trackerType = game.GetType("Verse.AI.Pawn_JobTracker", true);
        object pawn = FormatterServices.GetUninitializedObject(pawnType);
        object otherPawn = FormatterServices.GetUninitializedObject(pawnType);
        object food = FormatterServices.GetUninitializedObject(thingType);
        object otherFood = FormatterServices.GetUninitializedObject(thingType);
        object job = FormatterServices.GetUninitializedObject(jobType);
        object tracker = FormatterServices.GetUninitializedObject(trackerType);
        pawnType.GetField("jobs").SetValue(pawn, tracker);
        trackerType.GetField("curJob").SetValue(tracker, job);
        Type safety = mod.GetType("RatkinDiningEtiquette.RatDiningMealSafety", true);
        object scope = safety.GetMethod("ObserveIngestion", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new[] { pawn, food, job });
        FieldInfo ate = scope.GetType().GetField("ate", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo callback = mod.GetType("RatkinDiningEtiquette.Thing_Ingested_ClearPreviousChairThought", true)
            .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            callback.Invoke(null, new[] { food, otherPawn, (object)1f });
            callback.Invoke(null, new[] { otherFood, pawn, (object)1f });
            callback.Invoke(null, new[] { food, pawn, (object)0f });
            Require(!(bool)ate.GetValue(scope), "actual callback ignores wrong pawn/food and zero nutrition");
            callback.Invoke(null, new[] { food, pawn, (object).2f });
            Require((bool)ate.GetValue(scope), "actual production callback records positive matching nutrition");
        }
        finally { ((IDisposable)scope).Dispose(); }
        // Private chewing field is the native IEatingDriver integration contract.
        Require(game.GetType("RimWorld.JobDriver_Ingest", true).GetField("chewing", BindingFlags.Instance | BindingFlags.NonPublic) != null,
            "native ingestion chewing field still exists");
        Console.WriteLine("PASS real game-type ingestion observation: positive callback, wrong pawn/food, zero result, scope disposal and native chewing-field contract (no Unity meal execution).");
    }

    private static void CheckHarmonyRegistration(Assembly game, Assembly mod, Assembly toddlers, string harmonyDirectory = null)
    {
        Assembly harmonyAssembly = Assembly.LoadFrom(Path.Combine(harmonyDirectory ?? Harmony, "0Harmony.dll"));
        Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony");
        MethodInfo processor = harmonyType.GetMethod("CreateClassProcessor", new[] { typeof(Type) });
        object own = Activator.CreateInstance(harmonyType, new object[] { "rde.offline.dining.binding" });
        object toddlerOwner = Activator.CreateInstance(harmonyType, new object[] { "cyanobot.toddlers" });
        string[] names = { "RCellFinder_ScopedDiningResult", "Toils_Ingest_ProtectOwnedDiningSpot", "Toils_Ingest_ProtectOwnedDiningMapEdge" };
        foreach (string name in names)
        {
            object patchProcessor = processor.Invoke(own, new object[] { mod.GetType("RatkinDiningEtiquette." + name, true) });
            patchProcessor.GetType().GetMethod("Patch", Type.EmptyTypes).Invoke(patchProcessor, null);
        }
        MethodInfo target = game.GetType("RimWorld.Toils_Ingest").GetMethod("CarryIngestibleToChewSpot");
        // Exercise the factory/wrapper before installing Toddlers. Its actual
        // IsLiveToddler callback initializes ModsConfig and needs native Unity.
        object toil = target.Invoke(null, new object[] { null, Verse.AI.TargetIndex.A });
        Action action = (Action)toil.GetType().GetField("initAction").GetValue(toil);
        Require(action.Target.GetType().DeclaringType.FullName == "RatkinDiningEtiquette.Toils_Ingest_ProtectOwnedDiningSpot", "Real factory must return the production wrapper");
        FieldInfo originalField = action.Target.GetType().GetField("original");
        int originalCalls = 0;
        originalField.SetValue(action.Target, (Action)(() => originalCalls++));
        action();
        Require(originalCalls == 1, "Unowned real toil must retain its original callback");
        Console.WriteLine("PASS real RimWorld factory and production unowned callback dispatcher (probe callback, before native Toddlers initialization).");
        object toddlerProcessor = processor.Invoke(toddlerOwner, new object[] { toddlers.GetType("Toddlers.CarryIngestibleToChewSpot_Patch", true) });
        toddlerProcessor.GetType().GetMethod("Patch", Type.EmptyTypes).Invoke(toddlerProcessor, null);
        object info = harmonyType.GetMethod("GetPatchInfo").Invoke(null, new object[] { target });
        object postfixes = info.GetType().GetField("Postfixes").GetValue(info);
        Array patches = (Array)typeof(Enumerable).GetMethod("ToArray").MakeGenericMethod(harmonyAssembly.GetType("HarmonyLib.Patch"))
            .Invoke(null, new[] { postfixes });
        MethodInfo sortedMethod = harmonyAssembly.GetType("HarmonyLib.PatchProcessor").GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "GetSortedPatchMethods" && (m.GetParameters().Length == 2 || m.GetParameters().Length == 3))
            .OrderBy(m => m.GetParameters().Length).First();
        var sorted = ((System.Collections.IEnumerable)sortedMethod.Invoke(null, sortedMethod.GetParameters().Length == 2
            ? new object[] { target, patches } : new object[] { target, patches, false })).Cast<MethodInfo>().ToArray();
        int nativeIndex = Array.FindIndex(sorted, m => m.DeclaringType.FullName == "Toddlers.CarryIngestibleToChewSpot_Patch");
        int ownIndex = Array.FindIndex(sorted, m => m.DeclaringType.FullName == "RatkinDiningEtiquette.Toils_Ingest_ProtectOwnedDiningSpot");
        Require(nativeIndex >= 0 && ownIndex > nativeIndex, "Own wrapper must run after the actual Toddlers postfix even when installed first");
        Console.WriteLine("PASS real Harmony registration and sorted postfix order against installed RimWorld and Toddlers assemblies (offline process).");
        CheckExecutedGuard(game, mod, own, harmonyAssembly);
        int installed = names.Length;
        foreach (Type type in mod.GetTypes().Where(t => t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")
            && !names.Contains(t.Name) && t.Name != "Page_ModsConfig_DiningIcon"))
        {
            object classProcessor = processor.Invoke(own, new object[] { type });
            classProcessor.GetType().GetMethod("Patch", Type.EmptyTypes).Invoke(classProcessor, null);
            installed++;
        }
        Require(installed == 16, "All sixteen dining production patch classes installed");
        CheckPrisonerSelectionBoundary(game, mod, own, harmonyAssembly);
        Console.WriteLine("PASS all sixteen dining Harmony patch classes install against real game/dependency assemblies. Configuration UI hook is checked by IconHookTests; its asset initializer requires Unity.");
    }

    private static bool selectionProbeHasSpot;
    private static bool AllowSelectionProbe(ref bool __result) { __result = true; return false; }
    private static bool CurrentSpotProbe(ref bool __result, ref Verse.IntVec3 cell)
    {
        __result = selectionProbeHasSpot;
        cell = selectionProbeHasSpot ? new Verse.IntVec3(1, 0, 1) : Verse.IntVec3.Invalid;
        return false;
    }
    private static void ThrowSelectionProbe() { throw new IndexOutOfRangeException("selection-probe-unrelated"); }

    private static void CheckPrisonerSelectionBoundary(Assembly game, Assembly mod, object owner, Assembly harmonyAssembly)
    {
        Type processor = harmonyAssembly.GetType("HarmonyLib.PatchProcessor");
        MethodInfo getInstructions = processor.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2
                && !m.GetParameters()[1].ParameterType.IsByRef);
        MethodInfo getFood = game.GetType("RimWorld.JobGiver_GetFood").GetMethod("TryGiveJob", BindingFlags.Instance | BindingFlags.NonPublic);
        var instructions = ((System.Collections.IEnumerable)getInstructions.Invoke(null, new object[] { getFood, null })).Cast<object>().ToArray();
        var calls = instructions.Select(i => i.GetType().GetField("operand").GetValue(i)).OfType<MethodInfo>().ToArray();
        int selection = Array.FindIndex(calls, m => m.DeclaringType.FullName == "RimWorld.Toils_Ingest" && m.Name == "TryFindChairOrSpot");
        int lastMakeJob = Array.FindLastIndex(calls, m => m.DeclaringType.Name == "JobMaker" && m.Name == "MakeJob");
        Require(selection >= 0 && lastMakeJob > selection, "Installed GetFood checks a chew spot before making its final Ingest job");
        Console.WriteLine("PASS installed GetFood IL performs meal-position preflight before Ingest creation and the existing AI postfix.");

        Type safety = mod.GetType("RatkinDiningEtiquette.RatDiningSpotSafety");
        Type boundary = mod.GetType("RatkinDiningEtiquette.Toils_Ingest_ProtectPrisonerMealSelection");
        MethodInfo target = game.GetType("RimWorld.Toils_Ingest").GetMethod("TryFindChairOrSpot");
        Require(boundary.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).GetParameters().Last().ParameterType == typeof(IDisposable).MakeByRefType(), "Native state contract");
        Require(boundary.GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic).ReturnType == typeof(Exception), "Native exception contract");
        MethodInfo patch = owner.GetType().GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5);
        Type harmonyMethod = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod");
        foreach (var pair in new[] {
            new[] { "CanProtectPrisonerSelection", "AllowSelectionProbe" },
            new[] { "TryCurrentPrisonerSpot", "CurrentSpotProbe" } })
        {
            object prefix = Activator.CreateInstance(harmonyMethod, new object[] { typeof(DiningHookTests).GetMethod(pair[1], BindingFlags.Static | BindingFlags.NonPublic) });
            patch.Invoke(owner, new[] { (object)safety.GetMethod(pair[0], BindingFlags.Static | BindingFlags.NonPublic), prefix, null, null, null });
        }
        // Only force eligibility, the native search result (ForceNoSpot above),
        // and the safe-cell decision. Execute the installed selector and the
        // production RCell guard, boundary finalizer, and scope cleanup in Mono.
        object pawn = FormatterServices.GetUninitializedObject(game.GetType("Verse.Pawn"));
        object food = FormatterServices.GetUninitializedObject(game.GetType("Verse.Thing"));
        game.GetType("Verse.Thing").GetField("def").SetValue(food, FormatterServices.GetUninitializedObject(game.GetType("Verse.ThingDef")));
        foreach (bool hasSpot in new[] { false, true })
        {
            selectionProbeHasSpot = hasSpot;
            object[] args = { pawn, food, Verse.IntVec3.Invalid };
            Require((bool)target.Invoke(null, args) == hasSpot, "Real Harmony boundary preserves safe/absent fallback result");
            Require(((Verse.IntVec3)args[2]).IsValid == hasSpot, "Real Harmony out-cell matches its result");
            Require(!(bool)safety.GetMethod("IsSelecting", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { pawn }), "Real boundary must dispose its owned scope");
        }
        object throwing = Activator.CreateInstance(harmonyMethod, new object[] { typeof(DiningHookTests).GetMethod("ThrowSelectionProbe", BindingFlags.Static | BindingFlags.NonPublic) });
        harmonyMethod.GetField("priority").SetValue(throwing, 0); // After the production scope prefix.
        patch.Invoke(owner, new[] { (object)target, throwing, null, null, null });
        bool propagated = false;
        try { target.Invoke(null, new object[] { pawn, food, Verse.IntVec3.Invalid }); }
        catch (TargetInvocationException error) { propagated = error.InnerException is IndexOutOfRangeException && error.InnerException.Message == "selection-probe-unrelated"; }
        Require(propagated && !(bool)safety.GetMethod("IsSelecting", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { pawn }), "Unrelated native-process exception propagates and scope closes");
        Console.WriteLine("PASS real Harmony selector/finalizer/out-cell contract and scope cleanup with forced eligibility/search/fallback probes; unrelated exceptions remain visible. This is not a full prisoner game reproduction.");
    }

    private static bool ForceNoSpot(ref Verse.IntVec3 __result)
    { __result = Verse.IntVec3.Invalid; return false; }

    private static void CheckExecutedGuard(Assembly game, Assembly mod, object owner, Assembly harmonyAssembly)
    {
        MethodInfo original = game.GetType("RimWorld.RCellFinder").GetMethod("SpotToChewStandingNear");
        Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod");
        object prefix = Activator.CreateInstance(harmonyMethodType, new object[] { typeof(DiningHookTests).GetMethod("ForceNoSpot", BindingFlags.Static | BindingFlags.NonPublic) });
        MethodInfo patch = owner.GetType().GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5);
        patch.Invoke(owner, new[] { (object)original, prefix, null, null, null });
        // Force only the search result; the result check and exception propagation
        // execute through actual Harmony and actual RimWorld structs in Mono.
        object pawn = FormatterServices.GetUninitializedObject(game.GetType("Verse.Pawn"));
        var args = new[] { pawn, null, null };
        object unowned = original.Invoke(null, args);
        Require(!(bool)unowned.GetType().GetProperty("IsValid").GetValue(unowned, null), "Unowned invalid result must be left untouched");
        Type safety = mod.GetType("RatkinDiningEtiquette.RatDiningSpotSafety");
        var scope = (IDisposable)safety.GetMethod("SelectFor", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new[] { pawn, unowned });
        bool caught = false;
        using (scope)
        {
            try { original.Invoke(null, args); }
            catch (TargetInvocationException error) { caught = error.InnerException?.GetType().FullName == "RatkinDiningEtiquette.RatDiningNoChewSpotException"; }
        }
        Require(caught, "Real scoped invalid result must raise the specific signal before any grid access");
        Require(!(bool)safety.GetMethod("IsSelecting", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { pawn }), "Real query scope must dispose");
        original.Invoke(null, args);
        Console.WriteLine("PASS actual Harmony invalid-result callback, scoped exception propagation and restoration of the unowned path (forced search result; no full-game reproduction).");
    }

    private static void CheckHook(Assembly game, Assembly mod, string patchName, string targetType, string methodName, string resultType)
    {
        Type patch = mod.GetType("RatkinDiningEtiquette." + patchName, true);
        MethodInfo[] candidates = game.GetType(targetType, true).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == methodName).ToArray();
        Require(candidates.Length == 1, "Unambiguous installed target: " + methodName);
        MethodInfo target = candidates[0];
        Require(target.ReturnType.FullName == resultType, "Installed return type: " + methodName);
        MethodInfo postfix = patch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (ParameterInfo parameter in postfix.GetParameters())
        {
            Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
            if (parameter.Name == "__result") Require(type == target.ReturnType, "Result parameter: " + patchName);
            else Require(target.GetParameters().Any(p => p.Name == parameter.Name && p.ParameterType == type), "Original named parameter: " + patchName + "." + parameter.Name);
        }
        var attribute = patch.GetCustomAttributesData().Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
        Require((Type)attribute.ConstructorArguments[0].Value == target.DeclaringType
            && (string)attribute.ConstructorArguments[1].Value == methodName, "Harmony target metadata: " + patchName);
        Console.WriteLine("PASS " + targetType + "." + methodName + " matches production postfix metadata.");
    }
}
