using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;

namespace Arian.RevitMcp.Server;

/// <summary>Tools that live in the main (non hot-reloadable) assembly.</summary>
public static class CoreTools
{
    [Tool("reload_tools", """
        Developer/maintenance: reloads the tool implementations (Arian.RevitMcp.Tools.dll) from disk WITHOUT restarting Revit and re-registers all tools. Use after a new build of the tools assembly.
        @path Optional path to a Tools dll (default: the plugin folder).
        """, Category = "Session", NoDocument = true, NoTransaction = true)]
    public static object ReloadTools(string path = null)
    {
        var v = ToolLoader.Load(path);
        return new JsonObject { ["reloaded"] = true, ["tools"] = ToolRegistry.All.Count, ["assembly_version"] = v, ["from"] = ToolLoader.LoadedFrom };
    }

    [Tool("define_custom_tool", """
        Teach the plugin a NEW reusable tool: you supply a name, description, JSON-schema for its arguments and C# code. The tool is compiled, registered immediately (it appears in tools/list as '[Custom] name'), persisted on disk and available in every future session. Use it for repeated workflows (e.g. 'create_apartment_unit', 'place_fire_extinguishers', 'number_rooms_by_level').
        The code is the same kind of body as execute_csharp: object Run(Document doc, UIApplication uiapp, UIDocument uidoc, JsonObject args, Action<object> Print). Arguments arrive in `args` (e.g. args["width_mm"].GetValue<double>()). Lengths are feet internally: use U.Mm(). The code runs in one transaction unless read_only=true.
        @name snake_case tool name. @description What it does and when to use it.
        @input_schema JSON Schema object: {"type":"object","properties":{"width_mm":{"type":"number","description":"..."}},"required":["width_mm"]}
        @code C# body. @members Optional helper members. @read_only True if the tool never modifies the model.
        """, Category = "Advanced", NoDocument = true, NoTransaction = true)]
    public static object DefineCustomTool(string name, string description, string code, JsonObject inputSchema = null, string members = null, bool readOnly = false)
    {
        var d = new CustomTools.Def { Name = name, Description = description, Code = code, InputSchema = inputSchema, Members = members, ReadOnly = readOnly };
        var td = CustomTools.Make(d);
        if (ToolRegistry.TryGet(name, out var existing) && !existing.IsCustom)
            throw new ToolException($"'{name}' is a built-in tool name; choose another name.");
        ScriptEngine.Compile(code, members); // validate now so errors are reported to the agent immediately
        CustomTools.Save(d);
        ToolRegistry.Add(td);
        return new JsonObject { ["defined"] = name, ["saved_to"] = CustomTools.Dir, ["note"] = "The tool is available now (re-list tools if your client caches them)." };
    }

    [Tool("list_custom_tools", "Lists agent-defined custom tools with their code.", Category = "Advanced", NoDocument = true, NoTransaction = true, ReadOnly = true)]
    public static object ListCustomTools()
    {
        var arr = new JsonArray();
        if (Directory.Exists(CustomTools.Dir))
            foreach (var f in Directory.GetFiles(CustomTools.Dir, "*.json"))
                try { arr.Add(JsonNode.Parse(File.ReadAllText(f))); } catch { }
        return new JsonObject { ["custom_tools"] = arr };
    }

    [Tool("delete_custom_tool", "Deletes an agent-defined custom tool.", Category = "Advanced", NoDocument = true, NoTransaction = true, Destructive = true)]
    public static object DeleteCustomTool(string name)
    {
        var f = Path.Combine(CustomTools.Dir, name + ".json");
        if (!File.Exists(f)) throw new ToolException("No custom tool named " + name);
        File.Delete(f);
        ToolRegistry.Remove(name);
        return new JsonObject { ["deleted"] = name };
    }
}
