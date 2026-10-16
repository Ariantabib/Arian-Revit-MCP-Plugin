using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Arian.RevitMcp.Server;

namespace Arian.RevitMcp.Core;

/// <summary>Compiles and runs C# snippets inside Revit with Roslyn.</summary>
public static class ScriptEngine
{
    static List<MetadataReference> _refs;
    static int _counter;

    const string Usings = """
        using System; using System.Collections.Generic; using System.Linq; using System.Text; using System.IO;
        using System.Text.Json; using System.Text.Json.Nodes;
        using Autodesk.Revit.DB; using Autodesk.Revit.UI; using Autodesk.Revit.DB.Architecture; using Autodesk.Revit.DB.Structure;
        using Autodesk.Revit.DB.Mechanical; using Autodesk.Revit.DB.Plumbing; using Autodesk.Revit.DB.Electrical;
        using Autodesk.Revit.DB.Analysis; using Autodesk.Revit.DB.Visual; using Autodesk.Revit.DB.IFC;
        using Arian.RevitMcp.Core;
        """;

    static List<MetadataReference> References()
    {
        if (_refs != null) return _refs;
        var set = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var name = Path.GetFileName(path);
            if (set.ContainsKey(name)) return;
            try { set[name] = MetadataReference.CreateFromFile(path); } catch { }
        }
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic) continue;
            try { Add(a.Location); } catch { }
        }
        var rt = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var n in new[] { "System.Runtime.dll", "netstandard.dll", "System.Collections.dll", "System.Linq.dll", "System.Text.Json.dll", "System.Runtime.Extensions.dll", "System.Console.dll", "System.Memory.dll", "System.Private.CoreLib.dll" })
            Add(Path.Combine(rt, n));
        Add(typeof(Autodesk.Revit.DB.Element).Assembly.Location);
        Add(typeof(UIApplication).Assembly.Location);
        Add(typeof(ScriptEngine).Assembly.Location);
        _refs = set.Values.ToList();
        return _refs;
    }

    public sealed class Outcome
    {
        public object Value;
        public List<string> Output = new();
    }

    public static JsonObject ToResult(Outcome o)
    {
        var res = new JsonObject();
        if (o.Value != null)
        {
            try { res["result"] = J.ToNode(o.Value); }
            catch { res["result"] = o.Value.ToString(); }
        }
        if (o.Output.Count > 0) res["output"] = new JsonArray(o.Output.Select(x => (JsonNode)x).ToArray());
        if (res.Count == 0) res["ok"] = true;
        return res;
    }

    public sealed class Compiled
    {
        public MethodInfo Method;
    }

    /// <summary>Compiles the snippet; throws ToolException with diagnostics on errors.</summary>
    public static Compiled Compile(string body, string members)
    {
        string cls = "__ArianScript" + Interlocked.Increment(ref _counter);
        var sb = new StringBuilder();
        sb.AppendLine(Usings);
        sb.AppendLine($"public static class {cls} {{");
        if (!string.IsNullOrWhiteSpace(members)) sb.AppendLine(members);
        sb.AppendLine("public static object Run(Document doc, UIApplication uiapp, UIDocument uidoc, JsonObject args, Action<object> Print) {");
        int firstLine = sb.ToString().Split((char)10).Length; // line number where user body starts (1-based)
        sb.AppendLine(body);
        sb.AppendLine("return null; } }");

        var tree = CSharpSyntaxTree.ParseText(sb.ToString(), new CSharpParseOptions(LanguageVersion.Latest));
        var comp = CSharpCompilation.Create(cls, new[] { tree }, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable));
        using var ms = new MemoryStream();
        var emit = comp.Emit(ms);
        if (!emit.Success)
        {
            var errs = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(15).Select(d =>
            {
                var ls = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                return $"line {ls - firstLine + 1}: {d.Id} {d.GetMessage()}";
            });
            throw new ToolException("C# compile errors (line numbers relative to your code):" + Environment.NewLine + string.Join(Environment.NewLine, errs));
        }
        var asm = Assembly.Load(ms.ToArray());
        return new Compiled { Method = asm.GetType(cls).GetMethod("Run") };
    }

    public static Outcome Run(string body, string members, JsonObject args, bool useTransaction, string txName)
        => Execute(Compile(body, members), args, useTransaction, txName);

    public static Outcome Execute(Compiled compiled, JsonObject args, bool useTransaction, string txName)
    {
        var method = compiled.Method;
        var outcome = new Outcome();
        var doc = Ctx.Doc;
        Action<object> print = o => outcome.Output.Add(o is JsonNode jn ? jn.ToJsonString() : o?.ToString() ?? "null");
        object[] callArgs = { doc, Ctx.UiApp, Ctx.UiApp?.ActiveUIDocument, args ?? new JsonObject(), print };

        if (useTransaction && doc != null)
        {
            using var tx = new Transaction(doc, txName);
            tx.Start();
            var fp = new ToolFailurePreprocessor(PluginSettings.Current.WarningPolicy);
            var fho = tx.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(fp);
            tx.SetFailureHandlingOptions(fho);
            try
            {
                outcome.Value = method.Invoke(null, callArgs);
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new ToolException("Revit rolled back the script's transaction: " + string.Join(" | ", fp.Errors));
            }
            catch (TargetInvocationException tie)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw new ToolException("Script threw " + tie.InnerException.GetType().Name + ": " + tie.InnerException.Message + " (transaction rolled back)");
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
            if (fp.Warnings.Count > 0) outcome.Output.Add("[revit warnings] " + string.Join(" | ", fp.Warnings.Distinct()));
        }
        else
        {
            try { outcome.Value = method.Invoke(null, callArgs); }
            catch (TargetInvocationException tie)
            {
                throw new ToolException("Script threw " + tie.InnerException.GetType().Name + ": " + tie.InnerException.Message);
            }
        }
        return outcome;
    }
}

