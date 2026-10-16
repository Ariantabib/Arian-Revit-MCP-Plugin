using System.Text.Json;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Knowledge;

namespace Arian.RevitMcp.Server;

/// <summary>JSON-RPC 2.0 / Model Context Protocol message handling (transport independent).</summary>
public static class McpProtocol
{
    public const string ServerName = "arian-revit-mcp";
    public static readonly string Version = (typeof(McpProtocol).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion?.Split((char)43)[0] ?? "1.1.0";
    static readonly string[] Supported = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
    const int MaxText = 300_000;

    /// <summary>Handles one JSON-RPC message; returns null for notifications.</summary>
    public static async Task<JsonNode> HandleAsync(JsonNode msg, HashSet<string> categories)
    {
        if (msg is not JsonObject req) return Error(null, -32600, "Invalid Request");
        var id = req["id"]?.DeepClone();
        string method = req["method"]?.GetValue<string>();
        var prm = req["params"] as JsonObject;

        if (method == null) return null; // a response from the client; ignore
        bool isNotification = !req.ContainsKey("id");

        try
        {
            JsonNode result = method switch
            {
                "initialize" => Initialize(prm),
                "ping" => new JsonObject(),
                "tools/list" => ToolsList(categories),
                "tools/call" => await ToolsCall(prm),
                "resources/list" => ResourcesList(),
                "resources/read" => ResourcesRead(prm),
                "resources/templates/list" => new JsonObject { ["resourceTemplates"] = new JsonArray() },
                "prompts/list" => PromptsList(),
                "prompts/get" => PromptsGet(prm),
                "logging/setLevel" => new JsonObject(),
                "completion/complete" => new JsonObject { ["completion"] = new JsonObject { ["values"] = new JsonArray() } },
                _ when method.StartsWith("notifications/") => null,
                _ => throw new RpcException(-32601, "Method not found: " + method),
            };
            if (isNotification) return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result ?? new JsonObject() };
        }
        catch (RpcException rx)
        {
            return isNotification ? null : Error(id, rx.Code, rx.Message);
        }
        catch (Exception ex)
        {
            Log.Error("RPC error: " + ex);
            return isNotification ? null : Error(id, -32603, ex.Message);
        }
    }

    static JsonObject Error(JsonNode id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    };

    sealed class RpcException : Exception
    {
        public int Code;
        public RpcException(int c, string m) : base(m) { Code = c; }
    }

    // ---------------- initialize ----------------
    static JsonNode Initialize(JsonObject p)
    {
        string wanted = p?["protocolVersion"]?.GetValue<string>();
        string ver = Supported.Contains(wanted) ? wanted : Supported[0];
        return new JsonObject
        {
            ["protocolVersion"] = ver,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["resources"] = new JsonObject { ["listChanged"] = false, ["subscribe"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false },
                ["logging"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["title"] = "Arian Revit MCP Plugin",
                ["version"] = Version,
            },
            ["instructions"] = Guide.ServerInstructions(),
        };
    }

    // ---------------- tools ----------------
    static JsonNode ToolsList(HashSet<string> categories)
    {
        var arr = new JsonArray();
        var enabled = PluginSettings.Current.EnabledCategories;
        HashSet<string> global = string.IsNullOrWhiteSpace(enabled) ? null
            : new HashSet<string>(enabled.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        foreach (var t in ToolRegistry.All.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            if (categories != null && categories.Count > 0 && !categories.Contains(t.Category) && t.Category != "Session") continue;
            if (global != null && !global.Contains(t.Category) && t.Category != "Session") continue;
            if (t.Name == "execute_csharp" && !PluginSettings.Current.AllowCodeExecution) continue;
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["title"] = t.Name.Replace('_', ' '),
                ["description"] = $"[{t.Category}] {t.Description}",
                ["inputSchema"] = t.Schema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["readOnlyHint"] = t.ReadOnly,
                    ["destructiveHint"] = t.Destructive,
                    ["openWorldHint"] = false,
                },
            });
        }
        return new JsonObject { ["tools"] = arr };
    }

    static async Task<JsonNode> ToolsCall(JsonObject p)
    {
        string name = p?["name"]?.GetValue<string>();
        var args = p?["arguments"] as JsonObject ?? new JsonObject();
        if (name == null || !ToolRegistry.TryGet(name, out var def))
            return Fail($"Unknown tool '{name}'. Use tools/list to see available tools.");
        if (name == "execute_csharp" && !PluginSettings.Current.AllowCodeExecution)
            return Fail("Code execution is disabled in the Arian plugin settings.");

        var enabledCats = PluginSettings.Current.EnabledCategories;
        if (!string.IsNullOrWhiteSpace(enabledCats) && def.Category != "Session"
            && !enabledCats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(def.Category, StringComparer.OrdinalIgnoreCase))
            return Fail($"Tool category '{def.Category}' is disabled in the Arian settings (enabled: {enabledCats}).");
        var roCats = PluginSettings.Current.ReadOnlyCategories;
        if (!def.ReadOnly && !string.IsNullOrWhiteSpace(roCats)
            && roCats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(def.Category, StringComparer.OrdinalIgnoreCase))
            return Fail($"Category '{def.Category}' is set to READ-ONLY in the Arian settings, so '{name}' (which can change the model) is blocked.");
        var rec = Activity.Begin(name, def.Category, !def.ReadOnly);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ToolResult tr;
            if (def.AnyThread)
            {
                tr = ToolRunner.ToTool(def.Method.Invoke(null, BindAnyThread(def, args)));
            }
            else
            {
                var obj = await RevitDispatcher.Instance.RunAsync(def.Name, () => ToolRunner.Run(def, args),
                    PluginSettings.Current.ToolTimeoutSeconds);
                tr = (ToolResult)obj;
            }
            Activity.End(rec, true, sw.ElapsedMilliseconds);
            Log.Info($"tool {name} ok in {sw.ElapsedMilliseconds} ms");
            return Success(tr);
        }
        catch (Exception ex)
        {
            if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
            Activity.End(rec, false, sw.ElapsedMilliseconds, ex is ToolException ? ex.Message : ex.GetType().Name + ": " + ex.Message);
            Log.Warn($"tool {name} failed in {sw.ElapsedMilliseconds} ms: {(ex is ToolException ? ex.Message : ex.ToString())}");
            return Fail(FormatError(ex));
        }
    }

    static object[] BindAnyThread(ToolDef def, JsonObject args)
    {
        var vals = new object[def.Params.Length];
        for (int i = 0; i < vals.Length; i++)
        {
            var p = def.Params[i];
            if (args.TryGetPropertyValue(p.Name, out var n) && n != null) vals[i] = n.Deserialize(p.Type, J.Opts);
            else if (p.Required) throw new ToolException($"Missing required argument '{p.Name}'");
            else vals[i] = p.Default;
        }
        return vals;
    }

    static string FormatError(Exception ex)
    {
        if (ex is ToolException) return ex.Message;
        var sb = new System.Text.StringBuilder();
        sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        if (ex.GetType().Namespace?.StartsWith("Autodesk.Revit") == true)
            sb.Append("\n(Revit API exception - check argument values, that referenced elements exist, and that the geometry is valid.)");
        var frames = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains("System.Reflection")).Take(3);
        sb.Append("\n  at ").Append(string.Join("\n  at ", frames.Select(f => f.StartsWith("at ") ? f.Substring(3) : f)));
        return sb.ToString();
    }

    static JsonNode Fail(string text) => new JsonObject
    {
        ["isError"] = true,
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
    };

    static JsonNode Success(ToolResult tr)
    {
        var content = new JsonArray();
        if (tr.Image != null)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(tr.Image),
                ["mimeType"] = tr.ImageMime ?? "image/png",
            });
        }
        string text = tr.Text;
        if (text == null && tr.Json != null) text = tr.Json.ToJsonString(J.Opts);
        if (text != null)
        {
            if (text.Length > MaxText)
                text = text.Substring(0, MaxText) + $"\n...[output truncated at {MaxText} chars; use limit/offset/filters to narrow the result]";
            content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        }
        var res = new JsonObject { ["content"] = content };
        return res;
    }

    // ---------------- resources ----------------
    static JsonNode ResourcesList()
    {
        var arr = new JsonArray();
        foreach (var (uri, title, desc) in Guide.ListResources())
            arr.Add(new JsonObject { ["uri"] = uri, ["name"] = title, ["description"] = desc, ["mimeType"] = "text/markdown" });
        return new JsonObject { ["resources"] = arr };
    }

    static JsonNode ResourcesRead(JsonObject p)
    {
        string uri = p?["uri"]?.GetValue<string>();
        var text = Guide.ReadResource(uri) ?? throw new RpcException(-32002, "Resource not found: " + uri);
        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "text/markdown", ["text"] = text })
        };
    }

    // ---------------- prompts ----------------
    static JsonNode PromptsList()
    {
        var arr = new JsonArray();
        foreach (var pr in Guide.Prompts())
        {
            var args = new JsonArray();
            foreach (var a in pr.Args) args.Add(new JsonObject { ["name"] = a.Name, ["description"] = a.Description, ["required"] = a.Required });
            arr.Add(new JsonObject { ["name"] = pr.Name, ["description"] = pr.Description, ["arguments"] = args });
        }
        return new JsonObject { ["prompts"] = arr };
    }

    static JsonNode PromptsGet(JsonObject p)
    {
        string name = p?["name"]?.GetValue<string>();
        var args = new Dictionary<string, string>();
        if (p?["arguments"] is JsonObject ao) foreach (var kv in ao) args[kv.Key] = kv.Value?.ToString();
        var pr = Guide.Prompts().FirstOrDefault(x => x.Name == name) ?? throw new RpcException(-32602, "Unknown prompt: " + name);
        return new JsonObject
        {
            ["description"] = pr.Description,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = pr.Render(args) }
            })
        };
    }
}
