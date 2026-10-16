using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;

namespace Arian.RevitMcp.Tools;

public static class DynamoTools
{
    static string _resultPath;
    const char NL = (char)10;

    [Tool("run_dynamo_graph", """
        Runs a Dynamo for Revit graph (.dyn) on the active document — for parametric/generative modelling you or the user already have as Dynamo graphs. Runs asynchronously right after this call returns (Dynamo needs a genuine Revit command context); poll with get_dynamo_status.
        For NEW parametric geometry prefer execute_csharp (faster, synchronous, full control). Use this for existing .dyn files.
        @path Full path to the .dyn file. @show_ui Open the Dynamo window while running (default false = headless automation).
        """, Category = "Advanced", NoTransaction = true)]
    public static object RunDynamoGraph(string path, bool showUi = false)
    {
        DynamoRunner.Queue(Ctx.UiApp, path, showUi);
        return new JsonObject { ["queued"] = path, ["next"] = "Call get_dynamo_status until state is 'finished' or 'error'." };
    }

    [Tool("get_dynamo_status", "State of the last run_dynamo_graph / run_dynamo_python call: queued | running | started (run triggered) | finished (evaluation done; node_issues lists nodes in warning/error state) | error, with Dynamo's message and (for run_dynamo_python) the script result.", Category = "Advanced", NoDocument = true, ReadOnly = true)]
    public static object GetDynamoStatus()
    {
        var o = new JsonObject { ["state"] = DynamoRunner.State, ["graph"] = DynamoRunner.LastPath, ["message"] = DynamoRunner.LastMessage, ["finished_at"] = DynamoRunner.LastFinished?.ToString("s") };
        if (DynamoRunner.LastEvaluations > 0) { o["node_count"] = DynamoRunner.LastNodeCount; o["node_issues"] = new JsonArray(DynamoRunner.LastNodeIssues.Take(30).Select(s => (JsonNode)s).ToArray()); }
        if (_resultPath != null && File.Exists(_resultPath))
        {
            try { o["script_result"] = JsonNode.Parse(File.ReadAllText(_resultPath)); } catch { o["script_result"] = File.ReadAllText(_resultPath); }
            o["state"] = "finished";
        }
        return o;
    }

    static string Guid32() => Guid.NewGuid().ToString("N");

    [Tool("run_dynamo_python", """
        Runs Python (CPython 3 / PythonNet3, Dynamo's engine) inside a generated Dynamo graph with full Revit API access — the 'Dynamo way' to script Revit. Executes asynchronously after this call returns; poll get_dynamo_status (it returns the script_result).
        Available variables: doc (Document), uiapp, mm(v) (mm->feet), plus `from Autodesk.Revit.DB import *`. The code already runs inside a Dynamo transaction. Set `result = ...` (any JSON-serialisable value) to return data. Revit units are FEET.
        Example: `lv = FilteredElementCollector(doc).OfClass(Level).FirstElement(); result = lv.Name`
        For most tasks execute_csharp is faster and synchronous; use this when the user wants Dynamo/Python specifically.
        @code Python statements. @show_ui Show the Dynamo window.
        """, Category = "Advanced", NoTransaction = true)]
    public static object RunDynamoPython(string code, bool showUi = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "arian-dyn");
        Directory.CreateDirectory(dir);
        string id = Guid32();
        _resultPath = Path.Combine(dir, id + ".result.json");
        if (File.Exists(_resultPath)) File.Delete(_resultPath);
        string rp = _resultPath.Replace((char)92, '/');
        var userLines = code.Replace(((char)13).ToString(), "").Split(NL).Select(l => "    " + l);
        var lines = new List<string>
        {
            "import clr, json, traceback",
            "clr.AddReference('RevitAPI')",
            "clr.AddReference('RevitAPIUI')",
            "clr.AddReference('RevitServices')",
            "clr.AddReference('RevitNodes')",
            "from Autodesk.Revit.DB import *",
            "from RevitServices.Persistence import DocumentManager",
            "from RevitServices.Transactions import TransactionManager",
            "doc = DocumentManager.Instance.CurrentDBDocument",
            "uiapp = DocumentManager.Instance.CurrentUIApplication",
            "def mm(v): return v / 304.8",
            "result = None",
            "OUT = None",
            "try:",
            "    TransactionManager.Instance.EnsureInTransaction(doc)",
        };
        lines.AddRange(userLines);
        lines.Add("    TransactionManager.Instance.TransactionTaskDone()");
        lines.Add("    open(r'" + rp + "', 'w').write(json.dumps({'ok': True, 'result': result}, default=str))");
        lines.Add("except Exception:");
        lines.Add("    try: TransactionManager.Instance.ForceCloseTransaction()");
        lines.Add("    except: pass");
        lines.Add("    open(r'" + rp + "', 'w').write(json.dumps({'ok': False, 'error': traceback.format_exc()}))");
        string py = string.Join(NL.ToString(), lines);

        string nodeId = Guid32();
        var node = new JsonObject
        {
            ["ConcreteType"] = "PythonNodeModels.PythonNode, PythonNodeModels",
            ["Code"] = py,
            ["Engine"] = "PythonNet3",
            ["VariableInputPorts"] = true,
            ["Id"] = nodeId,
            ["NodeType"] = "PythonScriptNode",
            ["Inputs"] = new JsonArray(),
            ["Outputs"] = new JsonArray(new JsonObject { ["Id"] = Guid32(), ["Name"] = "OUT", ["Description"] = "Result of the python script", ["UsingDefaultValue"] = false, ["Level"] = 2, ["UseLevels"] = false, ["KeepListStructure"] = false }),
            ["Replication"] = "Disabled",
            ["Description"] = "Runs an embedded Python script.",
        };
        var graph = new JsonObject
        {
            ["Uuid"] = Guid.NewGuid().ToString(),
            ["IsCustomNode"] = false,
            ["Description"] = "Generated by Arian Revit MCP",
            ["Name"] = "ArianPython_" + id.Substring(0, 8),
            ["ElementResolver"] = new JsonObject { ["ResolutionMap"] = new JsonObject() },
            ["Inputs"] = new JsonArray(),
            ["Outputs"] = new JsonArray(),
            ["Nodes"] = new JsonArray(node),
            ["Connectors"] = new JsonArray(),
            ["Dependencies"] = new JsonArray(),
            ["NodeLibraryDependencies"] = new JsonArray(),
            ["EnableLegacyPolyCurveBehavior"] = true,
            ["Thumbnail"] = "",
            ["GraphDocumentationURL"] = null,
            ["ExtensionWorkspaceData"] = new JsonArray(),
            ["Author"] = "Arian",
            ["Linting"] = new JsonObject { ["activeLinter"] = "None", ["activeLinterId"] = "7b75fb44-43fd-4631-a878-29f4d5d8399a", ["warningCount"] = 0, ["errorCount"] = 0 },
            ["Bindings"] = new JsonArray(),
            ["View"] = new JsonObject
            {
                ["Dynamo"] = new JsonObject { ["ScaleFactor"] = 1.0, ["HasRunWithoutCrash"] = false, ["IsVisibleInDynamoLibrary"] = true, ["Version"] = "4.1.0.2940", ["RunType"] = "Automatic", ["RunPeriod"] = "100" },
                ["Camera"] = new JsonObject { ["Name"] = "_Background Preview", ["EyeX"] = -17.0, ["EyeY"] = 24.0, ["EyeZ"] = 50.0, ["LookX"] = 12.0, ["LookY"] = -13.0, ["LookZ"] = -58.0, ["UpX"] = 0.0, ["UpY"] = 1.0, ["UpZ"] = 0.0 },
                ["ConnectorPins"] = new JsonArray(),
                ["NodeViews"] = new JsonArray(new JsonObject { ["Id"] = nodeId, ["Name"] = "Python Script", ["IsSetAsInput"] = false, ["IsSetAsOutput"] = false, ["Excluded"] = false, ["ShowGeometry"] = true, ["X"] = 0.0, ["Y"] = 0.0 }),
                ["Annotations"] = new JsonArray(),
                ["X"] = 0.0, ["Y"] = 0.0, ["Zoom"] = 1.0,
            },
        };
        var path = Path.Combine(dir, id + ".dyn");
        File.WriteAllText(path, graph.ToJsonString());
        DynamoRunner.Queue(Ctx.UiApp, path, showUi);
        return new JsonObject { ["queued"] = path, ["next"] = "Call get_dynamo_status until state is 'finished' (it includes script_result)." };
    }

    [Tool("list_dynamo_graphs", "Lists .dyn graph files in a folder (default: the Dynamo samples shipped with Revit and the user's Documents/Dynamo).", Category = "Advanced", NoDocument = true, ReadOnly = true)]
    public static object ListDynamoGraphs(string folder = null)
    {
        var dirs = folder != null ? new[] { folder } : new[]
        {
            @"C:\Program Files\Autodesk\Revit 2027\AddIns\DynamoForRevit\Samples",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dynamo"),
        };
        var arr = new JsonArray();
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(d, "*.dyn", SearchOption.AllDirectories).Take(200))
                arr.Add(f);
        return new JsonObject { ["graphs"] = arr };
    }
}
