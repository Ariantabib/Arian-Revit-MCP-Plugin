using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Core;

/// <summary>Parameter reading / writing with automatic unit conversion (mm, deg, m2, m3).</summary>
public static class Par
{
    public static Parameter Get(Element e, string key)
    {
        if (e == null || string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        Parameter p = null;
        try { p = e.LookupParameter(key); } catch { }
        if (p != null) return p;
        if (key.StartsWith("BuiltIn:", StringComparison.OrdinalIgnoreCase)) key = key.Substring(8);
        if (key.Contains('_') && Enum.TryParse<BuiltInParameter>(key, true, out var bip) && Enum.IsDefined(typeof(BuiltInParameter), bip))
        {
            try { p = e.get_Parameter(bip); } catch { }
            if (p != null) return p;
        }
        if (Guid.TryParse(key, out var g)) { try { p = e.get_Parameter(g); } catch { } if (p != null) return p; }
        // fall back to the element's type
        if (e is not ElementType)
        {
            var t = e.Document.GetElement(e.GetTypeId());
            if (t != null && t.Id != e.Id) return Get(t, key);
        }
        return null;
    }

    static ForgeTypeId Spec(Parameter p)
    {
        try { return p.Definition.GetDataType(); } catch { return null; }
    }

    static bool IsYesNo(ForgeTypeId s) => s != null && s == SpecTypeId.Boolean.YesNo;

    public static JsonNode Value(Parameter p)
    {
        if (p == null || !p.HasValue) return null;
        var spec = Spec(p);
        switch (p.StorageType)
        {
            case StorageType.Double:
                {
                    double v = p.AsDouble();
                    if (spec != null && (spec == SpecTypeId.Length || spec == SpecTypeId.Angle || spec == SpecTypeId.Area || spec == SpecTypeId.Volume))
                        return JsonValue.Create(U.R(U.ToApi(v, spec), 4));
                    if (spec != null && UnitUtils.IsMeasurableSpec(spec))
                    {
                        try { return JsonValue.Create(U.R(UnitUtils.ConvertFromInternalUnits(v, p.GetUnitTypeId()), 4)); } catch { }
                    }
                    return JsonValue.Create(U.R(v, 6));
                }
            case StorageType.Integer:
                return IsYesNo(spec) ? JsonValue.Create(p.AsInteger() != 0) : JsonValue.Create(p.AsInteger());
            case StorageType.String:
                return JsonValue.Create(p.AsString());
            case StorageType.ElementId:
                {
                    var id = p.AsElementId();
                    if (id == null || id == ElementId.InvalidElementId) return null;
                    var el = p.Element.Document.GetElement(id);
                    return new JsonObject { ["id"] = id.Value, ["name"] = el?.Name };
                }
        }
        return null;
    }

    public static string UnitLabel(Parameter p)
    {
        var spec = Spec(p);
        if (p.StorageType != StorageType.Double || spec == null) return null;
        if (spec == SpecTypeId.Length) return "mm";
        if (spec == SpecTypeId.Angle) return "deg";
        if (spec == SpecTypeId.Area) return "m2";
        if (spec == SpecTypeId.Volume) return "m3";
        try { return LabelUtils.GetLabelForUnit(p.GetUnitTypeId()); } catch { return null; }
    }

    public static JsonObject Describe(Parameter p)
    {
        var def = p.Definition;
        var o = new JsonObject
        {
            ["name"] = def.Name,
            ["value"] = Value(p),
            ["storage"] = p.StorageType.ToString().ToLowerInvariant(),
            ["read_only"] = p.IsReadOnly,
        };
        try { var disp = p.AsValueString(); if (!string.IsNullOrEmpty(disp) && p.StorageType != StorageType.String) o["display"] = disp; } catch { }
        var unit = UnitLabel(p); if (unit != null) o["unit"] = unit;
        if (p.IsShared) o["shared_guid"] = p.GUID.ToString();
        if (def is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID) o["builtin"] = idf.BuiltInParameter.ToString();
        try { o["group"] = LabelUtils.GetLabelForGroup(def.GetGroupTypeId()); } catch { }
        return o;
    }

    /// <summary>All parameters as { name: value } (set includeEmpty to include null values).</summary>
    public static JsonObject Values(Element e, bool includeEmpty = false)
    {
        var o = new JsonObject();
        foreach (Parameter p in e.Parameters)
        {
            string n = p.Definition?.Name; if (string.IsNullOrEmpty(n)) continue;
            JsonNode v = null;
            try { v = Value(p); } catch { }
            if (v == null && !includeEmpty) continue;
            if (o.ContainsKey(n)) continue;
            o[n] = v;
        }
        return o;
    }

    public static JsonArray DescribeAll(Element e)
    {
        var arr = new JsonArray();
        var seen = new HashSet<string>();
        foreach (Parameter p in e.Parameters)
        {
            string n = p.Definition?.Name; if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
            try { arr.Add(Describe(p)); } catch { }
        }
        return arr;
    }

    /// <summary>Sets a parameter from a JSON value. Returns null on success or an error message.</summary>
    public static string Set(Parameter p, JsonNode v)
    {
        if (p == null) return "parameter not found";
        if (p.IsReadOnly) return "parameter is read-only";
        var doc = p.Element.Document;
        var spec = Spec(p);
        try
        {
            switch (p.StorageType)
            {
                case StorageType.Double:
                    {
                        if (v is JsonValue jv && jv.TryGetValue<string>(out var s))
                            return p.SetValueString(s) ? null : $"could not parse '{s}' for this parameter";
                        double d = v.GetValue<double>();
                        if (spec != null && (spec == SpecTypeId.Length || spec == SpecTypeId.Angle || spec == SpecTypeId.Area || spec == SpecTypeId.Volume))
                            d = U.FromApi(d, spec);
                        else if (spec != null && UnitUtils.IsMeasurableSpec(spec))
                        {
                            try { d = UnitUtils.ConvertToInternalUnits(d, p.GetUnitTypeId()); } catch { }
                        }
                        return p.Set(d) ? null : "Revit rejected the value";
                    }
                case StorageType.Integer:
                    {
                        if (v is JsonValue jb && jb.TryGetValue<bool>(out var b)) return p.Set(b ? 1 : 0) ? null : "Revit rejected the value";
                        if (v is JsonValue js && js.TryGetValue<string>(out var s))
                        {
                            if (int.TryParse(s, out var iv)) return p.Set(iv) ? null : "Revit rejected the value";
                            if (s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return p.Set(1) ? null : "rejected";
                            if (s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return p.Set(0) ? null : "rejected";
                            return p.SetValueString(s) ? null : $"could not parse '{s}'";
                        }
                        return p.Set(v.GetValue<int>()) ? null : "Revit rejected the value";
                    }
                case StorageType.String:
                    return p.Set(v is JsonValue sv && sv.TryGetValue<string>(out var str) ? str : v.ToJsonString().Trim('"')) ? null : "Revit rejected the value";
                case StorageType.ElementId:
                    {
                        long idv;
                        if (v is JsonValue nv && nv.TryGetValue<long>(out idv)) return p.Set(new ElementId(idv)) ? null : "Revit rejected the value";
                        string name = v.GetValue<string>();
                        if (long.TryParse(name, out idv)) return p.Set(new ElementId(idv)) ? null : "Revit rejected the value";
                        var el = ResolveByName(doc, name, spec);
                        if (el == null) return $"could not find an element named '{name}' for this parameter";
                        return p.Set(el.Id) ? null : "Revit rejected the value";
                    }
            }
        }
        catch (Exception ex) { return ex.Message; }
        return "unsupported storage type";
    }

    static Element ResolveByName(Document doc, string name, ForgeTypeId spec)
    {
        IEnumerable<Element> pool;
        if (spec != null && spec == SpecTypeId.Reference.Material) pool = new FilteredElementCollector(doc).OfClass(typeof(Material));
        else if (spec != null && spec == SpecTypeId.Reference.LoadClassification) pool = Enumerable.Empty<Element>();
        else
        {
            pool = new FilteredElementCollector(doc).OfClass(typeof(Material)).ToElements()
                .Concat(new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElements())
                .Concat(new FilteredElementCollector(doc).OfClass(typeof(Phase)).ToElements())
                .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElements());
        }
        return pool.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Applies a dictionary of parameter values to an element, returning per-parameter errors.</summary>
    public static JsonObject SetMany(Element e, Dictionary<string, JsonNode> values)
    {
        var errors = new JsonObject();
        if (values == null) return errors;
        foreach (var kv in values)
        {
            var p = Get(e, kv.Key);
            if (p == null) { errors[kv.Key] = "parameter not found"; continue; }
            var err = Set(p, kv.Value);
            if (err != null) errors[kv.Key] = err;
        }
        return errors;
    }
}
