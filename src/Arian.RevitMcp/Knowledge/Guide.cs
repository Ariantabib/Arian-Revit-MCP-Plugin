using Arian.RevitMcp.Server;

namespace Arian.RevitMcp.Knowledge;

public sealed class PromptArg
{
    public string Name, Description;
    public bool Required;
}

public sealed class PromptDef
{
    public string Name, Description, Template;
    public PromptArg[] Args = Array.Empty<PromptArg>();
    public string Render(Dictionary<string, string> a)
    {
        var s = Template;
        foreach (var arg in Args)
            s = s.Replace("{" + arg.Name + "}", a.TryGetValue(arg.Name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "(not specified — decide sensibly or ask the user)");
        return s;
    }
}

public static class Guide
{
    public static string ServerInstructions() => $@"You are connected to **Arian Revit MCP Plugin**: a live bridge into Autodesk Revit 2027 ({ToolRegistry.All.Count} tools) covering Architecture, Structure, MEP (mechanical, electrical, plumbing), Families, Materials, Views/Sheets/Schedules, model health and a C# scripting escape hatch.
Act like a senior BIM coordinator/modeler who works at any LOD (100–500).

CONVENTIONS
- ALL lengths are millimetres, angles degrees, areas m², volumes m³. Points are [x,y,z] in mm in project (internal-origin) coordinates; z may be omitted for level-based items. Never use feet.
- Element ids are integers. Types are referenced by name (""Family: Type"" or just ""Type"") or by id; levels/views/materials by name or id.
- Every write tool runs in its own Revit transaction (undoable in Revit). Warnings produced are returned in `revit_warnings`; the plugin can auto-ignore them.

WORKFLOW (follow it)
1. Call `get_session_info` and `get_model_overview` first; call `get_guidance` with topic ""index"" (or read resources revit://guide/*) for recipes per discipline.
2. Discover before you build: `list_levels`, `list_element_types` (wall/floor/door/... types), `list_families`, `list_materials`, `list_grids`. Prefer existing project types; create missing ones with `create_compound_type`, `create_material`, `family_*` tools.
3. Build in the right order: levels -> grids -> structure (columns/beams/slabs/foundations) -> walls -> floors/roofs/ceilings -> openings/doors/windows -> rooms -> stairs/railings -> furniture/equipment -> MEP -> annotations/tags/dimensions -> views/sheets/schedules.
4. VERIFY visually and numerically after every significant step: `get_view_image` (returns a PNG you can see), `query_elements`, `get_element`, `get_warnings`. Fix problems before continuing.
5. Large builds: use the bulk tools (create_walls, place_family_instances, batch) instead of hundreds of single calls.
6. If no tool exists for something, use `execute_csharp` (full Revit API, runs inside Revit) — after checking with `get_guidance` topic ""scripting"".
7. If Revit is blocked by a dialog (tool times out), use `get_dialog_log` and `dismiss_dialogs`. Prefer `set_warning_policy` to silence recurring warnings/errors instead of clicking them.

Ask the user when requirements (program, area, code, structural system, LOD) are genuinely ambiguous; otherwise proceed with explicit, stated assumptions.";

    public static IEnumerable<(string uri, string title, string desc)> ListResources()
    {
        yield return ("revit://guide/index", "Arian Revit MCP — guide index", "Start here: what guides exist and how to work");
        foreach (var t in Topics.All)
            yield return ($"revit://guide/{t.Key}", t.Value.Title, t.Value.Description);
    }

    public static string ReadResource(string uri)
    {
        if (uri == null || !uri.StartsWith("revit://guide/")) return null;
        var key = uri.Substring("revit://guide/".Length);
        return Read(key);
    }

    public static string Read(string key)
    {
        key = (key ?? "index").Trim().ToLowerInvariant();
        if (key == "index" || key == "")
        {
            var sb = new System.Text.StringBuilder("# Arian Revit MCP guide topics\n\nCall `get_guidance` with one of these topics (or read the resource revit://guide/<topic>):\n\n");
            foreach (var t in Topics.All) sb.Append($"- **{t.Key}** — {t.Value.Description}\n");
            return sb.ToString();
        }
        return Topics.All.TryGetValue(key, out var v) ? v.Text : null;
    }

    public static IEnumerable<PromptDef> Prompts() => PromptCatalog.All;
}
