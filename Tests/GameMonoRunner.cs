using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Embed the installed game's Mono in a separate diagnostic process. No Unity
// player is started and no installation/configuration/save files are modified.
internal static class GameMonoRunner
{
    private const string GameRoot = @"D:\SteamLibrary\steamapps\common\RimWorld";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mono_set_dirs(byte[] assemblyDir, byte[] configDir);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mono_set_assemblies_path(byte[] path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mono_jit_init_version(byte[] name, byte[] version);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mono_domain_assembly_open(IntPtr domain, byte[] name);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mono_jit_exec(IntPtr domain, IntPtr assembly, int argc, IntPtr argv);
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

    private static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: GameMonoRunner <diagnostic.exe> [arguments]"); return 2; }
        IntPtr argv = IntPtr.Zero;
        var strings = new IntPtr[args.Length];
        try
        {
            string executable = Path.GetFullPath(args[0]);
            if (!File.Exists(executable)) throw new FileNotFoundException("Diagnostic executable", executable);
            // Unity's embedded Mono reports dynamic-assembly CodeBase through
            // its ANSI cwd. Use the existing ASCII installation path only as
            // this process's working directory; the diagnostic path is absolute.
            Environment.CurrentDirectory = GameRoot;
            if (!SetDllDirectory(Path.Combine(GameRoot, "MonoBleedingEdge", "EmbedRuntime"))) throw new InvalidOperationException("Mono library lookup setup failed.");
            string managed = Path.Combine(GameRoot, "RimWorldWin64_Data", "Managed");
            mono_set_dirs(Utf8(managed), Utf8(Path.Combine(GameRoot, "MonoBleedingEdge", "etc")));
            mono_set_assemblies_path(Utf8(managed));
            IntPtr domain = mono_jit_init_version(Utf8("RDE offline checks"), Utf8("v4.0.30319"));
            if (domain == IntPtr.Zero) throw new InvalidOperationException("Game Mono initialization failed.");
            IntPtr assembly = mono_domain_assembly_open(domain, Utf8(executable));
            if (assembly == IntPtr.Zero) throw new InvalidOperationException("Game Mono could not load the diagnostic.");
            argv = Marshal.AllocHGlobal((args.Length + 1) * IntPtr.Size);
            for (int i = 0; i < args.Length; i++)
            {
                byte[] bytes = Utf8(i == 0 ? executable : args[i]);
                strings[i] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, strings[i], bytes.Length);
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
            }
            Marshal.WriteIntPtr(argv, args.Length * IntPtr.Size, IntPtr.Zero);
            Console.WriteLine("Running offline diagnostic with RimWorld's installed Mono runtime.");
            int result = mono_jit_exec(domain, assembly, args.Length, argv);
            Console.WriteLine("Game Mono diagnostic exit code: " + result);
            return result;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            foreach (IntPtr value in strings) if (value != IntPtr.Zero) Marshal.FreeHGlobal(value);
            if (argv != IntPtr.Zero) Marshal.FreeHGlobal(argv);
        }
    }
}
