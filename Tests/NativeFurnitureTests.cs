using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using RimWorld;
using Verse;

// Read upstream 1.6 metadata, instantiate uninitialized type-shaped objects,
// then exercise the production classifier without Unity or hygiene toils.
internal static class NativeFurnitureTests
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static int Main()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string reference = Path.Combine(root, ".tools", "release-1.2.0", "dbh-reference");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            foreach (string folder in new[] { reference,
                Path.Combine(root, ".tools", "dining-audit-2026-10-03", "bin", "Release", "net472"),
                @"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.6\Assemblies",
                @"D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed",
                @"D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies",
                @"D:\SteamLibrary\steamapps\workshop\content\294100\2903359152\1.6\Assemblies" })
            {
                string path = Path.Combine(folder, new AssemblyName(args.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try
        {
            Assembly hygiene = Assembly.LoadFrom(Path.Combine(reference, "BadHygiene.dll"));
            Assembly mod = Assembly.LoadFrom(Path.Combine(root, "1.6", "Assemblies", "RatkinDiningEtiquette.dll"));
            Type classifier = mod.GetType("RatkinDiningEtiquette.RatDiningFurniture", true);
            MethodInfo kind = classifier.GetMethod("Kind", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo seat = classifier.GetMethod("IsDiningSeat", BindingFlags.Static | BindingFlags.NonPublic);
            var xml = new XmlDocument(); xml.Load(Path.Combine(reference, "BuildingsB_Hygiene.xml"));
            int toilets = 0;
            foreach (string name in new[] { "PitLatrine", "ToiletStuff", "ToiletAdv", "ToiletAdvStuff", "ToiletSpacer" })
            {
                XmlNode node = xml.SelectSingleNode("/Defs/ThingDef[defName='" + name + "']");
                Require(node != null, "upstream toilet exists: " + name);
                string className = Inherited(node, "thingClass", xml);
                Type buildingType = hygiene.GetType(className, true);
                Require(typeof(Building).IsAssignableFrom(buildingType), "native building inheritance");
                var building = (Building)FormatterServices.GetUninitializedObject(buildingType);
                var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
                def.defName = name; def.thingClass = buildingType;
                def.building = new BuildingProperties { isSittable = Inherited(node, "building/isSittable", xml) == "true" };
                building.def = def;
                Require((bool)seat.Invoke(null, new object[] { building }), "real toilet qualifies without chair flag");
                Require(kind.Invoke(null, new object[] { building }).ToString() == "Toilet", "native class recognized: " + name);
                // Name-independent classification must work for modded variants.
                def.defName = "NeutralReskinName";
                Require(kind.Invoke(null, new object[] { building }).ToString() == "Toilet", "real class reskin");
                Console.WriteLine("PASS native hygiene definition " + name + ": " + className + "; isSittable=" + def.building.isSittable);
                toilets++;
            }
            var doorNode = xml.SelectSingleNode("/Defs/ThingDef[defName='ToiletStallDoor']");
            Type doorType = hygiene.GetType(Inherited(doorNode, "thingClass", xml), true);
            var door = (Building)FormatterServices.GetUninitializedObject(doorType);
            door.def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            door.def.defName = "ToiletStallDoor"; door.def.thingClass = doorType; door.def.building = new BuildingProperties();
            Require(!(bool)seat.Invoke(null, new object[] { door }), "stall door excluded");
            Console.WriteLine("PASS " + toilets + " upstream 1.6 toilet definitions, inherited classes, name-independent variants, and stall-door exclusion.");
            Console.WriteLine("LIMIT: upstream snapshot metadata and production classification only; no hygiene gameplay, Unity, pathfinding or complete meal integration.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string Inherited(XmlNode node, string field, XmlDocument xml)
    {
        for (int depth = 0; node != null && depth < 32; depth++)
        {
            XmlNode value = node.SelectSingleNode(field);
            if (value != null) return value.InnerText;
            string parent = node.Attributes["ParentName"]?.Value;
            node = parent == null ? null : xml.SelectSingleNode("/Defs/ThingDef[@Name='" + parent + "']");
        }
        return null;
    }
}
