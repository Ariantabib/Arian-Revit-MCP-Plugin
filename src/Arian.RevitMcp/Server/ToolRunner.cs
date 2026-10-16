using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Server;

/// <summary>Returned by a tool to send an image back to the agent.</summary>
public sealed class ImageResult
{
    public byte[] Bytes;
    public string Mime = "image/png";
    public object Info;
}

public sealed class ToolResult
{
    public JsonNode Json;
    public byte[] Image;
    public string ImageMime;
    public string Text;
}

/// <summary>Collects warnings/errors during a tool transaction and applies the configured policy.</summary>
public sealed class ToolFailurePreprocessor : IFailuresPreprocessor
{
    public readonly List<string> Warnings = new();
    public readonly List<string> Errors = new();
    public readonly List<string> AutoResolved = new();
    readonly WarningPolicy _policy;
    public ToolFailurePreprocessor(WarningPolicy p) { _policy = p; }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
    {
        bool rollback = false;
        foreach (var m in fa.GetFailureMessages())
        {
            var sev = m.GetSeverity();
            string text = m.GetDescriptionText();
            if (sev == FailureSeverity.Warning)
            {
                Warnings.Add(text);
                if (_policy != WarningPolicy.Off) fa.DeleteWarning(m);
            }
            else
            {
                if (_policy == WarningPolicy.IgnoreAndResolveErrors && m.HasResolutions())
                {
                    try { fa.ResolveFailure(m); AutoResolved.Add(text); continue; } catch { }
                }
                Errors.Add(text);
                rollback = true;
            }
        }
        return rollback ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}

public static class ToolRunner
{
    /// <summary>Runs on the Revit thread.</summary>
    public static ToolResult Run(ToolDef def, JsonObject args)
    {
        args ??= new JsonObject();
        Activity.CancelRequested = false;
        if (!def.NoDocument || def.DocumentOptional)
        {
            string docArg = args["document"]?.GetValue<string>();
            Ctx.Doc = Ctx.FindDocument(docArg);
            if (Ctx.Doc == null && !def.DocumentOptional) throw new ToolException("No Revit document is open. Use create_project or open_document first.");
        }
        else Ctx.Doc = Ctx.UiApp?.ActiveUIDocument?.Document;

        if (def.CustomHandler != null)
        {
            var cr = def.CustomHandler(args);
            return ToTool(cr);
        }
        var callArgs = Bind(def, args);

        object result;
        ToolFailurePreprocessor fp = null;
        if (def.ReadOnly || def.NoTransaction || Ctx.Doc == null)
        {
            result = Invoke(def, callArgs);
        }
        else
        {
            var doc = Ctx.Doc;
            if (doc.IsReadOnly && !doc.IsFamilyDocument) throw new ToolException("The document is read-only.");
            using var tx = new Transaction(doc, "Arian MCP: " + def.Name);
            tx.Start();
            fp = new ToolFailurePreprocessor(PluginSettings.Current.WarningPolicy);
            var fho = tx.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(fp);
            fho.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(fho);
            try
            {
                result = Invoke(def, callArgs);
                var st = tx.Commit();
                if (st != TransactionStatus.Committed)
                {
                    var why = fp.Errors.Count > 0 ? string.Join(" | ", fp.Errors.Distinct()) : st.ToString();
                    throw new ToolException($"Revit rolled back the operation: {why}");
                }
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
        }

        var tr = ToTool(result);
        if (fp != null && tr.Json is JsonObject jo)
        {
            if (fp.Warnings.Count > 0) jo["revit_warnings"] = new JsonArray(fp.Warnings.Distinct().Select(w => (JsonNode)w).ToArray());
            if (fp.AutoResolved.Count > 0) jo["auto_resolved_errors"] = new JsonArray(fp.AutoResolved.Distinct().Select(w => (JsonNode)w).ToArray());
        }
        return tr;
    }

    static object Invoke(ToolDef def, object[] callArgs)
    {
        try { return def.Method.Invoke(null, callArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    static object[] Bind(ToolDef def, JsonObject args)
    {
        var vals = new object[def.Params.Length];
        for (int i = 0; i < vals.Length; i++)
        {
            var p = def.Params[i];
            if (args.TryGetPropertyValue(p.Name, out var node) && node != null)
            {
                try { vals[i] = node.Deserialize(p.Type, J.Opts); }
                catch (Exception ex)
                {
                    throw new ToolException($"Invalid value for argument '{p.Name}' (expected {Describe(p.Type)}): {ex.Message}");
                }
            }
            else if (p.Required)
            {
                throw new ToolException($"Missing required argument '{p.Name}' ({Describe(p.Type)}){(string.IsNullOrEmpty(p.Description) ? "" : ": " + p.Description)}");
            }
            else vals[i] = p.Default;
        }
        // reject unknown args to catch typos early
        foreach (var kv in args)
        {
            if (kv.Key == "document") continue;
            if (!def.Params.Any(p => p.Name == kv.Key))
                throw new ToolException($"Unknown argument '{kv.Key}' for tool '{def.Name}'. Valid arguments: {string.Join(", ", def.Params.Select(p => p.Name))}");
        }
        return vals;
    }

    static string Describe(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(double[])) return "array of numbers";
        if (t == typeof(double[][])) return "array of [x,y,z] points";
        if (t.IsArray) return "array of " + Describe(t.GetElementType());
        return t.Name.ToLowerInvariant();
    }

    public static ToolResult ToTool(object result)
    {
        switch (result)
        {
            case null: return new ToolResult { Json = new JsonObject { ["ok"] = true } };
            case ToolResult tr: return tr;
            case ImageResult ir:
                return new ToolResult { Image = ir.Bytes, ImageMime = ir.Mime, Json = ir.Info == null ? null : J.ToNode(ir.Info) };
            case string s: return new ToolResult { Text = s };
            default:
                var n = J.ToNode(result);
                if (n is JsonArray || n is JsonValue) n = new JsonObject { ["result"] = n };
                return new ToolResult { Json = n };
        }
    }
}
