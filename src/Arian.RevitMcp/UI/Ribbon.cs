using System.Reflection;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.UI;

public static class Ribbon
{
    public const string TabName = "Arian";
    static PushButton _startStop, _warn, _dialogs;

    static string Asm => Assembly.GetExecutingAssembly().Location;

    static PushButton Add(RibbonPanel panel, string id, string text, Type cmd, System.Windows.Media.Imaging.BitmapSource large, string tip)
    {
        var d = new PushButtonData(id, text, Asm, cmd.FullName)
        {
            LargeImage = large,
            Image = Icons.Make(large == null ? "" : "•", Icons.Gray, 16),
            ToolTip = tip,
        };
        return (PushButton)panel.AddItem(d);
    }

    static readonly List<(PushButton btn, string key)> Static = new();

    static PushButton Add2(RibbonPanel panel, string id, string key, Type cmd, string icon, System.Windows.Media.Color color, string tip)
    {
        var btn = Add(panel, id, Loc.T(key), cmd, Icons.Draw(icon, color), tip);
        Static.Add((btn, key));
        return btn;
    }

    public static void Create(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { /* already exists */ }

        var pServer = app.CreateRibbonPanel(TabName, Loc.T("MCP Server")); 
        _startStop = Add(pServer, "ArianStartStop", Loc.T("Start\nServer"), typeof(Commands.StartStopCommand),
            Icons.Draw("play", Icons.Green), "Start or stop the Arian MCP server so an AI agent can control Revit."); 
        Add2(pServer, "ArianConnect", "Connect\nAgent", typeof(Commands.ConnectCommand), "plug", Icons.Blue, "Show ready-to-copy connection commands for Claude Code, Claude Desktop, Cursor, etc."); 
        Add2(pServer, "ArianSettings", "Settings", typeof(Commands.SettingsCommand), "gear", Icons.Gray, "Port, auto-start, security, warning policy, tool profiles/categories, language.");

        var pMon = app.CreateRibbonPanel(TabName, Loc.T("Monitor")); 
        Add2(pMon, "ArianPanel", "Agent\nPanel", typeof(Commands.ShowPaneCommand), "panel", Icons.Teal, "Open the Arian agent panel: live tool calls, image history, log, undo and cancel."); 
        Add2(pMon, "ArianUndoAgent", "Undo\nAgent", typeof(Commands.UndoAgentCommand), "undo", Icons.Orange, "Undo the last change (the agent's last operation is labelled 'Arian MCP: …' in Revit's undo list)."); 
        Add2(pMon, "ArianCancel", "Cancel\nTask", typeof(Commands.CancelTaskCommand), "cancel", Icons.Red, "Cancel queued requests and ask the running tool to stop at its next checkpoint.");

        var pAuto = app.CreateRibbonPanel(TabName, Loc.T("Automation")); 
        _warn = Add(pAuto, "ArianWarnings", Loc.T("Warnings:\nIgnore"), typeof(Commands.WarningPolicyCommand),
            Icons.Draw("warn", Icons.Orange), "Cycle the warnings/errors policy: Off → Ignore warnings → Ignore + auto-resolve errors."); 
        _dialogs = Add(pAuto, "ArianDialogs", Loc.T("Auto Dialogs:\nOff"), typeof(Commands.DialogsToggleCommand),
            Icons.Draw("bubble", Icons.Purple), "Toggle automatic answering of Revit task dialogs."); 
        Add2(pAuto, "ArianDismiss", "Dismiss\nDialogs", typeof(Commands.DismissNowCommand), "close", Icons.Red, "Click the safest button of any dialogs that are currently open (UI Automation)."); 
        Add2(pAuto, "ArianRunDynamo", "Run Dynamo\nGraph", typeof(Commands.RunDynamoCommand), "dynamo", Icons.Teal, "Run a Dynamo graph (.dyn) in automation mode (also callable by the AI agent)."); 
        Add2(pAuto, "ArianWarningsReport", "Model\nWarnings", typeof(Commands.WarningsReportCommand), "report", Icons.Orange, "Summarise the current model warnings.");

        var pHelp = app.CreateRibbonPanel(TabName, Loc.T("Help")); 
        Add2(pHelp, "ArianLogs", "Open\nLogs", typeof(Commands.OpenLogsCommand), "list", Icons.Teal, "Open the log folder."); 
        Add2(pHelp, "ArianAbout", "About", typeof(Commands.AboutCommand), "info", Icons.Blue, "About Arian Revit MCP Plugin.");

        Refresh();
    }

    /// <summary>Refresh toggle button captions/icons from current state.</summary>
    public static void Refresh()
    {
        try
        {
            var srv = McpHttpServer.Instance;
            if (_startStop != null)
            {
                if (srv.Running)
                {
                    _startStop.ItemText = Loc.T("Stop Server\n:{0}", srv.Port);
                    _startStop.LargeImage = Icons.Draw("stop", Icons.Red);
                }
                else
                {
                    _startStop.ItemText = Loc.T("Start\nServer");
                    _startStop.LargeImage = Icons.Draw("play", Icons.Green);
                }
            }
            if (_warn != null)
            {
                var p = PluginSettings.Current.WarningPolicy;
                _warn.ItemText = p switch
                {
                    WarningPolicy.Off => Loc.T("Warnings:\nDefault"),
                    WarningPolicy.IgnoreWarnings => Loc.T("Warnings:\nIgnore"),
                    _ => Loc.T("Warnings:\nIgnore+Fix"),
                };
                _warn.LargeImage = Icons.Draw("warn", p == WarningPolicy.Off ? Icons.Gray : Icons.Orange);
            }
            if (_dialogs != null)
            {
                bool on = PluginSettings.Current.AutoDismissDialogs;
                _dialogs.ItemText = on ? Loc.T("Auto Dialogs:\nOn") : Loc.T("Auto Dialogs:\nOff");
                _dialogs.LargeImage = Icons.Draw("bubble", on ? Icons.Purple : Icons.Gray);
            }
        }
        catch (Exception ex) { Log.Warn("Ribbon refresh: " + ex.Message); }
    }
}
