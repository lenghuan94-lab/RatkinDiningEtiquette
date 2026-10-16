using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RatkinDiningEtiquette;
using UnityEngine;

internal static class IconHookTests
{
    private static void Main(string[] args)
    {
        string managed = Path.Combine(args.Length > 0 ? args[0] : @"D:\SteamLibrary\steamapps\common\RimWorld", "RimWorldWin64_Data", "Managed");
        if (args.Length == 0 && !Directory.Exists(managed))
            managed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".tools", "dining-audit-2026-10-03", "bin", "Release", "net472"));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run(managed); }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
            if (e.InnerException != null) Console.Error.WriteLine(e.InnerException.GetType().FullName + ": " + e.InnerException.Message);
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string managed)
    {
        Console.WriteLine("Reading installed configuration-row IL.");
        ConstructorInfo copyRect = AccessTools.Constructor(typeof(Rect), new[] { typeof(Rect) });
        var generator = new DynamicMethod("ReadIconHook", typeof(void), Type.EmptyTypes).GetILGenerator();
        // Read Mono/Unity assemblies without invoking native game initializers.
        var opcodeMap = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Name);
        List<CodeInstruction> original = new List<CodeInstruction>();
        foreach (var instruction in ReadInstructions(Path.Combine(managed, "Assembly-CSharp.dll"), "RimWorld.Page_ModsConfig", "DoModRow"))
        {
            object operand = instruction.Operand;
            if (instruction.Opcode == "call" && (string)Property(operand, "FullName") == "System.Void UnityEngine.Rect::.ctor(UnityEngine.Rect)") operand = copyRect;
            original.Add(new CodeInstruction(opcodeMap[instruction.Opcode], operand));
        }
        Require(original.Count(i => i.opcode == OpCodes.Call && Equals(i.operand, copyRect)) == 1, "One supported name-rectangle hook in installed RimWorld 1.6");
        int originalIndex = original.FindIndex(i => i.opcode == OpCodes.Call && Equals(i.operand, copyRect));
        original[originalIndex].labels.Add(generator.DefineLabel());
        original[originalIndex].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var patched = Page_ModsConfig_DiningIcon.Transpiler(original).ToList();
        Require(patched.Count == original.Count + 1, "Only one metadata argument added");
        Require(patched[originalIndex].opcode == OpCodes.Ldarg_2, "Hook receives the correct mod metadata argument");
        Require(patched[originalIndex + 1].opcode == OpCodes.Call && (patched[originalIndex + 1].operand as MethodInfo)?.Name == "InitializeNameRect", "Only name rectangle is replaced");
        for (int i = 0; i < original.Count; i++)
        {
            if (i == originalIndex) continue;
            Require(ReferenceEquals(original[i], patched[i < originalIndex ? i : i + 1]), "Other instructions are preserved");
        }
        Require(patched[originalIndex].labels.SequenceEqual(original[originalIndex].labels), "Branch labels preserved");
        Require(patched[originalIndex].blocks.SequenceEqual(original[originalIndex].blocks), "Exception markers preserved");
        Require(patched.Any(i => (string)Property(i.operand, "Name") == "DrawContentSource"), "Original source marker retained");

        Console.WriteLine("PASS row IL preserves source marker, branch labels and other instructions.");
        // Check the package gate in IL without calling native Unity GUI APIs.
        var helper = ReadInstructions(Path.GetFullPath("1.6/Assemblies/RatkinDiningEtiquette.dll"), "RatkinDiningEtiquette.Page_ModsConfig_DiningIcon", "InitializeNameRect");
        int packageGate = helper.FindIndex(i => (string)Property(i.Operand, "Name") == "SamePackageId");
        int iconLookup = helper.FindIndex(i => (string)Property(i.Operand, "Name") == "get_Icon");
        Require(packageGate >= 0 && iconLookup > packageGate, "Icon lookup happens only after the own-package gate");
        Require(helper.Any(i => i.Opcode == "ldstr" && Equals(i.Operand, "lenghuan.ratkindiningetiquette")), "Package gate uses this mod's identifier");
        Console.WriteLine("PASS configuration-list icon hook against installed RimWorld IL; source marker, other instructions and unrelated layouts retained. Actual game rendering still requires visual validation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class OfflineInstruction
    {
        internal string Opcode;
        internal object Operand;
    }

    private static object Property(object value, string name) => value?.GetType().GetProperty(name)?.GetValue(value, null);

    private static List<OfflineInstruction> ReadInstructions(string path, string typeName, string methodName)
    {
        // Harmony bundles an internal Cecil reader; reflection avoids adding
        // a second Cecil dependency or initializing any native Unity class.
        Type moduleType = typeof(Harmony).Assembly.GetType("Mono.Cecil.ModuleDefinition", true);
        object module = moduleType.GetMethod("ReadModule", new[] { typeof(string) }).Invoke(null, new object[] { path });
        using ((IDisposable)module)
        {
            object type = ((IEnumerable)Property(module, "Types")).Cast<object>().Single(t => (string)Property(t, "FullName") == typeName);
            object method = ((IEnumerable)Property(type, "Methods")).Cast<object>().Single(m => (string)Property(m, "Name") == methodName);
            return ((IEnumerable)Property(Property(method, "Body"), "Instructions")).Cast<object>()
                .Select(i => new OfflineInstruction { Opcode = (string)Property(Property(i, "OpCode"), "Name"), Operand = Property(i, "Operand") }).ToList();
        }
    }
}
