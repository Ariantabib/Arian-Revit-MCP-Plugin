using System.Reflection;
using System.Runtime.Loader;
using Arian.RevitMcp.Core;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Server;

/// <summary>
/// Loads Arian.RevitMcp.Tools.dll into a collectible AssemblyLoadContext from a byte stream (so the file is never locked)
/// and can swap it for a newer build at any time without restarting Revit.
/// </summary>
public static class ToolLoader
{
    static AssemblyLoadContext _alc;
    static int _counter;
    public static Assembly Current { get; private set; }
    public static string LoadedFrom { get; private set; }
    public static DateTime? LoadedAt { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Path.GetDirectoryName(typeof(ToolLoader).Assembly.Location), "Arian.RevitMcp.Tools.dll");

    /// <summary>Loads (or reloads) the tools assembly and rebuilds the registry.</summary>
    public static string Load(string path = null)
    {
        path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        if (!File.Exists(path)) throw new ToolException("Tools assembly not found: " + path);
        var alc = new AssemblyLoadContext("ArianTools#" + (++_counter), isCollectible: true);
        Assembly asm;
        using (var fs = new MemoryStream(File.ReadAllBytes(path)))
        {
            string pdb = Path.ChangeExtension(path, ".pdb");
            if (File.Exists(pdb)) { using var ps = new MemoryStream(File.ReadAllBytes(pdb)); asm = alc.LoadFromStream(fs, ps); }
            else asm = alc.LoadFromStream(fs);
        }
        var old = _alc;
        _alc = alc; Current = asm; LoadedFrom = path; LoadedAt = DateTime.Now;
        ToolRegistry.Build();
        if (old != null) { try { old.Unload(); } catch { } }
        GC.Collect(); GC.WaitForPendingFinalizers();
        Log.Info($"Tools assembly loaded from {path} ({asm.GetName().Version})");
        return asm.GetName().Version?.ToString();
    }
}
