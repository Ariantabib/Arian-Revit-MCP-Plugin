using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Core;

/// <summary>
/// Runs Dynamo for Revit graphs (.dyn) through Dynamo's own external command (the same one as the ribbon button),
/// in automation mode. Because Dynamo's command needs a genuine ExternalCommandData, we post OUR ribbon command, which then forwards it.
/// </summary>
public static class DynamoRunner
{
    public sealed class Job
    {
        public string Path;
        public bool ShowUi;
        public bool Automation = true;
        public DateTime Queued = DateTime.Now;
    }

    public static Job Pending;
    public static string State = "idle";          // idle | queued | running | started | finished | error
    public static string LastMessage;
    public static string LastPath;
    public static DateTime? LastFinished;
    /// <summary>Nodes of the last evaluated graph that ended in warning/error ("Name: tooltip"), filled when the run completes.</summary>
    public static List<string> LastNodeIssues = new();
    public static int LastNodeCount;
    public static int LastEvaluations;

    /// <summary>Subscribes to the workspace's EvaluationCompleted event (reflection) so the state becomes 'finished' when the run really ends.</summary>
    static void HookCompletion(object ws, string file)
    {
        try
        {
            LastNodeIssues = new(); LastNodeCount = 0;
            var ev = ws.GetType().GetEvent("EvaluationCompleted");
            if (ev == null) return;
            var m = typeof(DynamoRunner).GetMethod(nameof(OnEvaluationCompleted), BindingFlags.NonPublic | BindingFlags.Static);
            var del = Delegate.CreateDelegate(ev.EventHandlerType, m);
            try { ev.RemoveEventHandler(ws, del); } catch { }
            ev.AddEventHandler(ws, del);
        }
        catch (Exception ex) { Log.Warn("Dynamo completion hook failed: " + ex.Message); }
    }

    static void OnEvaluationCompleted(object sender, EventArgs e)
    {
        try
        {
            var issues = new List<string>();
            int count = 0;
            if (sender?.GetType().GetProperty("Nodes")?.GetValue(sender) is System.Collections.IEnumerable nodes)
                foreach (var n in nodes)
                {
                    count++;
                    var st = n.GetType().GetProperty("State")?.GetValue(n)?.ToString();
                    if (st == null || st == "Active" || st == "Dead") continue;
                    string name = n.GetType().GetProperty("Name")?.GetValue(n)?.ToString() ?? "?";
                    string tip = n.GetType().GetProperty("ToolTipText")?.GetValue(n)?.ToString() ?? "";
                    // warning texts live in NodeModel.Infos (Info.Message); ToolTipText is often empty
                    try
                    {
                        if (n.GetType().GetProperty("Infos")?.GetValue(n) is System.Collections.IEnumerable infos)
                            foreach (var inf in infos)
                            {
                                var msg = inf.GetType().GetProperty("Message")?.GetValue(inf)?.ToString();
                                if (!string.IsNullOrWhiteSpace(msg)) tip += " " + msg;
                            }
                    }
                    catch { }
                    tip = string.Join(" ", tip.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                    if (tip.Length > 300) tip = tip.Substring(0, 300) + "...";
                    issues.Add($"{name} [{st}]: {tip}".Trim());
                }
            LastNodeIssues = issues; LastNodeCount = count; LastEvaluations++;
            State = "finished";
            LastMessage = issues.Count == 0 ? $"Graph evaluated ({count} nodes, no warnings)." : $"Graph evaluated ({count} nodes) with {issues.Count} node warning(s)/error(s).";
            LastFinished = DateTime.Now;
        }
        catch (Exception ex) { Log.Warn("Dynamo completion handler: " + ex.Message); }
    }

    public const string CommandIdString = "CustomCtrl_%CustomCtrl_%Arian%Automation%ArianRunDynamo";

    /// <summary>Queue a graph; it runs as soon as Revit returns to idle after the current tool call.</summary>
    public static void Queue(UIApplication uiapp, string path, bool showUi)
    {
        if (!File.Exists(path)) throw new ToolException("Graph not found: " + path);
        Pending = new Job { Path = path, ShowUi = showUi };
        State = "queued"; LastMessage = null; LastPath = path;
        var id = RevitCommandId.LookupCommandId(CommandIdString);
        if (id == null) throw new ToolException("The 'Run Dynamo' ribbon command is not available (plugin ribbon failed to load).");
        uiapp.PostCommand(id);
    }

    static Type FindDynamoCommand()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name != "DynamoRevitDS") continue;
            var t = a.GetType("Dynamo.Applications.DynamoRevit");
            if (t != null) return t;
        }
        var path = @"C:\Program Files\Autodesk\Revit 2027\AddIns\DynamoForRevit\Revit\DynamoRevitDS.dll";
        if (File.Exists(path))
        {
            var asm = Assembly.LoadFrom(path);
            return asm.GetType("Dynamo.Applications.DynamoRevit");
        }
        return null;
    }

    static bool TryRun(Type dynType, string path)
    {
        var model = dynType.GetProperty("RevitDynamoModel")?.GetValue(null);
        var ws = model?.GetType().GetProperty("CurrentWorkspace")?.GetValue(model);
        if (ws == null) return false;
        string file = ws.GetType().GetProperty("FileName")?.GetValue(ws) as string;
        // the graph must really be the open one (an empty home workspace has no file name)
        if (string.IsNullOrEmpty(file) || !string.Equals(System.IO.Path.GetFileName(file), System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) return false;
        var run = ws.GetType().GetMethod("Run", Type.EmptyTypes);
        if (run == null) return false;
        HookCompletion(ws, file);
        run.Invoke(ws, null);
        return true;
    }

    /// <summary>Asks Dynamo (through its own command class, with our journal data) to open the graph.</summary>
    static object OpenGraph(UIApplication app, Type type, Job job)
    {
        var asm = type.Assembly;
        var cdType = asm.GetType("Dynamo.Applications.DynamoRevitCommandData") ?? throw new ToolException("DynamoRevitCommandData type not found (unsupported Dynamo version).");
        var cd = Activator.CreateInstance(cdType);
        cdType.GetProperty("Application").SetValue(cd, app);
        var dict = new Dictionary<string, string>
        {
            ["dynPath"] = job.Path,
            ["dynPathExecute"] = "true",
            ["dynShowUI"] = job.ShowUi ? "true" : "false",
            ["dynAutomation"] = job.Automation ? "true" : "false",
            ["dynForceManualRun"] = "false",
            ["dynModelShutdown"] = "false",
        };
        cdType.GetProperty("JournalData").SetValue(cd, dict);
        var cmd = Activator.CreateInstance(type);
        return type.GetMethod("ExecuteCommand").Invoke(cmd, new[] { cd });
    }

    static void RunWhenReady(UIApplication uiapp, Type dynType, Job job)
    {
        string path = job.Path;
        var started = DateTime.Now;
        var lastOpen = DateTime.Now;
        int attempts = 1;
        State = "running";
        EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> h = null;
        h = (s, e) =>
        {
            try
            {
                if (TryRun(dynType, path))
                {
                    uiapp.Idling -= h; if (State != "finished") { State = "started"; LastMessage = "Graph opened and run triggered."; LastFinished = DateTime.Now; } return;
                }
                // first use after Revit start: Dynamo initialises on the first command and only opens the file on the next one
                if (attempts < 4 && (DateTime.Now - lastOpen).TotalSeconds > 6 && dynType.GetProperty("RevitDynamoModel")?.GetValue(null) != null)
                {
                    attempts++; lastOpen = DateTime.Now;
                    OpenGraph(uiapp, dynType, job);
                }
                if ((DateTime.Now - started).TotalSeconds > 90)
                {
                    uiapp.Idling -= h; State = "error"; LastMessage = "Dynamo did not open the graph within 90 s."; LastFinished = DateTime.Now; return;
                }
                e.SetRaiseWithoutDelay();
            }
            catch (Exception ex) { uiapp.Idling -= h; State = "error"; LastMessage = ex.Message; LastFinished = DateTime.Now; }
        };
        uiapp.Idling += h;
    }

    /// <summary>Called from the ribbon command on the Revit thread.</summary>
    public static Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var job = Pending; Pending = null;
        if (job == null)
        {
            // manual click: let the user pick a graph
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Dynamo graphs (*.dyn)|*.dyn", Title = "Run a Dynamo graph" };
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            job = new Job { Path = dlg.FileName, ShowUi = true };
        }
        State = "running"; LastPath = job.Path; LastNodeIssues = new();
        try
        {
            var type = FindDynamoCommand() ?? throw new ToolException("Dynamo for Revit is not installed/loaded (DynamoRevitDS.dll not found).");
            var res = OpenGraph(data.Application, type, job);
            // The graph opens in Manual run mode (and asynchronously on first use): trigger the run as soon as it is open.
            if (TryRun(type, job.Path)) { if (State != "finished") { State = "started"; LastMessage = "Graph opened and run triggered (" + res + ")."; LastFinished = DateTime.Now; } }
            else RunWhenReady(data.Application, type, job);
            message = "";
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            State = "error";
            LastMessage = (ex.InnerException ?? ex).Message;
            LastFinished = DateTime.Now;
            Log.Error("Dynamo run failed: " + ex);
            message = LastMessage;
            return Result.Failed;
        }
    }
}
