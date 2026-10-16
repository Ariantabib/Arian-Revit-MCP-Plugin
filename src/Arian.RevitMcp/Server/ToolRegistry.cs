using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;

namespace Arian.RevitMcp.Server;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public ToolAttribute(string name, string description) { Name = name; Description = description; }

    /// <summary>Category used for filtering (Session, Query, Modify, Architecture, Structure, MEP, Family, Material, View, Docs, Manage, Advanced).</summary>
    public string Category { get; set; } = "General";
    /// <summary>Does not modify the model: no transaction is opened.</summary>
    public bool ReadOnly { get; set; }
    /// <summary>The tool manages its own transactions (or needs none); the runner will not open one.</summary>
    public bool NoTransaction { get; set; }
    /// <summary>Can run on the HTTP thread (does not touch the Revit API).</summary>
    public bool AnyThread { get; set; }
    public bool Destructive { get; set; }
    /// <summary>Tool works without any open document.</summary>
    public bool NoDocument { get; set; }
    /// <summary>Tool accepts the 'document' argument but also works when no document is open.</summary>
    public bool DocumentOptional { get; set; }
}

/// <summary>Description for a field of a structured argument type.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DescAttribute : Attribute
{
    public string Text { get; }
    public DescAttribute(string t) { Text = t; }
}

public sealed class ParamDef
{
    public string Name;           // snake_case JSON name
    public Type Type;
    public bool Required;
    public object Default;
    public string Description;
}

public sealed class ToolDef
{
    public string Name;
    public string Description;
    public string Category;
    public bool ReadOnly, NoTransaction, AnyThread, Destructive, NoDocument, DocumentOptional;
    public MethodInfo Method;
    public ParamDef[] Params;
    public JsonObject Schema;
    /// <summary>For agent-defined tools: runs the stored C# directly (no reflection method).</summary>
    public Func<JsonObject, object> CustomHandler;
    public bool IsCustom => CustomHandler != null;
}

public static class ToolRegistry
{
    static readonly Dictionary<string, ToolDef> Tools = new(StringComparer.Ordinal);
    public static IReadOnlyCollection<ToolDef> All => Tools.Values;
    public static bool TryGet(string name, out ToolDef d) => Tools.TryGetValue(name, out d);

    public static void Build()
    {
        Tools.Clear();
        var assemblies = new List<Assembly> { typeof(ToolRegistry).Assembly };
        if (ToolLoader.Current != null) assemblies.Add(ToolLoader.Current);
        foreach (var asm in assemblies) RegisterAssembly(asm);
        foreach (var d in CustomTools.LoadAll()) { if (!Tools.ContainsKey(d.Name)) Tools[d.Name] = d; }
        Log.Info($"Registered {Tools.Count} tools.");
    }

    public static void Add(ToolDef d) { Tools[d.Name] = d; }
    public static bool Remove(string name) => Tools.Remove(name);

    static void RegisterAssembly(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); Log.Warn("Some types failed to load from " + asm.GetName().Name); }
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var a = m.GetCustomAttribute<ToolAttribute>();
                if (a == null) continue;
                try
                {
                    var d = Make(a, m);
                    if (Tools.ContainsKey(d.Name)) { Log.Error("Duplicate tool name: " + d.Name); continue; }
                    Tools[d.Name] = d;
                }
                catch (Exception ex) { Log.Error($"Tool {a.Name} failed to register: {ex.Message}"); }
            }
        }
    }

    public static ToolDef Make(ToolAttribute a, MethodInfo m)
    {
        // split description into main text + "@param text" lines
        var main = new List<string>();
        var pdesc = new Dictionary<string, string>();
        foreach (var raw in a.Description.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("@") && line.TrimStart().Length > 1 && char.IsLetter(line.TrimStart()[1]))
            {
                var s = line.TrimStart().Substring(1);
                int sp = s.IndexOfAny(new[] { ' ', ':' });
                if (sp > 0) pdesc[s.Substring(0, sp)] = s.Substring(sp + 1).TrimStart(' ', ':');
                else pdesc[s] = "";
            }
            else main.Add(line.Trim());
        }

        var ps = m.GetParameters();
        var defs = new List<ParamDef>();
        var props = new JsonObject();
        var req = new JsonArray();
        foreach (var p in ps)
        {
            var jn = JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name);
            var pd = new ParamDef
            {
                Name = jn,
                Type = p.ParameterType,
                Required = !p.HasDefaultValue,
                Default = p.HasDefaultValue ? p.DefaultValue : null,
            };
            pdesc.TryGetValue(p.Name, out pd.Description);
            if (pd.Description == null) pdesc.TryGetValue(jn, out pd.Description);
            defs.Add(pd);
            var sch = Schema(p.ParameterType, 0);
            if (!string.IsNullOrEmpty(pd.Description)) sch["description"] = pd.Description;
            if (p.HasDefaultValue && p.DefaultValue != null && sch["type"] != null)
            {
                try { sch["default"] = JsonSerializer.SerializeToNode(p.DefaultValue, J.Opts); } catch { }
            }
            props[jn] = sch;
            if (pd.Required) req.Add(jn);
        }
        if (!a.NoDocument || a.DocumentOptional)
        {
            props["document"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional: title or path of the open document to target. Defaults to the active document."
            };
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (req.Count > 0) schema["required"] = req;

        return new ToolDef
        {
            Name = a.Name,
            Description = string.Join("\n", main).Trim(),
            Category = a.Category,
            ReadOnly = a.ReadOnly,
            NoTransaction = a.NoTransaction,
            AnyThread = a.AnyThread,
            Destructive = a.Destructive,
            NoDocument = a.NoDocument,
            DocumentOptional = a.DocumentOptional,
            Method = m,
            Params = defs.ToArray(),
            Schema = schema,
        };
    }

    // ---------- JSON schema generation ----------
    public static JsonObject Schema(Type t, int depth)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (t == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return new JsonObject { ["type"] = "integer" };
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return new JsonObject { ["type"] = "number" };
        if (t == typeof(Autodesk.Revit.DB.ElementId)) return new JsonObject { ["type"] = "integer", ["description"] = "Revit element id" };
        if (t == typeof(Autodesk.Revit.DB.XYZ))
            return new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" }, ["description"] = "[x,y,z] in millimetres" };
        if (t.IsEnum)
        {
            var arr = new JsonArray();
            foreach (var n in Enum.GetNames(t)) arr.Add(JsonNamingPolicy.SnakeCaseLower.ConvertName(n));
            return new JsonObject { ["type"] = "string", ["enum"] = arr };
        }
        if (t == typeof(JsonObject)) return new JsonObject { ["type"] = "object" };
        if (t == typeof(JsonArray)) return new JsonObject { ["type"] = "array" };
        if (t == typeof(JsonNode) || t == typeof(object) || t == typeof(JsonElement)) return new JsonObject();
        if (t.IsArray) return new JsonObject { ["type"] = "array", ["items"] = Schema(t.GetElementType(), depth + 1) };
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            return new JsonObject { ["type"] = "array", ["items"] = Schema(t.GetGenericArguments()[0], depth + 1) };
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = Schema(t.GetGenericArguments()[1], depth + 1) };
        if (depth > 4) return new JsonObject { ["type"] = "object" };

        // structured argument class: public fields/properties
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var jn = JsonNamingPolicy.SnakeCaseLower.ConvertName(f.Name);
            var s = Schema(f.FieldType, depth + 1);
            var d = f.GetCustomAttribute<DescAttribute>();
            if (d != null) s["description"] = d.Text;
            props[jn] = s;
        }
        foreach (var f in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.CanWrite) continue;
            var jn = JsonNamingPolicy.SnakeCaseLower.ConvertName(f.Name);
            var s = Schema(f.PropertyType, depth + 1);
            var d = f.GetCustomAttribute<DescAttribute>();
            if (d != null) s["description"] = d.Text;
            props[jn] = s;
        }
        return new JsonObject { ["type"] = "object", ["properties"] = props };
    }
}
