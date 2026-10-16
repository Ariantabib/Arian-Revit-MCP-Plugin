using System.Text.Json;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;

namespace Arian.RevitMcp.Server;

/// <summary>Agent-defined tools (reusable C# macros) persisted as JSON in %APPDATA%\Arian\RevitMcp\custom_tools.</summary>
public static class CustomTools
{
    public static string Dir => Path.Combine(PluginSettings.Dir, "custom_tools");

    public sealed class Def
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JsonObject InputSchema { get; set; }
        public string Code { get; set; }
        public string Members { get; set; }
        public bool ReadOnly { get; set; }
    }

    public static IEnumerable<ToolDef> LoadAll()
    {
        if (!Directory.Exists(Dir)) yield break;
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
        {
            ToolDef td = null;
            try
            {
                var d = JsonSerializer.Deserialize<Def>(File.ReadAllText(f), J.Opts);
                td = Make(d);
            }
            catch (Exception ex) { Log.Warn($"Custom tool {Path.GetFileName(f)} not loaded: {ex.Message}"); }
            if (td != null) yield return td;
        }
    }

    public static ToolDef Make(Def d)
    {
        if (string.IsNullOrWhiteSpace(d.Name) || !System.Text.RegularExpressions.Regex.IsMatch(d.Name, "^[a-z][a-z0-9_]{2,50}$"))
            throw new ToolException("Custom tool name must be snake_case (letters, digits, underscore; 3-50 chars).");
        var schema = d.InputSchema != null ? (JsonObject)d.InputSchema.DeepClone() : new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
        if (schema["type"] == null) schema["type"] = "object";
        if (schema["properties"] == null) schema["properties"] = new JsonObject();
        ((JsonObject)schema["properties"])["document"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: title of the document to target (default: active)." };
        ScriptEngine.Compiled compiled = null;
        var def = d;
        var td = new ToolDef
        {
            Name = d.Name,
            Description = (d.Description ?? "").Trim() + "\n(Custom tool defined by the agent; C# code stored in the plugin.)",
            Category = "Custom",
            ReadOnly = d.ReadOnly,
            NoTransaction = true,
            NoDocument = true,
            DocumentOptional = true,
            Params = Array.Empty<ParamDef>(),
            Schema = schema,
        };
        td.CustomHandler = args =>
        {
            compiled ??= ScriptEngine.Compile(def.Code, def.Members);
            var clean = new JsonObject();
            foreach (var kv in args) if (kv.Key != "document") clean[kv.Key] = kv.Value?.DeepClone();
            var o = ScriptEngine.Execute(compiled, clean, !def.ReadOnly, "Arian MCP: " + def.Name);
            return ScriptEngine.ToResult(o);
        };
        return td;
    }

    public static void Save(Def d)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, d.Name + ".json"), JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
    }
}
