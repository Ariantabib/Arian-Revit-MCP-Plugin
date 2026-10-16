using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Knowledge;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

public static class SessionTools
{
    [Tool("get_session_info", """
        Information about the running Revit session: version, user, open documents, the active document/view, project units and plugin state. Call this first.
        """, Category = "Session", ReadOnly = true, NoDocument = true)]
    public static object GetSessionInfo()
    {
        var app = Ctx.App;
        var o = new JsonObject
        {
            ["revit_version"] = app.VersionNumber,
            ["revit_build"] = app.VersionBuild,
            ["revit_name"] = app.VersionName,
            ["user"] = app.Username,
            ["language"] = app.Language.ToString(),
            ["plugin_version"] = McpProtocol.Version,
            ["api_units"] = "lengths mm, angles deg, areas m2, volumes m3",
            ["tool_count"] = ToolRegistry.All.Count,
            ["warning_policy"] = PluginSettings.Current.WarningPolicy.ToString(),
            ["auto_dismiss_dialogs"] = PluginSettings.Current.AutoDismissDialogs,
        };
        var docs = new JsonArray();
        foreach (Document d in app.Documents)
        {
            if (d.IsLinked) continue;
            docs.Add(new JsonObject
            {
                ["title"] = d.Title,
                ["path"] = d.PathName,
                ["kind"] = d.IsFamilyDocument ? "family" : "project",
                ["modified"] = d.IsModified,
                ["workshared"] = d.IsWorkshared,
                ["active"] = Ctx.UiDoc != null && d.Equals(Ctx.UiDoc.Document),
            });
        }
        o["documents"] = docs;
        var ad = Ctx.UiDoc?.Document;
        if (ad != null)
        {
            var v = ad.ActiveView;
            o["active_document"] = ad.Title;
            if (v != null) o["active_view"] = new JsonObject { ["id"] = v.Id.Value, ["name"] = v.Name, ["type"] = v.ViewType.ToString(), ["level"] = (v as ViewPlan)?.GenLevel?.Name };
            if (!ad.IsFamilyDocument)
            {
                var pi = ad.ProjectInformation;
                o["project"] = new JsonObject { ["name"] = pi?.Name, ["number"] = pi?.Number, ["status"] = pi?.Status };
            }
            else o["family_category"] = ad.OwnerFamily?.FamilyCategory?.Name;
        }
        else o["hint"] = "No document is open. Use create_project or open_document.";
        return o;
    }

    [Tool("get_guidance", """
        Returns expert BIM guidance and tool recipes (markdown). Topics: index, workflow, architecture, structure, mep, family, materials, views, lod, scripting, warnings, troubleshooting. Use topic='index' to list all topics.
        @topic Topic key; 'index' lists topics.
        """, Category = "Session", ReadOnly = true, NoDocument = true, AnyThread = true)]
    public static object GetGuidance(string topic = "index")
    {
        var t = Guide.Read(topic);
        if (t == null) return new JsonObject { ["error"] = $"Unknown topic '{topic}'.", ["topics"] = Guide.Read("index") };
        return t;
    }

    [Tool("get_server_status", "Plugin/server health: uptime, request count, current busy tool, queue depth, settings.",
        Category = "Session", ReadOnly = true, NoDocument = true, AnyThread = true)]
    public static object GetServerStatus()
    {
        var s = McpHttpServer.Instance;
        return new JsonObject
        {
            ["running"] = s.Running,
            ["url"] = s.Url,
            ["started_at"] = s.StartedAt?.ToString("s"),
            ["requests"] = s.Requests,
            ["completed_tool_calls"] = RevitDispatcher.CompletedCalls,
            ["busy_with"] = RevitDispatcher.CurrentTool,
            ["busy_since"] = RevitDispatcher.BusySince?.ToString("s"),
            ["pending"] = RevitDispatcher.Pending,
            ["warnings_ignored"] = Automation.WarningsIgnored,
            ["errors_auto_resolved"] = Automation.ErrorsResolved,
            ["dialogs_dismissed"] = Automation.DialogsDismissed,
            ["settings"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(PluginSettings.Current)),
        };
    }

    [Tool("get_server_log", "Last lines of the plugin log (useful to debug failed tool calls).",
        Category = "Session", ReadOnly = true, NoDocument = true, AnyThread = true)]
    public static object GetServerLog(int lines = 80) => new JsonObject { ["log"] = new JsonArray(Log.Tail(lines).Select(l => (JsonNode)l).ToArray()) };

    [Tool("get_dialog_log", "Recent Revit dialogs / message boxes that appeared (and how they were answered). Use when a tool call times out.",
        Category = "Manage", ReadOnly = true, NoDocument = true, AnyThread = true)]
    public static object GetDialogLog()
    {
        var arr = new JsonArray();
        foreach (var d in Automation.DialogLog())
            arr.Add(new JsonObject { ["time"] = d.Time.ToString("T"), ["id"] = d.Id, ["kind"] = d.Kind, ["message"] = d.Message, ["answer"] = d.Answer });
        return new JsonObject { ["dialogs"] = arr };
    }

    [Tool("dismiss_dialogs", """
        Clicks a button on any Revit dialog that is currently open, via UI Automation (works even when Revit is blocked by a modal dialog). Safe default button order: OK, Close, Continue, Ignore, Yes, Cancel, No.
        @preferred_buttons Button captions to prefer, in order (e.g. ["Ignore","OK"]).
        @list_only Only list open dialogs and their buttons without clicking.
        """, Category = "Manage", NoDocument = true, AnyThread = true)]
    public static object DismissDialogs(string[] preferredButtons = null, bool listOnly = false)
        => Automation.DismissDialogs(preferredButtons, listOnly);

    [Tool("set_warning_policy", """
        Configure how Revit warnings and errors are handled automatically (applies to the agent's tools AND to the user's own work).
        @policy 'off' (Revit default), 'ignore_warnings' (delete warnings as they occur), 'ignore_and_resolve_errors' (also apply Revit's default resolution to errors).
        @auto_dismiss_dialogs Also auto-answer task dialogs/message boxes with the least destructive option.
        """, Category = "Manage", NoDocument = true, NoTransaction = true)]
    public static object SetWarningPolicy(WarningPolicy policy, bool? autoDismissDialogs = null)
    {
        PluginSettings.Current.WarningPolicy = policy;
        if (autoDismissDialogs.HasValue) PluginSettings.Current.AutoDismissDialogs = autoDismissDialogs.Value;
        PluginSettings.Save();
        UI.Ribbon.Refresh();
        return new JsonObject { ["warning_policy"] = policy.ToString(), ["auto_dismiss_dialogs"] = PluginSettings.Current.AutoDismissDialogs };
    }

    [Tool("list_documents", "Lists all open project and family documents.", Category = "Session", ReadOnly = true, NoDocument = true)]
    public static object ListDocuments()
    {
        var arr = new JsonArray();
        foreach (Document d in Ctx.App.Documents)
        {
            if (d.IsLinked) continue;
            arr.Add(new JsonObject
            {
                ["title"] = d.Title,
                ["path"] = d.PathName,
                ["kind"] = d.IsFamilyDocument ? "family" : "project",
                ["modified"] = d.IsModified,
                ["family_category"] = d.IsFamilyDocument ? d.OwnerFamily?.FamilyCategory?.Name : null,
            });
        }
        return new JsonObject { ["documents"] = arr };
    }
}
