using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

// Isolated 1.7 CoreCLR diagnostic host; no game entry point or Unity renderer.
internal static class GameCoreRunner
{
    private const string Game = @"D:\SteamLibrary\steamapps\common\RimWorld";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
    [DllImport("coreclr.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int coreclr_initialize(byte[] path, byte[] name, int count, IntPtr keys, IntPtr values, out IntPtr host, out uint domain);
    [DllImport("coreclr.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int coreclr_create_delegate(IntPtr host, uint domain, byte[] assembly, byte[] type, byte[] method, out IntPtr entry);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Entry(IntPtr context, int size);
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");
    private static IntPtr Strings(IEnumerable<string> values, List<IntPtr> allocations)
    {
        string[] array = values.ToArray(); IntPtr result = Marshal.AllocHGlobal(array.Length * IntPtr.Size); allocations.Add(result);
        for (int i = 0; i < array.Length; i++)
        {
            byte[] bytes = Utf8(array[i]); IntPtr item = Marshal.AllocHGlobal(bytes.Length); allocations.Add(item);
            Marshal.Copy(bytes, 0, item, bytes.Length); Marshal.WriteIntPtr(result, i * IntPtr.Size, item);
        }
        return result;
    }
    private static int Main(string[] args)
    {
        var allocations = new List<IntPtr>();
        try
        {
            string diagnostic = Path.GetFullPath(args[0]);
            var dlls = Directory.GetFiles(Game, "*.dll").Where(IsManaged).ToList();
            dlls.Add(diagnostic);
            var properties = new Dictionary<string, string> {
                { "TRUSTED_PLATFORM_ASSEMBLIES", string.Join(";", dlls) },
                { "APP_PATHS", Game + ";" + Path.GetDirectoryName(diagnostic) },
                { "APP_CONTEXT_BASE_DIRECTORY", Game + "\\" },
                { "NATIVE_DLL_SEARCH_DIRECTORIES", Game }
            };
            var configuration = (Dictionary<string, object>)new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(Path.Combine(Game, "RimWorld.runtimeconfig.json")));
            var options = (Dictionary<string, object>)configuration["runtimeOptions"];
            foreach (var property in (Dictionary<string, object>)options["configProperties"])
                properties[property.Key] = property.Value is bool ? property.Value.ToString().ToLowerInvariant() : property.Value.ToString();
            SetDllDirectory(Game);
            int code = coreclr_initialize(Utf8(diagnostic), Utf8("RDE 1.7 offline checks"), properties.Count,
                Strings(properties.Keys, allocations), Strings(properties.Values, allocations), out IntPtr host, out uint domain);
            if (code < 0) throw new InvalidOperationException("CoreCLR init: " + code.ToString("X8"));
            string name = Path.GetFileNameWithoutExtension(diagnostic);
            code = coreclr_create_delegate(host, domain, Utf8(name), Utf8("Native17Diagnostics"), Utf8("Entry"), out IntPtr entry);
            if (code < 0) throw new InvalidOperationException("CoreCLR entry: " + code.ToString("X8"));
            return ((Entry)Marshal.GetDelegateForFunctionPointer(entry, typeof(Entry)))(IntPtr.Zero, 0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { foreach (IntPtr allocation in allocations) Marshal.FreeHGlobal(allocation); }
    }
    private static bool IsManaged(string file)
    { try { AssemblyName.GetAssemblyName(file); return true; } catch (BadImageFormatException) { return false; } }
}
