using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Arian.RevitMcp.Core;

public sealed class DialogRecord
{
    public DateTime Time;
    public string Id, Kind, Message, Answer;
}

/// <summary>
/// Hands-free handling of Revit warnings/errors (FailuresProcessing) and task dialogs (DialogBoxShowing),
/// plus a UI-Automation based fallback that can click buttons of dialogs that are already open.
/// </summary>
public static class Automation
{
    static readonly List<DialogRecord> Dialogs = new();
    static readonly object Gate = new();
    public static long WarningsIgnored, ErrorsResolved, DialogsDismissed;
    static System.Threading.Timer _watch; static volatile bool _busyWatch;

    public static void Hook(UIControlledApplication app)
    {
        // watchdog: while a tool has been running a few seconds and a dialog blocks Revit (e.g. 'Error - cannot be ignored'), answer it so the agent never hangs
        _watch ??= new System.Threading.Timer(_ =>
        {
            try
            {
                if (_busyWatch || !PluginSettings.Current.AutoDismissDialogs) return;
                var since = Server.RevitDispatcher.BusySince;
                if (Server.RevitDispatcher.CurrentTool == null || since == null || (DateTime.Now - since.Value).TotalSeconds < 4) return;
                _busyWatch = true;
                try { var r = DismissDialogs(new[] { "OK", "Continue", "Ignore", "Close", "Cancel", "No" }, false); if ((int)r["dialogs_found"] > 0) Log.Info("Watchdog dismissed dialog(s): " + r.ToJsonString()); }
                finally { _busyWatch = false; }
            }
            catch { _busyWatch = false; }
        }, null, 3000, 2000);
        app.ControlledApplication.FailuresProcessing += OnFailures;
        app.DialogBoxShowing += OnDialog;
    }

    public static void Unhook(UIControlledApplication app)
    {
        try { app.ControlledApplication.FailuresProcessing -= OnFailures; } catch { }
        try { app.DialogBoxShowing -= OnDialog; } catch { }
    }

    // ---------------- failures ----------------
    static void OnFailures(object sender, FailuresProcessingEventArgs e)
    {
        var policy = PluginSettings.Current.WarningPolicy;
        if (policy == WarningPolicy.Off) return;
        try
        {
            var fa = e.GetFailuresAccessor();
            bool resolved = false, unresolved = false, deleted = false;
            foreach (var m in fa.GetFailureMessages())
            {
                if (m.GetSeverity() == Autodesk.Revit.DB.FailureSeverity.Warning)
                {
                    fa.DeleteWarning(m); deleted = true; WarningsIgnored++;
                }
                else if (policy == WarningPolicy.IgnoreAndResolveErrors && m.HasResolutions())
                {
                    try { fa.ResolveFailure(m); resolved = true; ErrorsResolved++; } catch { unresolved = true; }
                }
                else unresolved = true;
                try { Log.Info($"Failure [{m.GetSeverity()}] {m.GetDescriptionText()}"); } catch { }
            }
            if (resolved && !unresolved) e.SetProcessingResult(Autodesk.Revit.DB.FailureProcessingResult.ProceedWithCommit);
            else if (deleted && !unresolved) e.SetProcessingResult(Autodesk.Revit.DB.FailureProcessingResult.Continue);
            else if (unresolved && PluginSettings.Current.AutoDismissDialogs) e.SetProcessingResult(Autodesk.Revit.DB.FailureProcessingResult.ProceedWithRollBack); // never leave an unignorable error dialog blocking the agent
        }
        catch (Exception ex) { Log.Warn("FailuresProcessing handler: " + ex.Message); }
    }

    // ---------------- dialogs ----------------
    static void OnDialog(object sender, DialogBoxShowingEventArgs e)
    {
        try
        {
            string kind = e.GetType().Name, msg = "", id = e.DialogId ?? "";
            if (e is TaskDialogShowingEventArgs t) { msg = t.Message; kind = "TaskDialog"; }
            else if (e is MessageBoxShowingEventArgs m) { msg = m.Message; kind = "MessageBox"; }

            string answer = null;
            if (PluginSettings.Current.AutoDismissDialogs)
            {
                int code = 1; // OK
                string name = "Ok";
                if (PluginSettings.Current.DialogRules.TryGetValue(id, out var rule) && Enum.TryParse<TaskDialogResult>(rule, true, out var tr))
                { code = (int)tr; name = tr.ToString(); }
                else if (msg != null && (msg.Contains("save", StringComparison.OrdinalIgnoreCase) && msg.Contains("changes", StringComparison.OrdinalIgnoreCase)))
                { code = 7; name = "No"; } // never silently save/discard unexpected prompts: conservative "No"
                if (e is TaskDialogShowingEventArgs td) td.OverrideResult(code);
                else if (e is MessageBoxShowingEventArgs mb) mb.OverrideResult(code);
                answer = name; DialogsDismissed++;
            }
            lock (Gate)
            {
                Dialogs.Add(new DialogRecord { Time = DateTime.Now, Id = id, Kind = kind, Message = msg, Answer = answer });
                while (Dialogs.Count > 60) Dialogs.RemoveAt(0);
            }
            Log.Info($"Dialog [{kind}] {id}: {msg} -> {answer ?? "(left open)"}");
        }
        catch (Exception ex) { Log.Warn("DialogBoxShowing handler: " + ex.Message); }
    }

    public static List<DialogRecord> DialogLog()
    {
        lock (Gate) return Dialogs.ToList();
    }

    // ---------------- UI Automation fallback ----------------
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

    /// <summary>Clicks an element: InvokePattern when available, else a window message straight to its HWND (never the physical mouse).</summary>
    static bool Click(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var ip)) { ((InvokePattern)ip).Invoke(); return true; }
        }
        catch { }
        try
        {
            int h = el.Current.NativeWindowHandle;
            if (h == 0) return false;
            var lp = (IntPtr)(5 + (5 << 16));
            PostMessage((IntPtr)h, 0x0201, (IntPtr)1, lp);
            Thread.Sleep(60);
            PostMessage((IntPtr)h, 0x0202, IntPtr.Zero, lp);
            return true;
        }
        catch { return false; }
    }

    static bool IsButtonLike(AutomationElement e)
    {
        var ct = e.Current.ControlType;
        var cls = e.Current.ClassName ?? "";
        if (string.IsNullOrWhiteSpace(e.Current.Name)) return false;
        return ct == ControlType.Button || cls == "Button" || cls.StartsWith("CCPushButton", StringComparison.Ordinal) || cls.StartsWith("CCCommandLink", StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds open dialogs of the Revit process (top-level windows and #32770 dialogs embedded in the Revit main window tree)
    /// and clicks the best matching button. If listOnly, only reports what it finds.
    /// </summary>
    public static JsonObject DismissDialogs(string[] preferred, bool listOnly)
    {
        var pid = Process.GetCurrentProcess().Id;
        preferred = preferred is { Length: > 0 } ? preferred : new[] { "OK", "Close", "Continue", "Ignore", "Yes", "Cancel", "No" };
        var report = new JsonArray();
        var root = AutomationElement.RootElement;
        var tops = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
        string mainTitle = null;
        try { mainTitle = Process.GetCurrentProcess().MainWindowTitle; } catch { }

        var candidates = new List<AutomationElement>();
        foreach (AutomationElement top in tops)
        {
            string name = top.Current.Name ?? "";
            bool isMain = (!string.IsNullOrEmpty(mainTitle) && name == mainTitle) || name.StartsWith("Autodesk Revit", StringComparison.OrdinalIgnoreCase);
            if (top.Current.ControlType != ControlType.Window) continue;
            if (!isMain) candidates.Add(top);
            else
            {
                // dialogs are child windows of the main frame
                foreach (AutomationElement w in top.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)))
                    candidates.Add(w);
            }
        }

        foreach (var w in candidates)
        {
            string name = w.Current.Name ?? "", cls = w.Current.ClassName ?? "";
            if (cls.StartsWith("WindowsForms10", StringComparison.Ordinal) && name.EndsWith("Monitor", StringComparison.Ordinal)) continue; // Revit's hidden helper windows
            var buttons = new List<AutomationElement>();
            foreach (AutomationElement b in w.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                if (IsButtonLike(b)) buttons.Add(b);
            if (buttons.Count == 0) continue;

            var texts = new List<string>();
            try
            {
                foreach (AutomationElement t in w.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
                { var n = t.Current.Name; if (!string.IsNullOrWhiteSpace(n)) texts.Add(n); if (texts.Count > 8) break; }
            }
            catch { }

            // read everything BEFORE clicking: the elements vanish once the dialog closes
            var named = buttons.Select(b => { bool en = true; try { en = b.Current.IsEnabled; } catch { } return (el: b, name: (b.Current.Name ?? "").Replace("&", "").Trim(), en); }).Where(x => x.en).ToList();   // disabled buttons (e.g. OK of an unignorable error) are skipped, so Cancel gets chosen
            var buttonNames = new JsonArray(named.Select(x => (JsonNode)x.name).Distinct().Select(n => (JsonNode)n.ToString()).ToArray());

            string clicked = null;
            if (!listOnly)
            {
                foreach (var want in preferred)
                {
                    foreach (var (el, bn, _) in named)
                    {
                        if (!bn.Equals(want, StringComparison.OrdinalIgnoreCase) && !bn.StartsWith(want, StringComparison.OrdinalIgnoreCase)) continue;
                        if (Click(el)) { clicked = bn; break; }
                    }
                    if (clicked != null) break;
                }
                if (clicked != null) DialogsDismissed++;
            }
            report.Add(new JsonObject
            {
                ["window"] = name,
                ["class"] = cls,
                ["text"] = string.Join(" | ", texts),
                ["buttons"] = buttonNames,
                ["clicked"] = clicked,
            });
        }
        return new JsonObject { ["dialogs_found"] = report.Count, ["dialogs"] = report };
    }
}
