using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

// =====================================================================================
//  Tiny expression engine: parses "Width/2 - Leg_Size", evaluates it with parameter values
//  and renders it as a unit-correct Revit formula.
// =====================================================================================
abstract class Ex
{
    public abstract double Eval(Func<string, double> p);
    public abstract bool IsLen(Func<string, bool> pl);
    public abstract string Gen(bool len, Func<string, bool> pl);
    public static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

sealed class ExNum : Ex
{
    public double V;
    public override double Eval(Func<string, double> p) => V;
    public override bool IsLen(Func<string, bool> pl) => false;
    public override string Gen(bool len, Func<string, bool> pl) => len ? Fmt(V) + " mm" : Fmt(V);
}

sealed class ExPar : Ex
{
    public string N;
    public override double Eval(Func<string, double> p) => p(N);
    public override bool IsLen(Func<string, bool> pl) => pl(N);
    public override string Gen(bool len, Func<string, bool> pl) => N;
}

sealed class ExNeg : Ex
{
    public Ex X;
    public override double Eval(Func<string, double> p) => -X.Eval(p);
    public override bool IsLen(Func<string, bool> pl) => X.IsLen(pl);
    public override string Gen(bool len, Func<string, bool> pl) => "-(" + X.Gen(len, pl) + ")";
}

sealed class ExBin : Ex
{
    public char Op; public Ex L, R;
    public override double Eval(Func<string, double> p) => Op switch { '+' => L.Eval(p) + R.Eval(p), '-' => L.Eval(p) - R.Eval(p), '*' => L.Eval(p) * R.Eval(p), '/' => L.Eval(p) / R.Eval(p), _ => 0 };
    public override bool IsLen(Func<string, bool> pl) => Op switch
    {
        '+' or '-' => L.IsLen(pl) || R.IsLen(pl),
        '*' => L.IsLen(pl) || R.IsLen(pl),
        _ => L.IsLen(pl) && !R.IsLen(pl),
    };
    public override string Gen(bool len, Func<string, bool> pl)
    {
        switch (Op)
        {
            case '+': case '-': return "(" + L.Gen(len, pl) + " " + Op + " " + R.Gen(len, pl) + ")";
            case '*':
                {
                    bool ll = L.IsLen(pl), rl = R.IsLen(pl);
                    if (len && !ll && !rl) return "(" + L.Gen(false, pl) + " * " + R.Gen(true, pl) + ")";
                    if (len && ll && rl) return "(" + L.Gen(true, pl) + " * " + R.Gen(false, pl) + ")"; // length*length is unusual; keep Revit happy
                    return "(" + L.Gen(len && ll, pl) + " * " + R.Gen(len && rl, pl) + ")";
                }
            default:
                {
                    bool ll = L.IsLen(pl), rl = R.IsLen(pl);
                    if (len) return "(" + L.Gen(true, pl) + " / " + R.Gen(false, pl) + ")";
                    return "(" + L.Gen(ll, pl) + " / " + R.Gen(rl, pl) + ")";
                }
        }
    }
}

sealed class ExFn : Ex
{
    public string Name; public List<Ex> A = new();
    public override double Eval(Func<string, double> p)
    {
        var v = A.Select(a => a.Eval(p)).ToList();
        return Name switch { "min" => v.Min(), "max" => v.Max(), "abs" => Math.Abs(v[0]), "sqrt" => Math.Sqrt(v[0]), "round" => Math.Round(v[0]), _ => throw new ToolException("Unknown function " + Name) };
    }
    public override bool IsLen(Func<string, bool> pl) => A.Any(a => a.IsLen(pl));
    public override string Gen(bool len, Func<string, bool> pl)
    {
        bool l = len || IsLen(pl);
        string g(int i) => A[i].Gen(l, pl);
        return Name switch
        {
            "min" => $"if({g(0)} < {g(1)}, {g(0)}, {g(1)})",
            "max" => $"if({g(0)} > {g(1)}, {g(0)}, {g(1)})",
            "abs" => $"abs({g(0)})",
            "sqrt" => $"sqrt({g(0)})",
            "round" => $"round({g(0)})",
            _ => throw new ToolException("Unknown function " + Name),
        };
    }
}

sealed class ExParser
{
    readonly string _s; int _i;
    ExParser(string s) { _s = s ?? ""; }
    public static Ex Parse(string s)
    {
        var p = new ExParser(s);
        var e = p.Sum();
        p.Ws();
        if (p._i < p._s.Length) throw new ToolException($"Cannot parse expression '{s}' at position {p._i}.");
        return e;
    }
    void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
    Ex Sum()
    {
        var l = Prod();
        while (true)
        {
            Ws();
            if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) { char op = _s[_i++]; l = new ExBin { Op = op, L = l, R = Prod() }; }
            else return l;
        }
    }
    Ex Prod()
    {
        var l = Unary();
        while (true)
        {
            Ws();
            if (_i < _s.Length && (_s[_i] == '*' || _s[_i] == '/')) { char op = _s[_i++]; l = new ExBin { Op = op, L = l, R = Unary() }; }
            else return l;
        }
    }
    Ex Unary()
    {
        Ws();
        if (_i < _s.Length && _s[_i] == '-') { _i++; return new ExNeg { X = Unary() }; }
        if (_i < _s.Length && _s[_i] == '+') { _i++; return Unary(); }
        return Atom();
    }
    Ex Atom()
    {
        Ws();
        if (_i >= _s.Length) throw new ToolException($"Unexpected end of expression '{_s}'.");
        char c = _s[_i];
        if (c == '(') { _i++; var e = Sum(); Ws(); if (_i >= _s.Length || _s[_i] != ')') throw new ToolException($"Missing ')' in '{_s}'."); _i++; return e; }
        if (char.IsDigit(c) || c == '.')
        {
            int st = _i; while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            var n = new ExNum { V = double.Parse(_s.Substring(st, _i - st), CultureInfo.InvariantCulture) };
            // optional unit suffix
            int save = _i; Ws();
            if (_i + 1 < _s.Length + 0 && string.Compare(_s, _i, "mm", 0, 2, StringComparison.OrdinalIgnoreCase) == 0 && (_i + 2 >= _s.Length || !char.IsLetterOrDigit(_s[_i + 2]))) _i += 2; else _i = save;
            return n;
        }
        if (c == '[')
        {
            int end = _s.IndexOf(']', _i);
            if (end < 0) throw new ToolException($"Missing ']' in '{_s}'.");
            string nm = _s.Substring(_i + 1, end - _i - 1).Trim();
            _i = end + 1;
            return new ExPar { N = nm };
        }
        if (char.IsLetter(c) || c == '_')
        {
            int st = _i; while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
            string id = _s.Substring(st, _i - st);
            Ws();
            if (_i < _s.Length && _s[_i] == '(')
            {
                _i++; var f = new ExFn { Name = id.ToLowerInvariant() };
                while (true) { f.A.Add(Sum()); Ws(); if (_i < _s.Length && _s[_i] == ',') { _i++; continue; } break; }
                Ws(); if (_i >= _s.Length || _s[_i] != ')') throw new ToolException($"Missing ')' in '{_s}'."); _i++;
                return f;
            }
            return new ExPar { N = id };
        }
        throw new ToolException($"Unexpected character '{c}' in expression '{_s}'.");
    }
}

// =====================================================================================
//  Spec classes
// =====================================================================================
public sealed class FamParamSpec
{
    [Desc("Parameter name (letters, digits, underscore — no spaces).")] public string Name;
    [Desc("length | number | integer | angle | area | volume | text | yesno | material | url | ...")] public string Type = "length";
    [Desc("Default value (mm for length; true/false for yesno; material name for material).")] public JsonNode Value;
    [Desc("Instance parameter (varies per placed element) instead of type parameter.")] public bool Instance;
    [Desc("Palette group: dimensions (default for length), materials, constraints, identity_data, visibility, data, general...")] public string Group;
    [Desc("Optional formula in terms of other parameters, e.g. 'Width / 2'.")] public string Formula;
}

public sealed class FamTypeSpec
{
    [Desc("Type name, e.g. '1600x800'.")] public string Name;
    [Desc("Parameter values for this type (mm for lengths); unspecified parameters keep the default.")] public Dictionary<string, JsonNode> Values;
}

public sealed class FamShapeSpec
{
    [Desc("box | cylinder | prism")] public string Kind = "box";
    public string Name;
    [Desc("box: [from,to] expressions along X (mm / parameter expressions)")] public string[] X;
    [Desc("box: [from,to] along Y")] public string[] Y;
    [Desc("box/cylinder: [from,to] along Z (height)")] public string[] Z;
    [Desc("cylinder: [x,y] centre expressions")] public string[] Center;
    [Desc("cylinder: radius expression")] public string Radius;
    [Desc("prism: xy | xz | yz")] public string Plane = "xy";
    [Desc("prism: polygon points [[u,v],...] as expressions in the plane's coordinates")] public string[][] Points;
    [Desc("prism: [from,to] along the plane normal (z for xy, y for xz, x for yz)")] public string[] Range;
    [Desc("true = solid (default), false = void")] public bool Solid = true;
    public string Material;
    [Desc("Name of a family 'material' parameter to bind to this form.")] public string MaterialParameter;
    public string Subcategory;
    [Desc("Detail levels where visible: coarse, medium, fine")] public string[] DetailLevels;
}

public sealed class FamConnectorSpec
{
    [Desc("duct | pipe | electrical | conduit | cable_tray")] public string Kind;
    [Desc("Name of the shape carrying the connector face")] public string Shape;
    [Desc("[x,y,z] mm point on/near the face (use the face centre)")] public double[] Point;
    public string SystemType;
    [Desc("round | rectangular")] public string ShapeType = "round";
    public double? DiameterMm; public double? WidthMm; public double? HeightMm;
    public string FlowDirection = "bidirectional";
}

public sealed class FamilySpec
{
    public string Name;
    public string Template = "generic_model";
    public string Category;
    public string Folder;
    public List<FamParamSpec> Parameters = new();
    public List<FamTypeSpec> Types = new();
    public List<FamShapeSpec> Shapes = new();
    public List<FamConnectorSpec> Connectors = new();
    public bool Save = true;
    public bool Load = true;
    public bool Close = true;
    [Desc("Project to load into (default: the active project)")] public string Project;
    public bool FlexTest = true;
    [Desc("Delete the template's pre-built forms (e.g. the default beam/door geometry) before building. Default false.")] public bool ClearGeometry;
    [Desc("Delete the template's default types that are not listed in 'types' (default true when types are given).")] public bool ReplaceTypes = true;
}

public static class FamilySpecTools
{
    sealed class Ctx2
    {
        public Document Fd;
        public Dictionary<string, FamParamSpec> Params = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Vals = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PlaneByKey = new();      // "x|expr" -> plane name
        public Dictionary<string, double> PlaneVal = new();        // plane name -> default coordinate (mm)
        public int N;
        public List<string> Notes = new();
        public bool IsLen(string p) => Params.TryGetValue(p, out var s) && (s.Type ?? "length").Equals("length", StringComparison.OrdinalIgnoreCase);
        public double Val(string p) => Vals.TryGetValue(p, out var v) ? v : throw new ToolException($"Unknown parameter '{p}' used in an expression. Declare it in 'parameters'.");
    }

    [Tool("family_build_from_spec", """
        Builds a COMPLETE parametric family in one call from a declarative spec: parameters (with formulas), types, and shapes (boxes, cylinders, prisms) whose positions are EXPRESSIONS of the parameters (e.g. "-Width/2 + 50"). The tool creates the reference planes, labelled dimensions, formulas, locked extrusions/voids, types, connectors, runs a flex test, saves, and loads the family into the project.
        Works for tables, cabinets, shelving, columns, beams (I/H/rect profiles), footings, equipment, fixtures, simple doors/windows... Expressions: numbers (mm), parameter names, + - * / ( ), min(), max(), abs(), sqrt(). Read get_guidance topic 'family' for the full format and examples.
        @spec The family spec.
        """, Category = "Family", NoDocument = true, NoTransaction = true)]
    public static object FamilyBuildFromSpec(FamilySpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Name)) throw new ToolException("spec.name is required.");
        var warnings = new JsonArray();
        var c = new Ctx2();

        // ------------------------------------------------ 1. create the family document
        var created = FamilyDocTools.FamilyCreate(spec.Name, spec.Template ?? "generic_model", spec.Folder, spec.Category);
        var fd = Ctx.FindDocument(spec.Name);
        c.Fd = fd;
        Ctx.Doc = fd;
        try
        {
            if (spec.ClearGeometry)
                Tx(fd, "clear geometry", () =>
                {
                    var ids = new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).ToElementIds().ToList();
                    foreach (var id in ids) { try { fd.Delete(id); } catch { } }
                });
            // ------------------------------------------------ 2. parameters
            foreach (var p in spec.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) throw new ToolException("A parameter has no name.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(p.Name, @"^[A-Za-z_][A-Za-z0-9_ ]*$"))
                    throw new ToolException($"Parameter name '{p.Name}' must contain only letters, digits, underscores (and spaces; then write it as [Name With Spaces] inside expressions).");
                c.Params[p.Name] = p;
                p.Type ??= "length";
                double dv = 0;
                if (p.Value is JsonValue jv && jv.TryGetValue<double>(out var d)) dv = d;
                else if (p.Value is JsonValue jb && jb.TryGetValue<bool>(out var b)) dv = b ? 1 : 0;
                c.Vals[p.Name] = dv;
            }
            // (default value for length parameters without value)
            foreach (var p in spec.Parameters) if (p.Type.Equals("length", StringComparison.OrdinalIgnoreCase) && p.Value == null && string.IsNullOrWhiteSpace(p.Formula)) c.Vals[p.Name] = 100;

            Tx(fd, "parameters", () =>
            {
                foreach (var p in spec.Parameters)
                {
                    bool isLen = p.Type.Equals("length", StringComparison.OrdinalIgnoreCase);
                    JsonNode val = p.Value ?? (isLen && string.IsNullOrWhiteSpace(p.Formula) ? JsonValue.Create(100) : null);
                    FamilyDocTools.FamilyAddParameter(p.Name, p.Type, p.Instance, p.Group ?? (p.Type == "material" ? "materials" : "dimensions"), val, null);
                }
            });
            // formulas of user parameters + evaluated defaults
            Tx(fd, "formulas", () =>
            {
                foreach (var p in spec.Parameters.Where(x => !string.IsNullOrWhiteSpace(x.Formula)))
                {
                    var e = ExParser.Parse(p.Formula);
                    try { c.Vals[p.Name] = e.Eval(c.Val); } catch { }
                    string f = e.Gen(p.Type.Equals("length", StringComparison.OrdinalIgnoreCase), c.IsLen);
                    FamilyDocTools.FamilySetFormula(p.Name, f);
                }
            });

            // ------------------------------------------------ 3. collect all plane expressions
            var needs = new List<(string axis, string expr)>();
            void Need(string axis, string expr) { if (IsTok(expr, out _, out _)) return; if (!needs.Contains((axis, Norm(expr)))) needs.Add((axis, Norm(expr))); }
            foreach (var s in spec.Shapes)
            {
                switch ((s.Kind ?? "box").ToLowerInvariant())
                {
                    case "box":
                        Req(s.X, 2, "x", s); Req(s.Y, 2, "y", s); Req(s.Z, 2, "z", s);
                        Need("x", s.X[0]); Need("x", s.X[1]); Need("y", s.Y[0]); Need("y", s.Y[1]); Need("z", s.Z[0]); Need("z", s.Z[1]); break;
                    case "cylinder":
                        Req(s.Center, 2, "center", s); Req(s.Z, 2, "z", s); if (string.IsNullOrWhiteSpace(s.Radius)) throw new ToolException($"Shape '{s.Name}': radius is required.");
                        Need("x", $"({s.Center[0]}) - ({s.Radius})"); Need("x", $"({s.Center[0]}) + ({s.Radius})"); Need("y", $"({s.Center[1]}) - ({s.Radius})");
                        Need("z", s.Z[0]); Need("z", s.Z[1]); break;
                    case "prism":
                        if (s.Points == null || s.Points.Length < 3) throw new ToolException($"Shape '{s.Name}': prism needs 3+ points.");
                        Req(s.Range, 2, "range", s);
                        string pl = (s.Plane ?? "xy").ToLowerInvariant();
                        string au = pl == "yz" ? "y" : "x", av = pl == "xy" ? "y" : "z", an = pl == "xy" ? "z" : pl == "xz" ? "y" : "x";
                        foreach (var pt in s.Points) { Need(au, pt[0]); Need(av, pt[1]); }
                        Need(an, s.Range[0]); Need(an, s.Range[1]); break;
                    default: throw new ToolException($"Unknown shape kind '{s.Kind}' (box, cylinder, prism).");
                }
            }

            // ------------------------------------------------ 4. planes (+ helper parameters + dimensions)
            Tx(fd, "reference planes", () =>
            {
                foreach (var (axis, expr) in needs)
                {
                    var e = ExParser.Parse(expr);
                    double v = e.Eval(c.Val);
                    string key = axis + "|" + expr;
                    if (Math.Abs(v) < 1e-9) { c.PlaneByKey[key] = "@0"; continue; }
                    string name = $"AR_{axis.ToUpper()}{++c.N}";
                    FamilyDocTools.FamilyAddReferencePlane(name, axis, v);
                    c.PlaneByKey[key] = name;
                    c.PlaneVal[name] = v;
                }
                fd.Regenerate();
            });
            Tx(fd, "dimensions", () =>
            {
                var fm = fd.FamilyManager;
                foreach (var (axis, expr) in needs)
                {
                    string key = axis + "|" + expr;
                    var name = c.PlaneByKey[key];
                    if (name == "@0") continue;
                    var e = ExParser.Parse(expr);
                    double v = e.Eval(c.Val);
                    string helper = "Ref_" + name.Substring(3);
                    string f = e.Gen(true, c.IsLen);
                    if (v < 0) f = "-(" + f + ")";
                    FamUtil.EnsureType(fm);
                    var hp = fm.get_Parameter(helper) ?? fm.AddParameter(helper, GroupTypeId.Constraints, SpecTypeId.Length, false);
                    fm.SetFormula(hp, f);
                    string baseName = BasePlane(fd, axis);
                    try { FamilyDocTools.FamilyAddDimension(baseName, name, helper, null, false, 600 + 200 * (c.N % 6)); }
                    catch (Exception ex) { c.Notes.Add($"dimension for {name}: {ex.Message}"); }
                }
            });

            // ------------------------------------------------ 5. shapes
            var shapeIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var shapeInfo = new JsonArray();
            int sIdx = 0;
            foreach (var s in spec.Shapes)
            {
                sIdx++;
                string nm = string.IsNullOrWhiteSpace(s.Name) ? $"Shape{sIdx}" : s.Name;
                Tx(fd, "shape " + nm, () =>
                {
                    long id = BuildShape(c, s, nm);
                    shapeIds[nm] = id;
                    shapeInfo.Add(new JsonObject { ["name"] = nm, ["id"] = id, ["kind"] = s.Kind, ["solid"] = s.Solid });
                });
            }

            // ------------------------------------------------ 6. types
            Tx(fd, "types", () =>
            {
                var fm = fd.FamilyManager;
                FamUtil.EnsureType(fm);
                if (spec.Types.Count == 0)
                {
                    foreach (var pr in spec.Parameters.Where(p => p.Value != null && string.IsNullOrWhiteSpace(p.Formula)))
                    { var fp = fm.get_Parameter(pr.Name); if (fp != null) { try { if (fp.StorageType == StorageType.ElementId) FamilyDocTools.FamilySetParameterValue(pr.Name, pr.Value); } catch { } } }
                }
                foreach (var t in spec.Types) FamilyDocTools.FamilyAddType(t.Name, t.Values, true);
                if (spec.ReplaceTypes && spec.Types.Count > 0)
                {
                    var keep = new HashSet<string>(spec.Types.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
                    foreach (var ft in fm.Types.Cast<FamilyType>().Where(x => !keep.Contains(x.Name)).ToList())
                    {
                        try { fm.CurrentType = ft; fm.DeleteCurrentType(); } catch { }
                    }
                    var first = fm.Types.Cast<FamilyType>().FirstOrDefault(); if (first != null) fm.CurrentType = first;
                }
            });

            // ------------------------------------------------ 7. connectors
            var conns = new JsonArray();
            foreach (var cn in spec.Connectors)
            {
                Tx(fd, "connector", () =>
                {
                    if (!shapeIds.TryGetValue(cn.Shape ?? "", out var fid)) throw new ToolException($"Connector refers to unknown shape '{cn.Shape}'.");
                    var r = FamilyDocTools.FamilyAddConnector(cn.Kind, fid, cn.Point, cn.SystemType, cn.ShapeType ?? "round", cn.DiameterMm, cn.WidthMm, cn.HeightMm, cn.FlowDirection ?? "bidirectional");
                    conns.Add(J.ToNode(r));
                });
            }

            var result = new JsonObject
            {
                ["document"] = fd.Title,
                ["category"] = fd.OwnerFamily.FamilyCategory?.Name,
                ["parameters"] = new JsonArray(spec.Parameters.Select(p => (JsonNode)p.Name).ToArray()),
                ["planes_created"] = c.PlaneVal.Count,
                ["shapes"] = shapeInfo,
                ["types"] = new JsonArray(fd.FamilyManager.Types.Cast<FamilyType>().Select(t => (JsonNode)t.Name).ToArray()),
            };
            if (conns.Count > 0) result["connectors"] = conns;

            // ------------------------------------------------ 8. flex test
            if (spec.FlexTest && spec.Types.Count > 0)
            {
                try
                {
                    Tx(fd, "flex test", () =>
                    {
                        var ft = (JsonObject)J.ToNode(FamilyDocTools.FamilyFlexTest(null, null));
                        result["flex_test_passed"] = ft["passed"]?.DeepClone();
                        if (ft["passed"]?.GetValue<bool>() == false) c.Notes.Add("Flex test FAILED for some sizes: run family_flex_test for details.");
                    });
                }
                catch (Exception ex) { c.Notes.Add("flex test: " + ex.Message); }
            }
            // geometry summary of the final current type
            result["bounding_boxes"] = J.ToNode(FamilyDocTools.FamilyGetGeometrySummary());

            // ------------------------------------------------ 9. save / load
            if (spec.Save || spec.Load) fd.Save();
            result["path"] = fd.PathName;
            if (spec.Load)
            {
                try
                {
                    var load = FamilyDocTools.FamilyLoadIntoProject(spec.Project, true, false);
                    result["loaded"] = J.ToNode(load);
                }
                catch (Exception ex) { c.Notes.Add("load into project: " + ex.Message); }
            }
            if (c.Notes.Count > 0) result["notes"] = new JsonArray(c.Notes.Select(n => (JsonNode)n).ToArray());
            string savedPath = fd.PathName;
            if (spec.Close) { try { fd.Close(false); } catch { } }
            else result["hint"] = "Family document left open as '" + fd.Title + "'.";
            FamUtil.CleanBackups(savedPath);
            return result;
        }
        catch
        {
            try { fd.Close(false); } catch { }
            throw;
        }
    }

    static void Req(string[] a, int n, string what, FamShapeSpec s)
    {
        if (a == null || a.Length < n) throw new ToolException($"Shape '{s.Name}' ({s.Kind}): '{what}' must have {n} expressions.");
    }

    /// <summary>Removes whitespace outside [bracketed names] so equal expressions get equal keys.</summary>
    static string Norm(string e)
    {
        var sb = new StringBuilder(); bool inB = false;
        foreach (var ch in e ?? "")
        {
            if (ch == '[') inB = true; else if (ch == ']') inB = false;
            if (char.IsWhiteSpace(ch) && !inB) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Tokens: "plane:Left" (an existing reference plane) and "level:Upper Ref Level" (a family level).</summary>
    static bool IsTok(string e, out string kind, out string name)
    {
        kind = null; name = null;
        if (e == null) return false;
        var t = e.Trim();
        if (t.StartsWith("plane:", StringComparison.OrdinalIgnoreCase)) { kind = "plane"; name = t.Substring(6).Trim(); return true; }
        if (t.StartsWith("level:", StringComparison.OrdinalIgnoreCase)) { kind = "level"; name = t.Substring(6).Trim(); return true; }
        return false;
    }

    static Level FamLevel(Document fd, string name)
    {
        var all = new FilteredElementCollector(fd).OfClass(typeof(Level)).Cast<Level>().ToList();
        return all.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(l => l.Name.Replace(".", "").Replace(" ", "").Equals(name.Replace(".", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
               ?? throw new ToolException($"Level '{name}' not found in the family. Levels: {string.Join(", ", all.Select(l => l.Name))}");
    }

    static string BasePlane(Document fd, string axis)
    {
        var planes = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
        XYZ ax = axis == "x" ? XYZ.BasisX : axis == "y" ? XYZ.BasisY : XYZ.BasisZ;
        ReferencePlane best = null; double bd = double.MaxValue;
        foreach (var r in planes)
        {
            var n = r.GetPlane().Normal;
            if (Math.Abs(Math.Abs(n.DotProduct(ax)) - 1) > 1e-6) continue;
            if (r.Name.StartsWith("AR_")) continue;
            double d = Math.Abs(r.GetPlane().Origin.DotProduct(ax));
            // prefer exactly-zero planes that are references (center / ref level)
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) throw new ToolException("Template has no base reference plane on axis " + axis);
        return best.Name;
    }

    static void Tx(Document fd, string name, Action a)
    {
        using var t = new Transaction(fd, "Arian: " + name);
        t.Start();
        try { a(); t.Commit(); }
        catch { if (t.GetStatus() == TransactionStatus.Started) t.RollBack(); throw; }
    }

    static string PlaneOf(Ctx2 c, string axis, string expr)
    {
        if (IsTok(expr, out var kind, out var nm))
        {
            if (kind == "plane") { FamUtil.RefPlane(c.Fd, nm); return nm; }
            FamLevel(c.Fd, nm); return "#L:" + nm;
        }
        var key = axis + "|" + Norm(expr);
        return c.PlaneByKey[key];
    }

    static double ValOf(Ctx2 c, string expr)
    {
        if (IsTok(expr, out var kind, out var nm))
        {
            if (kind == "level") return U.ToMm(FamLevel(c.Fd, nm).Elevation);
            var rp = FamUtil.RefPlane(c.Fd, nm); var pl = rp.GetPlane();
            var n = pl.Normal;
            XYZ ax = Math.Abs(n.X) >= Math.Abs(n.Y) && Math.Abs(n.X) >= Math.Abs(n.Z) ? XYZ.BasisX : Math.Abs(n.Y) >= Math.Abs(n.Z) ? XYZ.BasisY : XYZ.BasisZ;
            return U.ToMm(pl.Origin.DotProduct(ax));
        }
        return ExParser.Parse(expr).Eval(c.Val);
    }

    static long BuildShape(Ctx2 c, FamShapeSpec s, string name)
    {
        var fd = c.Fd;
        string kind = (s.Kind ?? "box").ToLowerInvariant();
        double z0, z1;
        string basePlane; // plane name for the lower face (null => origin plane)
        var notes = c.Notes;

        if (kind == "box")
        {
            double x0 = ValOf(c, s.X[0]), x1 = ValOf(c, s.X[1]), y0 = ValOf(c, s.Y[0]), y1 = ValOf(c, s.Y[1]);
            z0 = ValOf(c, s.Z[0]); z1 = ValOf(c, s.Z[1]);
            if (x1 <= x0 || y1 <= y0 || z1 <= z0) throw new ToolException($"Shape '{name}': ranges must increase (x {x0}..{x1}, y {y0}..{y1}, z {z0}..{z1}).");
            var prof = new[] { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } };
            var edgePlanes = new Dictionary<(string, int), string>();
            var ext = MakeExtrusion(c, "xy", z0, z1 - z0, prof, null, null, s, name);
            var lockMap = new List<(string axis, double val, string plane)>
            {
                ("x", x0, PlaneOf(c, "x", s.X[0])), ("x", x1, PlaneOf(c, "x", s.X[1])), ("y", y0, PlaneOf(c, "y", s.Y[0])), ("y", y1, PlaneOf(c, "y", s.Y[1])),
            };
            LockEdges(c, ext, "xy", lockMap);
            LockZ(c, ext, "xy", PlaneOf(c, "z", s.Z[0]), PlaneOf(c, "z", s.Z[1]));
            return ext.Id.Value;
        }
        if (kind == "cylinder")
        {
            double cx = ValOf(c, s.Center[0]), cy = ValOf(c, s.Center[1]), r = ValOf(c, s.Radius);
            z0 = ValOf(c, s.Z[0]); z1 = ValOf(c, s.Z[1]);
            if (r <= 0 || z1 <= z0) throw new ToolException($"Shape '{name}': radius must be > 0 and z increasing.");
            var ext = MakeExtrusion(c, "xy", z0, z1 - z0, null, new[] { cx, cy }, r, s, name);
            // lock the circle tangent to three planes (left, right, front)
            try
            {
                var view = FamUtil.PlanView(fd);
                var arcRef = new List<Reference>();
                foreach (var id in ext.Sketch.GetAllElements()) if (fd.GetElement(id) is ModelCurve mc) arcRef.Add(mc.GeometryCurve.Reference);
                if (arcRef.Count > 0)
                {
                    foreach (var pn in new[] { PlaneOf(c, "x", $"({s.Center[0]}) - ({s.Radius})"), PlaneOf(c, "x", $"({s.Center[0]}) + ({s.Radius})"), PlaneOf(c, "y", $"({s.Center[1]}) - ({s.Radius})") })
                    {
                        if (pn == "@0") continue;
                        try { fd.FamilyCreate.NewAlignment(view, FamUtil.RefPlane(fd, pn).GetReference(), arcRef[0]); }
                        catch (Exception ex) { notes.Add($"{name}: cylinder lock to {pn}: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { notes.Add($"{name}: cylinder locks skipped: {ex.Message}"); }
            LockZ(c, ext, "xy", PlaneOf(c, "z", s.Z[0]), PlaneOf(c, "z", s.Z[1]));
            return ext.Id.Value;
        }
        // prism
        {
            string pl = (s.Plane ?? "xy").ToLowerInvariant();
            string au = pl == "yz" ? "y" : "x", av = pl == "xy" ? "y" : "z", an = pl == "xy" ? "z" : pl == "xz" ? "y" : "x";
            double r0 = ValOf(c, s.Range[0]), r1 = ValOf(c, s.Range[1]);
            if (r1 <= r0) throw new ToolException($"Shape '{name}': range must increase.");
            var prof = s.Points.Select(p => new[] { ValOf(c, p[0]), ValOf(c, p[1]) }).ToArray();
            var ext = MakeExtrusion(c, pl, r0, r1 - r0, prof, null, null, s, name);
            var lockMap = new List<(string axis, double val, string plane)>();
            for (int i = 0; i < s.Points.Length; i++)
            {
                lockMap.Add((au, prof[i][0], PlaneOf(c, au, s.Points[i][0])));
                lockMap.Add((av, prof[i][1], PlaneOf(c, av, s.Points[i][1])));
            }
            LockEdges(c, ext, pl, lockMap);
            // lock extents along the normal
            LockNormal(c, ext, pl, PlaneOf(c, an, s.Range[0]), PlaneOf(c, an, s.Range[1]));
            return ext.Id.Value;
        }
    }

    static Extrusion MakeExtrusion(Ctx2 c, string plane, double offsetMm, double depthMm, double[][] profile, double[] circleCenter, double? radius, FamShapeSpec s, string name)
    {
        var fd = c.Fd;
        var curves = new CurveArray();
        if (radius.HasValue)
        {
            var center = FamUtil.Point3(plane, circleCenter[0], circleCenter[1], offsetMm);
            curves.Append(Arc.Create(center, U.Mm(radius.Value), 0, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
        }
        else
        {
            var pts = Geo.Clean(profile.Select(p => FamUtil.Point3(plane, p[0], p[1], offsetMm)).ToList(), true);
            for (int i = 0; i < pts.Count; i++) curves.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        }
        var arr = new CurveArrArray(); arr.Append(curves);
        var sp = FamUtil.Sketch(fd, plane, offsetMm);
        var ext = fd.FamilyCreate.NewExtrusion(s.Solid, arr, sp, U.Mm(depthMm));
        try { ext.Name = name; } catch { }
        var fm = fd.FamilyManager;
        if (!string.IsNullOrWhiteSpace(s.MaterialParameter))
        {
            var fp = FamUtil.Param(fd, s.MaterialParameter, true) ?? fm.AddParameter(s.MaterialParameter, GroupTypeId.Materials, SpecTypeId.Reference.Material, false);
            FamUtil.EnsureMaterialValue(fd, fp);
            var mp = ext.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (mp != null) fm.AssociateElementParameterToFamilyParameter(mp, fp);
        }
        else if (!string.IsNullOrWhiteSpace(s.Material))
        {
            var m = new FilteredElementCollector(fd).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name.Equals(s.Material, StringComparison.OrdinalIgnoreCase));
            ext.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(m != null ? m.Id : Material.Create(fd, s.Material));
        }
        if (!string.IsNullOrWhiteSpace(s.Subcategory) || (s.DetailLevels != null && s.DetailLevels.Length > 0))
            FamilyDocTools.ApplyFormOptionsPublic(fd, ext, null, null, null, s.Subcategory, s.DetailLevels);
        fd.Regenerate();
        return ext;
    }

    static void LockEdges(Ctx2 c, Extrusion ext, string plane, List<(string axis, double val, string plane)> map)
    {
        var fd = c.Fd;
        var view = plane == "xy" ? FamUtil.PlanView(fd) : plane == "xz" ? FamUtil.Elevation(fd, "Front", "Back") : FamUtil.Elevation(fd, "Right", "Left");
        if (view == null) { c.Notes.Add("no view to lock edges"); return; }
        foreach (var id in ext.Sketch.GetAllElements())
        {
            if (fd.GetElement(id) is not ModelCurve mc || mc.GeometryCurve is not Line ln) continue;
            var d = ln.Direction; var mid = (ln.GetEndPoint(0) + ln.GetEndPoint(1)) / 2;
            // which world axis is the edge CONSTANT in?
            string constAxis = Math.Abs(d.X) > 0.99 ? null : null;
            var cands = new List<(string axis, double val)>();
            if (Math.Abs(d.X) < 1e-6) cands.Add(("x", U.ToMm(mid.X)));
            if (Math.Abs(d.Y) < 1e-6) cands.Add(("y", U.ToMm(mid.Y)));
            if (Math.Abs(d.Z) < 1e-6) cands.Add(("z", U.ToMm(mid.Z)));
            foreach (var (axis, val) in cands)
            {
                var hit = map.FirstOrDefault(m => m.axis == axis && Math.Abs(m.val - val) < 0.05 && m.plane != "@0");
                if (hit.plane == null) continue;
                try { fd.FamilyCreate.NewAlignment(view, FamUtil.RefPlane(fd, hit.plane).GetReference(), mc.GeometryCurve.Reference); }
                catch (Exception ex) { c.Notes.Add($"align edge to {hit.plane}: {ex.Message}"); }
                break;
            }
            // edges that lie on a zero plane (centre/ref level): lock them to the base plane
            foreach (var (axis, val) in cands)
            {
                var hit0 = map.FirstOrDefault(m => m.axis == axis && Math.Abs(m.val - val) < 0.05 && m.plane == "@0");
                if (hit0.plane == null) continue;
                try { fd.FamilyCreate.NewAlignment(view, FamUtil.RefPlane(fd, BasePlane(fd, axis)).GetReference(), mc.GeometryCurve.Reference); }
                catch (Exception ex) { c.Notes.Add($"align edge to origin plane: {ex.Message}"); }
                break;
            }
        }
    }

    static void LockZ(Ctx2 c, Extrusion ext, string plane, string bottomPlane, string topPlane)
    {
        LockFaceTo(c, ext, plane, bottomPlane, false);
        LockFaceTo(c, ext, plane, topPlane, true);
    }

    static void LockNormal(Ctx2 c, Extrusion ext, string plane, string startPlane, string endPlane)
    {
        // for vertical sketch planes the extent direction is horizontal: use the matching elevation view
        LockFaceTo(c, ext, plane, startPlane, false);
        LockFaceTo(c, ext, plane, endPlane, true);
    }

    static void LockFaceTo(Ctx2 c, Extrusion ext, string plane, string planeName, bool end)
    {
        var fd = c.Fd;
        try
        {
            string pn = planeName;
            var nrm = FamUtil.Normal(plane);
            if (pn == "@0")
            {
                string axis = Math.Abs(nrm.X) > 0.5 ? "x" : Math.Abs(nrm.Y) > 0.5 ? "y" : "z";
                pn = BasePlane(fd, axis);
            }
            Reference planeRef = pn.StartsWith("#L:") ? FamLevel(fd, pn.Substring(3)).GetPlaneReference() : FamUtil.RefPlane(fd, pn).GetReference();
            // view where both the plane (a line) and the face (an edge) are visible
            View view = plane == "xy" ? FamUtil.Elevation(fd, "Front", "Back", "Right", "Left") : plane == "xz" ? FamUtil.PlanView(fd) : FamUtil.PlanView(fd);
            if (view == null) return;
            var bb = ext.get_BoundingBox(null);
            double target = end ? (bb.Max.X * nrm.X + bb.Max.Y * nrm.Y + bb.Max.Z * nrm.Z) : (bb.Min.X * nrm.X + bb.Min.Y * nrm.Y + bb.Min.Z * nrm.Z);
            var pt = (bb.Min + bb.Max) / 2;
            pt = pt - nrm * pt.DotProduct(nrm) + nrm * target;
            var face = Geo.NearestPlanarFace(ext, pt, end ? nrm : -nrm);
            if (face == null) return;
            fd.FamilyCreate.NewAlignment(view, planeRef, face.Reference);
        }
        catch (Exception ex) { c.Notes.Add($"lock face to {planeName}: {ex.Message}"); }
    }
}
