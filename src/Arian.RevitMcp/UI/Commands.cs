using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.UI.Commands;

[Transaction(TransactionMode.Manual)]
public class StartStopCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        var srv = McpHttpServer.Instance;
        if (srv.Running) srv.Stop();
        else if (!srv.Start(out var err))
        {
            TaskDialog.Show("Arian MCP", $"Could not start the server on port {PluginSettings.Current.Port}:\n\n{err}\n\nChange the port in Settings and try again.");
        }
        Ribbon.Refresh();
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class ShowPaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        try { d.Application.GetDockablePane(AgentPane.PaneId).Show(); } catch (Exception ex) { TaskDialog.Show("Arian MCP", "Could not open the panel: " + ex.Message); }
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class UndoAgentCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        try { d.Application.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); } catch (Exception ex) { message = ex.Message; return Result.Failed; }
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class CancelTaskCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        int n = Activity.Cancel();
        TaskDialog.Show("Arian MCP", $"Cancel requested. {n} queued request(s) dropped; the running tool (if any) stops at its next checkpoint.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class ConnectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements) { ConnectWindow.Show(); return Result.Succeeded; }
}

[Transaction(TransactionMode.Manual)]
public class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements) { SettingsWindow.Show(); return Result.Succeeded; }
}

[Transaction(TransactionMode.Manual)]
public class WarningPolicyCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        var s = PluginSettings.Current;
        s.WarningPolicy = (WarningPolicy)(((int)s.WarningPolicy + 1) % 3);
        PluginSettings.Save();
        Ribbon.Refresh();
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class DialogsToggleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        PluginSettings.Current.AutoDismissDialogs = !PluginSettings.Current.AutoDismissDialogs;
        PluginSettings.Save();
        Ribbon.Refresh();
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class DismissNowCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        var r = Automation.DismissDialogs(null, false);
        TaskDialog.Show("Arian MCP", "Open dialogs processed: " + r["dialogs_found"]);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class WarningsReportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        var doc = d.Application.ActiveUIDocument?.Document;
        if (doc == null) { TaskDialog.Show("Arian MCP", "No document is open."); return Result.Succeeded; }
        var warns = doc.GetWarnings();
        var groups = warns.GroupBy(w => w.GetDescriptionText()).OrderByDescending(g => g.Count()).Take(12);
        var text = $"{warns.Count} warnings in '{doc.Title}'.\n\n" + string.Join("\n", groups.Select(g => $"{g.Count()} × {g.Key}"));
        text += $"\n\nIgnored so far this session: {Automation.WarningsIgnored} warnings, {Automation.ErrorsResolved} errors auto-resolved.";
        TaskDialog.Show("Arian MCP — Warnings", text);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class OpenLogsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        try { System.IO.Directory.CreateDirectory(Log.LogDir); System.Diagnostics.Process.Start("explorer.exe", Log.LogDir); } catch { }
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class AboutCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements)
    {
        TaskDialog.Show("Arian Revit MCP Plugin",
            $"Arian Revit MCP Plugin v{McpProtocol.Version}\n\nA Model Context Protocol server that lets AI agents model and manage Revit " +
            $"(architecture, structure, MEP, families, materials, views, warnings) — {ToolRegistry.All.Count} tools.\n\nSettings & logs: {PluginSettings.Dir}");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class RunDynamoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData d, ref string message, ElementSet elements) => DynamoRunner.Execute(d, ref message, elements);
}
