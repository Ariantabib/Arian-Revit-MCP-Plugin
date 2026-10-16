using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arian.RevitMcp.Core;

public enum WarningPolicy
{
    /// <summary>Revit behaves normally (warnings show up in the Warnings dialog).</summary>
    Off,
    /// <summary>Warnings raised during transactions are silently deleted (a.k.a. "ignore").</summary>
    IgnoreWarnings,
    /// <summary>Warnings are ignored AND errors are auto-resolved with Revit's default resolution when one exists.</summary>
    IgnoreAndResolveErrors,
}

public sealed class PluginSettings
{
    public int Port { get; set; } = 8765;
    public bool AutoStartServer { get; set; } = false;
    /// <summary>Optional bearer token; empty = no authentication (loopback only).</summary>
    public string AuthToken { get; set; } = "";
    public bool AllowCodeExecution { get; set; } = true;
    public WarningPolicy WarningPolicy { get; set; } = WarningPolicy.IgnoreWarnings;
    /// <summary>Auto-answer Revit task dialogs / message boxes with the least destructive choice.</summary>
    public bool AutoDismissDialogs { get; set; } = true;
    /// <summary>Hard timeout (seconds) for one tool call executing on the Revit thread.</summary>
    public int ToolTimeoutSeconds { get; set; } = 600;
    /// <summary>Comma separated categories to expose in tools/list; empty = all.</summary>
    public string EnabledCategories { get; set; } = "";
    /// <summary>Per-dialog-id answers (TaskDialogResult name) used when AutoDismissDialogs is on.</summary>
    public Dictionary<string, string> DialogRules { get; set; } = new();
    /// <summary>Extra folders searched for .rfa families by search_family_library.</summary>
    public List<string> FamilyLibraryDirs { get; set; } = new();
    /// <summary>UI language: en | fa (Persian).</summary>
    public string Language { get; set; } = "en";
    /// <summary>Comma separated tool categories in which the agent may NOT change the model (read-only tools still work).</summary>
    public string ReadOnlyCategories { get; set; } = "";
    /// <summary>Name of the last selected tool profile (informational; EnabledCategories is what is enforced).</summary>
    public string ToolProfile { get; set; } = "All";

    [JsonIgnore] public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arian", "RevitMcp");
    [JsonIgnore] public static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    public static PluginSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(FilePath), Opts) ?? new PluginSettings();
            else Save();
        }
        catch (Exception ex) { Log.Error("Settings load failed: " + ex.Message); Current = new PluginSettings(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Opts));
        }
        catch (Exception ex) { Log.Error("Settings save failed: " + ex.Message); }
    }
}

public static class Log
{
    static readonly object Gate = new();
    static readonly Queue<string> Ring = new();
    public static string LogDir => Path.Combine(PluginSettings.Dir, "logs");

    public static void Info(string m) => Write("INFO", m);
    public static void Warn(string m) => Write("WARN", m);
    public static void Error(string m) => Write("ERROR", m);

    static void Write(string lvl, string m)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{lvl}] {m}";
        lock (Gate)
        {
            Ring.Enqueue(line);
            while (Ring.Count > 500) Ring.Dequeue();
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), line + Environment.NewLine);
            }
            catch { }
        }
    }

    public static string[] Tail(int n = 100)
    {
        lock (Gate) return Ring.Skip(Math.Max(0, Ring.Count - n)).ToArray();
    }
}
