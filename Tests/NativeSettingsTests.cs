using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using Verse;

internal static class NativeSettingsTests
{
    private static readonly string Managed = Directory.Exists(@"D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed")
        ? @"D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".tools", "dining-audit-2026-10-03", "bin", "Release", "net472"));

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string path = Path.Combine(Managed, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string runtimeVersion = "1.6", string repository = null)
    {
        repository = repository ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string output = Path.Combine(repository, ".tools", "toddler-fix-1.2.1", "native-settings-" + runtimeVersion);
        Directory.CreateDirectory(output);
        Assembly mod = Assembly.LoadFrom(Path.Combine(repository, runtimeVersion, "Assemblies", "RatkinDiningEtiquette.dll"));
        Type settingsType = mod.GetType("RatkinDiningEtiquette.RatDiningSettings", true);
        FieldInfo[] fields = Array.ConvertAll(new[] { "enableColonistToddlerHelp", "enableVisitorToddlerHelp",
            "enablePrisonerToddlerHelp", "enableSlaveToddlerHelp", "enableColonistSpecialSeats", "enableVisitorSpecialSeats",
            "enablePrisonerSpecialSeats", "enableSlaveSpecialSeats" }, name => settingsType.GetField(name));
        FieldInfo master = settingsType.GetField("enableToddlerHelp");
        // Only initialize the in-process preferences object needed by native
        // Scribe's debug-ID checker. Do not call Prefs.Init/Save or touch Config.
        FieldInfo preferences = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = preferences.GetValue(null);
        preferences.SetValue(null, FormatterServices.GetUninitializedObject(preferences.FieldType));
        try
        {
            for (int mask = 0; mask < (1 << fields.Length); mask++)
            {
                foreach (bool enabled in new[] { false, true })
                {
                    var settings = (IExposable)Activator.CreateInstance(settingsType);
                    for (int i = 0; i < fields.Length; i++) fields[i].SetValue(settings, (mask & (1 << i)) != 0);
                    master.SetValue(settings, enabled);
                    string path = Path.Combine(output, "selection-" + mask + "-" + enabled + ".xml");
                    Scribe.saver.InitSaving(path, "Settings"); settings.ExposeData(); Scribe.saver.FinalizeSaving();
                    var xml = new XmlDocument(); xml.Load(path);
                    Require(xml.DocumentElement.Name == "Settings", "native root node");
                    // Native Scribe omits values equal to their supplied default.
                    for (int i = 0; i < fields.Length; i++)
                    {
                        bool expected = (mask & (1 << i)) != 0;
                        XmlNode node = xml.DocumentElement[fields[i].Name];
                        Require((node == null) == (expected == (i == 0 || i >= 6)), "native omission matches category default");
                        if (node != null) Require(bool.Parse(node.InnerText) == expected, "native XML stores chosen flag");
                    }
                    var loaded = (IExposable)Activator.CreateInstance(settingsType);
                    Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.FinalizeLoading();
                    Require((bool)master.GetValue(loaded) == enabled, "master survives real XML round trip");
                    for (int i = 0; i < fields.Length; i++)
                        Require((bool)fields[i].GetValue(loaded) == ((mask & (1 << i)) != 0), "category survives real XML round trip");
                    Require(Scribe.mode == LoadSaveMode.Inactive, "native serializer closes cleanly");
                }
            }
            foreach (bool enabled in new[] { false, true })
            {
                string path = Path.Combine(output, "legacy-" + enabled + ".xml");
                File.WriteAllText(path, "<Settings><enableToddlerHelp>" + enabled + "</enableToddlerHelp></Settings>");
                var loaded = (IExposable)Activator.CreateInstance(settingsType);
                Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.FinalizeLoading();
                Require((bool)master.GetValue(loaded) == enabled, "legacy master retained");
                for (int i = 0; i < fields.Length; i++)
                    Require((bool)fields[i].GetValue(loaded) == (i == 0 || i >= 6), "missing new key uses actual native default");
            }
            string wrappedPath = Path.Combine(output, "mod-settings-wrapper.xml");
            ModSettings wrapped = (ModSettings)Activator.CreateInstance(settingsType);
            master.SetValue(wrapped, false); fields[0].SetValue(wrapped, false); fields[2].SetValue(wrapped, true);
            Scribe.saver.InitSaving(wrappedPath, "SettingsBlock");
            Scribe_Deep.Look(ref wrapped, "ModSettings"); Scribe.saver.FinalizeSaving();
            var wrapperXml = new XmlDocument(); wrapperXml.Load(wrappedPath);
            Require(wrapperXml.DocumentElement["ModSettings"].GetAttribute("Class") == settingsType.FullName,
                "saved settings retain full runtime class identity after moving source file");
            // Native type lookup uses the initialized running-mod registry,
            // not every assembly loaded into this diagnostic process. Read primitives
            // through the real node navigator without claiming deep loading.
            ModSettings wrappedLoaded = (ModSettings)Activator.CreateInstance(settingsType);
            Scribe.loader.InitLoading(wrappedPath);
            Require(Scribe.EnterNode("ModSettings"), "native wrapper node navigation");
            wrappedLoaded.ExposeData(); Scribe.ExitNode(); Scribe.loader.FinalizeLoading();
            Require(wrappedLoaded != null && wrappedLoaded.GetType() == settingsType
                && !(bool)master.GetValue(wrappedLoaded) && !(bool)fields[0].GetValue(wrappedLoaded)
                && (bool)fields[2].GetValue(wrappedLoaded) && !(bool)fields[1].GetValue(wrappedLoaded)
                && !(bool)fields[3].GetValue(wrappedLoaded), "native wrapper node reloads primitive flags");
            Console.WriteLine("PASS native RimWorld Scribe XML round trips: 512 help/special-seat/master combinations, default-value omission, and 2 legacy files with missing keys.");
            Console.WriteLine("PASS native SettingsBlock/ModSettings wrapper writes the unchanged Class tag and reloads flags via native node navigation.");
            CheckRequestCompletionFields(mod, output);
            Console.WriteLine("LIMIT: in-memory debug preferences only; deep XML class reconstruction needs game initialization. No Unity, Config, settings UI, pawn save/load, or complete game initialization.");
        }
        finally { preferences.SetValue(null, previous); }
    }

    private static void CheckRequestCompletionFields(Assembly mod, string output)
    {
        Type type = mod.GetType("RatkinDiningEtiquette.RatToddlerDiningRequest", true);
        FieldInfo completed = type.GetField("mealFinalized");
        FieldInfo jobId = type.GetField("mealJobId");
        foreach (bool flag in new[] { false, true }) foreach (int id in new[] { -1, 0, 152938 })
        {
            var request = (IExposable)Activator.CreateInstance(type);
            completed.SetValue(request, flag); jobId.SetValue(request, id);
            string path = Path.Combine(output, "request-completion-" + flag + "-" + id + ".xml");
            Scribe.saver.InitSaving(path, "Request"); request.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = (IExposable)Activator.CreateInstance(type);
            Scribe.loader.InitLoading(path);
            Scribe.loader.curParent = loaded;
            Scribe.loader.crossRefs.RegisterForCrossRefResolve(loaded);
            loaded.ExposeData(); Scribe.loader.FinalizeLoading();
            Require((bool)completed.GetValue(loaded) == flag && (int)jobId.GetValue(loaded) == id, "native completed request fields round trip");
        }
        string legacyPath = Path.Combine(output, "legacy-request-completion.xml");
        File.WriteAllText(legacyPath, "<Request><id>9</id><state>EatingOnChair</state></Request>");
        var legacy = (IExposable)Activator.CreateInstance(type);
        completed.SetValue(legacy, true); jobId.SetValue(legacy, 777);
        Scribe.loader.InitLoading(legacyPath);
        Scribe.loader.curParent = legacy;
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(legacy);
        legacy.ExposeData(); Scribe.loader.FinalizeLoading();
        Require(!(bool)completed.GetValue(legacy) && (int)jobId.GetValue(legacy) == -1, "legacy request cannot fabricate successful meal");
        Console.WriteLine("PASS native request completion XML: 6 finalized/job-ID combinations and missing-key legacy defaults. Pawn references and deep game save reconstruction are not exercised.");
    }
}
