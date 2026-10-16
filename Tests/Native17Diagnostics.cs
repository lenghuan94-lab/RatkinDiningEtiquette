using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public static class Native17Diagnostics
{
    public static int Entry(IntPtr context, int size)
    {
        try
        {
            Console.WriteLine("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Assembly game = Assembly.Load("Assembly-CSharp");
            Console.WriteLine("Game: " + game.GetName().FullName);
            if (game.GetName().Version.Major != 1 || game.GetName().Version.Minor != 7)
                throw new InvalidOperationException("Diagnostic requires actual RimWorld 1.7.");
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Native17Diagnostics).Assembly.Location), "..", "..", "..", ".."));
            string harmonyDirectory = @"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
            string toddlersDirectory = @"D:\SteamLibrary\steamapps\workshop\content\294100\2903359152\1.6\Assemblies";
            string newerToddlers = @"D:\SteamLibrary\steamapps\workshop\content\294100\2903359152\1.7\Assemblies";
            if (File.Exists(Path.Combine(newerToddlers, "Toddlers.dll"))) toddlersDirectory = newerToddlers;
            AssemblyLoadContext.Default.Resolving += (loadContext, name) => {
                foreach (string dir in new[] { harmonyDirectory, toddlersDirectory })
                { string path = Path.Combine(dir, name.Name + ".dll"); if (File.Exists(path)) return loadContext.LoadFromAssemblyPath(path); }
                return null;
            };
            Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "1.7", "Assemblies", "RatkinDiningEtiquette.dll"));
            Console.WriteLine("Mod: " + mod.GetName().FullName);
            foreach (Type type in mod.GetTypes()) { }
            Console.WriteLine("PASS all production types load against native 1.7 and existing Toddlers reference.");
            Assembly toddlers = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(toddlersDirectory, "Toddlers.dll"));
            RunCheck(typeof(DiningHookTests), "CheckNativeJobContinuation", game);
            RunCheck(typeof(DiningHookTests), "CheckIngestionObserver", game, mod);
            RunCheck(typeof(NativeSettingsTests), "Run", "1.7", root);
            CheckGeneratedPatches(game, mod, toddlers, Assembly.Load("0Harmony"));
            CheckSelectionIl(game, Assembly.Load("0Harmony"));
            if (Environment.GetEnvironmentVariable("RDE_TEST_DETOUR") == "1")
            {
                CheckDetourBackend(Assembly.Load("0Harmony"));
                RunCheck(typeof(DiningHookTests), "CheckHarmonyRegistration", game, mod, toddlers, harmonyDirectory);
            }
            Console.WriteLine("LIMIT: isolated API, Harmony and Scribe checks; no complete game, pathfinding or dependency gameplay validation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int BackendTarget() => 1;
    private static void BackendPostfix(ref int __result) { __result = 2; }
    private static void CheckDetourBackend(Assembly harmony)
    {
        Console.WriteLine("Checking installed detour backend on a diagnostic method, without any production/game target.");
        Type ownerType = harmony.GetType("HarmonyLib.Harmony", true);
        object owner = Activator.CreateInstance(ownerType, new object[] { "rde.offline.17.backend" });
        object postfix = Activator.CreateInstance(harmony.GetType("HarmonyLib.HarmonyMethod", true), new object[] { typeof(Native17Diagnostics).GetMethod("BackendPostfix", AnyStatic) });
        ownerType.GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5)
            .Invoke(owner, new object[] { typeof(Native17Diagnostics).GetMethod("BackendTarget", AnyStatic), null, postfix, null, null });
        if (BackendTarget() != 2) throw new InvalidOperationException("Detour did not change the diagnostic method.");
        Console.WriteLine("PASS installed Harmony detour backend on diagnostic-only target.");
    }
    private static void CheckGeneratedPatches(Assembly game, Assembly mod, Assembly toddlers, Assembly harmony)
    {
        // Emit the real combined Harmony replacements without redirecting native
        // methods. This tests parameter/IL binding even when the installed
        // MonoMod detour backend cannot initialize in an isolated CoreCLR host.
        Type harmonyType = harmony.GetType("HarmonyLib.Harmony", true);
        object owner = Activator.CreateInstance(harmonyType, new object[] { "rde.offline.17.emit" });
        Type infoType = harmony.GetType("HarmonyLib.PatchInfo", true);
        Type hm = harmony.GetType("HarmonyLib.HarmonyMethod", true);
        Type functions = harmony.GetType("HarmonyLib.PatchFunctions", true);
        MethodInfo getOriginal = harmony.GetTypes().SelectMany(t => t.GetMethods(AnyStatic))
            .Single(m => m.Name == "GetOriginalMethod" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == hm);
        var groups = new Dictionary<MethodBase, object>();
        Type[] classes = mod.GetTypes().Where(t => t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")).ToArray();
        if (classes.Length != 17) throw new InvalidOperationException("Expected 16 dining classes and one configuration UI class.");
        foreach (Type type in classes.Concat(new[] { toddlers.GetType("Toddlers.CarryIngestibleToChewSpot_Patch", true) }))
        {
            object processor = harmonyType.GetMethod("CreateClassProcessor").Invoke(owner, new object[] { type });
            IEnumerable patches = (IEnumerable)processor.GetType().GetField("patchMethods", AnyInstance).GetValue(processor);
            foreach (object patch in patches)
            {
                object attributes = patch.GetType().GetField("info", AnyInstance).GetValue(patch);
                var target = (MethodBase)getOriginal.Invoke(null, new[] { attributes });
                if (target == null) throw new MissingMethodException("Patch target: " + type.FullName);
                if (!groups.TryGetValue(target, out object info)) groups[target] = info = Activator.CreateInstance(infoType);
                string kind = patch.GetType().GetField("type", AnyInstance).GetValue(patch).ToString();
                Array additions = Array.CreateInstance(hm, 1); additions.SetValue(attributes, 0);
                if (kind == "Prefix") infoType.GetMethod("AddPrefixes", AnyInstance).Invoke(info, new object[] { "rde.offline.17.emit", additions });
                // Postfix/Transpiler/Finalizer plural forms differ from Prefix.
                if (kind == "Postfix") infoType.GetMethod("AddPostfixes", AnyInstance).Invoke(info, new object[] { type.Assembly == toddlers ? "cyanobot.toddlers" : "rde.offline.17.emit", additions });
                if (kind == "Transpiler") infoType.GetMethod("AddTranspilers", AnyInstance).Invoke(info, new object[] { "rde.offline.17.emit", additions });
                if (kind == "Finalizer") infoType.GetMethod("AddFinalizers", AnyInstance).Invoke(info, new object[] { "rde.offline.17.emit", additions });
            }
        }
        int generated = 0;
        foreach (var group in groups)
        {
            object Patches(string name) => infoType.GetField(name, AnyInstance).GetValue(group.Value);
            object Calls(string name) => functions.GetMethod("GetSortedPatchCalls", AnyStatic)
                .Invoke(null, new object[] { group.Key, Patches(name), false, null });
            object transpilers = functions.GetMethod("GetSortedPatchMethods", AnyStatic)
                .Invoke(null, new object[] { group.Key, Patches("transpilers"), false });
            object EmptyInfix() => Activator.CreateInstance(typeof(List<>).MakeGenericType(harmony.GetType("HarmonyLib.Infix", true)));
            Type configType = harmony.GetType("HarmonyLib.MethodCreatorConfig", true);
            object config = configType.GetConstructors(AnyInstance).Single(c => c.GetParameters().Length == 10)
                .Invoke(new object[] { group.Key, null, Calls("prefixes"), Calls("postfixes"), transpilers, Calls("finalizers"), EmptyInfix(), EmptyInfix(), EmptyInfix(), false });
            Type creatorType = harmony.GetType("HarmonyLib.MethodCreator", true);
            object creator = creatorType.GetConstructors(AnyInstance).Single().Invoke(new[] { config, (object)true });
            object tuple = creatorType.GetMethod("CreateReplacement", AnyInstance).Invoke(creator, null);
            var replacement = (MethodInfo)tuple.GetType().GetField("Item1").GetValue(tuple);
            if (replacement == null) throw new InvalidOperationException("Replacement generation: " + group.Key);
            if (group.Key.Name == "CarryIngestibleToChewSpot")
            {
                var sorted = ((IEnumerable)functions.GetMethod("GetSortedPatchMethods", AnyStatic)
                    .Invoke(null, new object[] { group.Key, Patches("postfixes"), false })).Cast<MethodInfo>().ToArray();
                int nativeIndex = Array.FindIndex(sorted, m => m.DeclaringType.FullName == "Toddlers.CarryIngestibleToChewSpot_Patch");
                int ownIndex = Array.FindIndex(sorted, m => m.DeclaringType.FullName == "RatkinDiningEtiquette.Toils_Ingest_ProtectOwnedDiningSpot");
                if (nativeIndex < 0 || ownIndex <= nativeIndex) throw new InvalidOperationException("Native Toddlers postfix must precede the owned-meal wrapper.");
                // Toddlers callback itself needs initialized game state. Emit
                // the combined wrapper here, but leave gameplay execution to
                // full-game validation.
                Console.WriteLine("PASS combined Toddlers/owned-meal factory replacement and actual sorted postfix order.");
            }
            generated++;
        }
        Console.WriteLine("PASS all 17 production Harmony classes bind and generate " + generated + " combined native 1.7 replacements; includes configuration-row transpiler. No detour installation claimed.");
    }
    private static void CheckSelectionIl(Assembly game, Assembly harmony)
    {
        MethodInfo reader = harmony.GetType("HarmonyLib.PatchProcessor", true).GetMethods(AnyStatic)
            .Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        MethodInfo target = game.GetType("RimWorld.JobGiver_GetFood", true).GetMethod("TryGiveJob", BindingFlags.Instance | BindingFlags.NonPublic);
        object[] instructions = ((IEnumerable)reader.Invoke(null, new object[] { target, null })).Cast<object>().ToArray();
        MethodInfo[] calls = instructions.Select(i => i.GetType().GetField("operand").GetValue(i)).OfType<MethodInfo>().ToArray();
        int selection = Array.FindIndex(calls, m => m.DeclaringType.FullName == "RimWorld.Toils_Ingest" && m.Name == "TryFindChairOrSpot");
        int creation = Array.FindLastIndex(calls, m => m.DeclaringType.Name == "JobMaker" && m.Name == "MakeJob");
        if (selection < 0 || creation <= selection) throw new InvalidOperationException("1.7 GetFood selection boundary changed.");
        Console.WriteLine("PASS actual 1.7 GetFood IL retains chair-position selection before the final Ingest job creation.");
    }
    private static void RunCheck(Type type, string method, params object[] args)
    {
        try { type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
}
