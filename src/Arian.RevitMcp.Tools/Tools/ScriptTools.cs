using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Arian.RevitMcp.Tools;

public static class ScriptTools
{
    [Tool("execute_csharp", """
        Compile and run C# code INSIDE Revit with full Revit API access — the escape hatch for anything the dedicated tools don't cover (custom geometry, bulk edits, Dynamo-like parametric generation, API features).
        Your code is the BODY of: object Run(Document doc, UIApplication uiapp, UIDocument uidoc, JsonObject args, Action<object> Print). Use `return` to return a value (serialised to JSON; ElementId and XYZ are supported) and Print(x) to log text.
        Namespaces preloaded: System, System.Linq, System.Collections.Generic, System.Text.Json(.Nodes), Autodesk.Revit.DB (+Architecture, Structure, Mechanical, Plumbing, Electrical, Analysis, IFC), Autodesk.Revit.UI, Arian.RevitMcp.Core (U unit helpers: U.Mm(mm)->feet, U.ToMm(feet)).
        REMEMBER: Revit internal units are decimal FEET; convert mm with U.Mm(). By default the code runs inside ONE transaction (rolled back on exception). Set transaction=false if your code manages its own Transactions/TransactionGroups.
        Example body: `var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).ToElements(); return walls.Count;`
        @code Statements forming the method body.
        @members Optional extra static members (helper methods/classes) inserted into the script class.
        @args Optional JSON object passed to the code as `args`.
        @transaction Wrap in a single transaction (default true).
        """, Category = "Advanced", NoTransaction = true, Destructive = true, NoDocument = true, DocumentOptional = true)]
    public static object ExecuteCSharp(string code, string members = null, JsonObject args = null, bool transaction = true)
    {
        var doc = Ctx.Doc;
        var o = ScriptEngine.Run(code, members, args, transaction, "Arian MCP: execute_csharp");
        var res = new JsonObject();
        if (o.Value != null)
        {
            try { res["result"] = J.ToNode(o.Value); }
            catch { res["result"] = o.Value.ToString(); }
        }
        if (o.Output.Count > 0) res["output"] = new JsonArray(o.Output.Select(s => (JsonNode)s).ToArray());
        if (res.Count == 0) res["ok"] = true;
        return res;
    }
}
