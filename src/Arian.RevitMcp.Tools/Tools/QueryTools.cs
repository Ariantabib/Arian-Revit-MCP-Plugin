using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Tools;

public sealed class ParamFilter
{
    [Desc("Parameter name (or BuiltInParameter name)")] public string Name;
    [Desc("equals | not_equals | contains | starts_with | greater | less | greater_or_equal | less_or_equal | empty | not_empty")] public string Op = "equals";
    [Desc("Value to compare (number in mm/deg/m2/m3 for dimensional parameters, string, or bool)")] public JsonNode Value;
}

public static class QueryTools
{
    // ---------------------------------------------------------------- overview
    [Tool("get_model_overview", """
        Bird's-eye summary of the active project: element counts per category, levels, grids, views, sheets, rooms, links, phases, warnings count. Call after opening a project to orient yourself.
        """, Category = "Query", ReadOnly = true)]
    public static object GetModelOverview()
    {
        var doc = Ctx.RequireDoc();
        var o = new JsonObject { ["title"] = doc.Title, ["kind"] = doc.IsFamilyDocument ? "family" : "project" };
        var counts = new Dictionary<string, int>();
        foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            var c = e.Category;
            if (c == null || c.CategoryType != CategoryType.Model) continue;
            counts[c.Name] = counts.TryGetValue(c.Name, out var n) ? n + 1 : 1;
        }
        var cats = new JsonObject();
        foreach (var kv in counts.OrderByDescending(k => k.Value).Take(60)) cats[kv.Key] = kv.Value;
        o["model_element_counts"] = cats;
        o["total_model_elements"] = counts.Values.Sum();
        if (doc.IsFamilyDocument)
        {
            var fm = doc.FamilyManager;
            o["family_category"] = doc.OwnerFamily.FamilyCategory?.Name;
            o["family_types"] = fm.Types.Size;
            o["family_parameters"] = fm.Parameters.Size;
            return o;
        }
        o["levels"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation)
            .Select(l => (JsonNode)new JsonObject { ["id"] = l.Id.Value, ["name"] = l.Name, ["elevation_mm"] = U.R(U.ToMm(l.Elevation), 1) }).ToArray());
        o["grids"] = new FilteredElementCollector(doc).OfClass(typeof(Grid)).GetElementCount();
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
        o["views"] = views.Count;
        o["view_types"] = new JsonObject(views.GroupBy(v => v.ViewType.ToString()).Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count())));
        o["sheets"] = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
        o["rooms"] = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount();
        o["links"] = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
        o["phases"] = new JsonArray(doc.Phases.Cast<Phase>().Select(p => (JsonNode)p.Name).ToArray());
        try { o["warnings"] = doc.GetWarnings().Count; } catch { }
        o["worksharing"] = doc.IsWorkshared;
        o["loaded_families"] = new FilteredElementCollector(doc).OfClass(typeof(Family)).GetElementCount();
        o["materials"] = new FilteredElementCollector(doc).OfClass(typeof(Material)).GetElementCount();
        o["active_view"] = doc.ActiveView?.Name;
        return o;
    }

    // ---------------------------------------------------------------- categories
    [Tool("list_categories", """
        Lists Revit categories with their BuiltInCategory names. Use the names with query_elements / other tools.
        @kind model | annotation | all
        @with_counts Include the number of instances in the project (slower).
        @name_contains Filter by name.
        """, Category = "Query", ReadOnly = true)]
    public static object ListCategories(string kind = "model", bool withCounts = false, string nameContains = null)
    {
        var doc = Ctx.RequireDoc();
        var arr = new JsonArray();
        foreach (Category c in doc.Settings.Categories)
        {
            if (kind == "model" && c.CategoryType != CategoryType.Model) continue;
            if (kind == "annotation" && c.CategoryType != CategoryType.Annotation) continue;
            if (!string.IsNullOrEmpty(nameContains) && !c.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) continue;
            string bic = null;
            try { bic = ((BuiltInCategory)(int)c.Id.Value).ToString(); } catch { }
            var o = new JsonObject { ["name"] = c.Name, ["builtin"] = bic, ["type"] = c.CategoryType.ToString().ToLowerInvariant() };
            if (withCounts)
            {
                try { o["count"] = new FilteredElementCollector(doc).OfCategoryId(c.Id).WhereElementIsNotElementType().GetElementCount(); } catch { }
            }
            arr.Add(o);
        }
        return new JsonObject { ["categories"] = arr };
    }

    // ---------------------------------------------------------------- query_elements
    [Tool("query_elements", """
        The main search tool. Finds elements by category / class / level / type / name / parameter values / location, with paging. Returns brief info (id, category, family, type, level) plus any parameters you request.
        Examples: all walls on Level 1 -> category="Walls", level="Level 1". Doors of a type -> category="Doors", type_name="M_Single-Flush". Elements with Mark containing "A" -> filters=[{name:"Mark",op:"contains",value:"A"}].
        @category Category name or BuiltInCategory (e.g. Walls, OST_Floors, Structural Columns).
        @class_name Revit class e.g. Wall, FamilyInstance, Floor, Room, Duct, Pipe, Level, Grid.
        @level Level name or id.
        @type_name Type name / 'Family: Type' / substring.
        @name_contains Substring of the element name.
        @view View name/id: only elements visible in that view.
        @ids Restrict to these element ids.
        @filters Parameter filters (all must match).
        @bbox_min Search box minimum corner [x,y,z] mm; use with bbox_max (elements intersecting the box).
        @bbox_max Search box maximum corner [x,y,z] mm.
        @types If true, return element TYPES (e.g. wall types) instead of instances.
        @parameters Names of parameters to include in each result.
        @detail Include full detail (location, bbox, all parameter values) per element - heavy.
        @limit Max results (default 100, max 1000).
        @offset Skip this many results (paging).
        """, Category = "Query", ReadOnly = true)]
    public static object QueryElements(string category = null, string className = null, string level = null, string typeName = null,
        string nameContains = null, string view = null, long[] ids = null, ParamFilter[] filters = null,
        double[] bboxMin = null, double[] bboxMax = null, bool types = false, string[] parameters = null,
        bool detail = false, int limit = 100, int offset = 0)
    {
        var doc = Ctx.RequireDoc();
        limit = Math.Clamp(limit, 1, 1000);
        FilteredElementCollector col;
        if (!string.IsNullOrWhiteSpace(view)) col = new FilteredElementCollector(doc, Find.View(doc, view).Id);
        else if (ids != null && ids.Length > 0) col = new FilteredElementCollector(doc, Find.Ids(ids));
        else col = new FilteredElementCollector(doc);

        if (types) col = col.WhereElementIsElementType(); else col = col.WhereElementIsNotElementType();
        if (!string.IsNullOrWhiteSpace(category)) col = col.OfCategoryId(Find.Category(doc, category).Id);
        if (!string.IsNullOrWhiteSpace(className)) col = col.OfClass(Find.RevitClass(className));
        if (bboxMin != null && bboxMax != null)
        {
            var ol = new Outline(U.Pt(bboxMin), U.Pt(bboxMax));
            col = col.WherePasses(new BoundingBoxIntersectsFilter(ol));
        }

        IEnumerable<Element> q = col;
        if (ids != null && ids.Length > 0 && string.IsNullOrWhiteSpace(view)) { var set = new HashSet<long>(ids); q = q.Where(e => set.Contains(e.Id.Value)); }
        Level lv = string.IsNullOrWhiteSpace(level) ? null : Find.Level(doc, level);
        if (lv != null) q = q.Where(e => Dto.LevelOf(e)?.Id == lv.Id);
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var tn = typeName.Trim();
            q = q.Where(e =>
            {
                var t = (e as ElementType) ?? doc.GetElement(e.GetTypeId()) as ElementType;
                if (t == null) return false;
                var full = (t.FamilyName ?? "") + ": " + t.Name;
                return t.Name.Equals(tn, StringComparison.OrdinalIgnoreCase) || full.Equals(tn, StringComparison.OrdinalIgnoreCase)
                       || full.Contains(tn, StringComparison.OrdinalIgnoreCase);
            });
        }
        if (!string.IsNullOrWhiteSpace(nameContains))
            q = q.Where(e => (Dto.SafeName(e) ?? "").Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        if (filters != null)
            foreach (var f in filters) { var ff = f; q = q.Where(e => Match(e, ff)); }

        var list = q.ToList();
        var page = list.Skip(offset).Take(limit).ToList();
        var arr = new JsonArray();
        foreach (var e in page)
        {
            var o = detail ? Dto.Detail(e) : Dto.Brief(e);
            if (parameters != null && parameters.Length > 0 && !detail)
            {
                var pv = new JsonObject();
                foreach (var pn in parameters)
                {
                    var p = Par.Get(e, pn);
                    pv[pn] = p == null ? null : Par.Value(p);
                }
                o["parameters"] = pv;
            }
            arr.Add(o);
        }
        return new JsonObject { ["total"] = list.Count, ["offset"] = offset, ["count"] = arr.Count, ["elements"] = arr };
    }

    static bool Match(Element e, ParamFilter f)
    {
        var p = Par.Get(e, f.Name);
        var op = (f.Op ?? "equals").ToLowerInvariant();
        if (op == "empty") return p == null || !p.HasValue || string.IsNullOrEmpty(p.AsValueString() ?? p.AsString());
        if (op == "not_empty") return p != null && p.HasValue && !string.IsNullOrEmpty(p.AsValueString() ?? p.AsString());
        if (p == null || !p.HasValue) return false;
        var v = Par.Value(p);
        if (v == null) return false;
        // element-id / object values compare by name or id
        string vs = v is JsonObject vo ? (vo["name"]?.ToString() ?? "") : v.ToString();
        string target = f.Value?.ToString() ?? "";
        if (f.Value is JsonValue jv && jv.TryGetValue<string>(out var s)) target = s;
        bool numeric = double.TryParse(vs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vn)
                       & double.TryParse(target, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tn);
        switch (op)
        {
            case "equals": case "eq": case "==":
                if (v is JsonObject vo2) return (vo2["name"]?.ToString() ?? "").Equals(target, StringComparison.OrdinalIgnoreCase) || vo2["id"]?.ToString() == target;
                if (v is JsonValue bv && bv.TryGetValue<bool>(out var b)) return b == (target.Equals("true", StringComparison.OrdinalIgnoreCase) || target == "1" || target.Equals("yes", StringComparison.OrdinalIgnoreCase));
                return numeric ? Math.Abs(vn - tn) < 0.01 : vs.Equals(target, StringComparison.OrdinalIgnoreCase);
            case "not_equals": case "ne": case "!=":
                return numeric ? Math.Abs(vn - tn) >= 0.01 : !vs.Equals(target, StringComparison.OrdinalIgnoreCase);
            case "contains": return vs.Contains(target, StringComparison.OrdinalIgnoreCase);
            case "starts_with": return vs.StartsWith(target, StringComparison.OrdinalIgnoreCase);
            case "greater": case "gt": case ">": return numeric && vn > tn;
            case "less": case "lt": case "<": return numeric && vn < tn;
            case "greater_or_equal": case "gte": case ">=": return numeric && vn >= tn;
            case "less_or_equal": case "lte": case "<=": return numeric && vn <= tn;
        }
        throw new ToolException($"Unknown filter op '{f.Op}'.");
    }

    // ---------------------------------------------------------------- single elements
    [Tool("get_element", """
        Full detail of one element: category/family/type/level, location, bounding box, host, phase, and ALL parameters (values in mm / m2 / m3 / deg).
        @id Element id.
        @describe_parameters Return rich parameter descriptions (storage type, read-only, units, shared GUID, built-in name) instead of a plain name->value map.
        @include_type Also include the element type's parameters.
        """, Category = "Query", ReadOnly = true)]
    public static object GetElement(long id, bool describeParameters = false, bool includeType = false)
    {
        var doc = Ctx.RequireDoc();
        var e = Find.Element(doc, id);
        var o = Dto.Detail(e, !describeParameters);
        if (describeParameters) o["parameters"] = Par.DescribeAll(e);
        if (includeType && e is not ElementType)
        {
            var t = doc.GetElement(e.GetTypeId());
            if (t != null) o["type_parameters"] = describeParameters ? Par.DescribeAll(t) : Par.Values(t);
            if (t != null) o["type_id"] = t.Id.Value;
        }
        return o;
    }

    [Tool("get_elements", "Brief or detailed info for several element ids at once.", Category = "Query", ReadOnly = true)]
    public static object GetElements(long[] ids, bool detail = false)
    {
        var doc = Ctx.RequireDoc();
        var arr = new JsonArray();
        foreach (var i in ids)
        {
            var e = doc.GetElement(new ElementId(i));
            if (e == null) { arr.Add(new JsonObject { ["id"] = i, ["error"] = "not found" }); continue; }
            arr.Add(detail ? Dto.Detail(e) : Dto.Brief(e));
        }
        return new JsonObject { ["elements"] = arr };
    }

    [Tool("get_parameters", """
        Reads parameters of an element (instance + falls back to type). Pass names to read only those; otherwise all non-empty parameters are returned.
        @id Element id.
        @names Parameter names or BuiltInParameter names.
        """, Category = "Query", ReadOnly = true)]
    public static object GetParameters(long id, string[] names = null)
    {
        var doc = Ctx.RequireDoc();
        var e = Find.Element(doc, id);
        if (names == null || names.Length == 0) return new JsonObject { ["id"] = id, ["parameters"] = Par.DescribeAll(e) };
        var o = new JsonObject();
        foreach (var n in names)
        {
            var p = Par.Get(e, n);
            o[n] = p == null ? new JsonObject { ["error"] = "not found" } : Par.Describe(p);
        }
        return new JsonObject { ["id"] = id, ["parameters"] = o };
    }

    [Tool("set_parameters", """
        Writes parameters on one element. Numbers for dimensional parameters are mm / deg / m2 / m3; strings are parsed by Revit using project units (e.g. "1500 mm"); booleans for Yes/No; ElementId parameters accept an id or a name (materials, levels).
        @id Element id.
        @values Map of parameter name -> value.
        """, Category = "Modify")]
    public static object SetParameters(long id, Dictionary<string, JsonNode> values)
    {
        var doc = Ctx.RequireDoc();
        var e = Find.Element(doc, id);
        var errs = Par.SetMany(e, values);
        doc.Regenerate();
        return new JsonObject { ["id"] = id, ["errors"] = errs, ["ok"] = errs.Count == 0 };
    }

    public sealed class ParamUpdate
    {
        public long Id;
        public Dictionary<string, JsonNode> Values;
    }

    [Tool("set_parameters_bulk", "Writes parameters on many elements: [{id, values:{name:value}}...]. Use for numbering, marks, comments, type changes at scale.",
        Category = "Modify")]
    public static object SetParametersBulk(ParamUpdate[] updates)
    {
        var doc = Ctx.RequireDoc();
        var errors = new JsonArray();
        int ok = 0;
        foreach (var u in updates)
        {
            var e = doc.GetElement(new ElementId(u.Id));
            if (e == null) { errors.Add(new JsonObject { ["id"] = u.Id, ["error"] = "not found" }); continue; }
            var errs = Par.SetMany(e, u.Values);
            if (errs.Count > 0) errors.Add(new JsonObject { ["id"] = u.Id, ["errors"] = errs }); else ok++;
        }
        doc.Regenerate();
        return new JsonObject { ["updated"] = ok, ["failed"] = errors.Count, ["errors"] = errors };
    }

    [Tool("set_parameter_on_elements", """
        Sets ONE parameter to the same value on many elements.
        @ids Element ids.
        @name Parameter name.
        @value Value to set.
        """, Category = "Modify")]
    public static object SetParameterOnElements(long[] ids, string name, JsonNode value)
    {
        var doc = Ctx.RequireDoc();
        int ok = 0; var errs = new JsonArray();
        foreach (var i in ids)
        {
            var e = doc.GetElement(new ElementId(i));
            var p = Par.Get(e, name);
            if (p == null) { errs.Add(new JsonObject { ["id"] = i, ["error"] = "parameter not found" }); continue; }
            var err = Par.Set(p, value);
            if (err != null) errs.Add(new JsonObject { ["id"] = i, ["error"] = err }); else ok++;
        }
        doc.Regenerate();
        return new JsonObject { ["updated"] = ok, ["errors"] = errs };
    }

    // ---------------------------------------------------------------- lists
    [Tool("list_levels", "All levels sorted by elevation (mm).", Category = "Query", ReadOnly = true)]
    public static object ListLevels()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
        {
            var o = new JsonObject { ["id"] = l.Id.Value, ["name"] = l.Name, ["elevation_mm"] = U.R(U.ToMm(l.Elevation), 1) };
            try { o["building_story"] = l.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY)?.AsInteger() == 1; } catch { }
            arr.Add(o);
        }
        return new JsonObject { ["levels"] = arr };
    }

    [Tool("list_grids", "All grids with their geometry (mm).", Category = "Query", ReadOnly = true)]
    public static object ListGrids()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().OrderBy(g => g.Name))
        {
            var o = new JsonObject { ["id"] = g.Id.Value, ["name"] = g.Name, ["curve_type"] = g.Curve.GetType().Name };
            if (g.Curve.IsBound) { o["start"] = Dto.JsonArr(g.Curve.GetEndPoint(0)); o["end"] = Dto.JsonArr(g.Curve.GetEndPoint(1)); }
            arr.Add(o);
        }
        return new JsonObject { ["grids"] = arr };
    }

    [Tool("list_element_types", """
        Lists element TYPES of a category (wall types, floor types, door types, beam types, duct types...). For loadable families the type is a FamilySymbol ('Family: Type'). Includes useful type parameters (width, thickness, function...) when available.
        @category Category name/BuiltInCategory (Walls, Floors, Roofs, Ceilings, Doors, Windows, Structural Columns, Structural Framing, Duct Systems...).
        @name_contains Filter by name.
        @parameters Extra type-parameter names to include.
        @limit Max results.
        """, Category = "Query", ReadOnly = true)]
    public static object ListElementTypes(string category, string nameContains = null, string[] parameters = null, int limit = 300)
    {
        var doc = Ctx.RequireDoc();
        var cat = Find.Category(doc, category);
        var arr = new JsonArray();
        var catIds = new List<ElementId> { cat.Id };
        // truss TYPES live in OST_Truss while the loadable 'Structural Trusses' family category is OST_StructuralTruss
        if (cat.Id.Value == (long)BuiltInCategory.OST_StructuralTruss) catIds.Add(new ElementId(BuiltInCategory.OST_Truss));
        foreach (var t in catIds.SelectMany(ci => new FilteredElementCollector(doc).OfCategoryId(ci).WhereElementIsElementType().Cast<ElementType>())
                     .OrderBy(t => t.FamilyName).ThenBy(t => t.Name))
        {
            var full = (t.FamilyName ?? "") + ": " + t.Name;
            if (!string.IsNullOrEmpty(nameContains) && !full.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) continue;
            var o = new JsonObject { ["id"] = t.Id.Value, ["family"] = t.FamilyName, ["type"] = t.Name, ["class"] = t.GetType().Name };
            if (t is FamilySymbol fs) o["active"] = fs.IsActive;
            if (t is WallType wt) { o["width_mm"] = U.R(U.ToMm(wt.Width), 1); o["kind"] = wt.Kind.ToString(); o["function_layers"] = wt.GetCompoundStructure()?.LayerCount; }
            if (t is HostObjAttributes ha && t is not WallType) { try { o["thickness_mm"] = U.R(U.ToMm(ha.GetCompoundStructure().GetWidth()), 1); o["layers"] = ha.GetCompoundStructure().LayerCount; } catch { } }
            if (parameters != null)
            {
                var pv = new JsonObject();
                foreach (var pn in parameters) { var p = Par.Get(t, pn); pv[pn] = p == null ? null : Par.Value(p); }
                o["parameters"] = pv;
            }
            arr.Add(o);
            if (arr.Count >= limit) break;
        }
        return new JsonObject { ["category"] = cat.Name, ["count"] = arr.Count, ["types"] = arr };
    }

    [Tool("list_families", """
        Lists loaded families with their types. Use to find what can be placed with place_family_instance.
        @category Filter by category.
        @name_contains Filter by family name.
        @in_place Only in-place families (true) / only loadable (false) / all (null).
        @include_types Include the list of type names.
        @limit Max families.
        """, Category = "Family", ReadOnly = true)]
    public static object ListFamilies(string category = null, string nameContains = null, bool? inPlace = null, bool includeTypes = true, int limit = 300)
    {
        var doc = Ctx.RequireDoc();
        ElementId catId = string.IsNullOrWhiteSpace(category) ? null : Find.Category(doc, category).Id;
        var arr = new JsonArray();
        foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().OrderBy(f => f.Name))
        {
            if (catId != null && f.FamilyCategory?.Id != catId) continue;
            if (!string.IsNullOrEmpty(nameContains) && !f.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) continue;
            if (inPlace.HasValue && f.IsInPlace != inPlace.Value) continue;
            var o = new JsonObject { ["id"] = f.Id.Value, ["name"] = f.Name, ["category"] = f.FamilyCategory?.Name, ["in_place"] = f.IsInPlace, ["editable"] = f.IsEditable };
            if (includeTypes)
            {
                var ts = new JsonArray();
                foreach (var sid in f.GetFamilySymbolIds())
                {
                    var s = doc.GetElement(sid) as FamilySymbol;
                    if (s != null) ts.Add(new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["active"] = s.IsActive });
                }
                o["types"] = ts;
            }
            arr.Add(o);
            if (arr.Count >= limit) break;
        }
        return new JsonObject { ["count"] = arr.Count, ["families"] = arr };
    }

    [Tool("list_views", """
        Lists views.
        @view_type Filter: FloorPlan, CeilingPlan, Elevation, Section, ThreeD, Detail, DraftingView, Legend, Schedule, AreaPlan, EngineeringPlan, Rendering...
        @name_contains Filter by name.
        @include_templates Include view templates.
        """, Category = "View", ReadOnly = true)]
    public static object ListViews(string viewType = null, string nameContains = null, bool includeTemplates = false)
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name))
        {
            if (v.IsTemplate && !includeTemplates) continue;
            if (v.ViewType == ViewType.ProjectBrowser || v.ViewType == ViewType.SystemBrowser || v.ViewType == ViewType.Internal) continue;
            if (!string.IsNullOrEmpty(viewType) && !v.ViewType.ToString().Equals(viewType, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(nameContains) && !v.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) continue;
            var o = new JsonObject { ["id"] = v.Id.Value, ["name"] = v.Name, ["type"] = v.ViewType.ToString(), ["is_template"] = v.IsTemplate };
            try { if (v.Scale > 0) o["scale"] = v.Scale; } catch { }
            try { if (v is ViewPlan vp && vp.GenLevel != null) o["level"] = vp.GenLevel.Name; } catch { }
            try { var tpl = doc.GetElement(v.ViewTemplateId); if (tpl != null) o["template"] = tpl.Name; } catch { }
            arr.Add(o);
        }
        return new JsonObject { ["count"] = arr.Count, ["views"] = arr };
    }

    [Tool("list_view_templates", "Lists view templates.", Category = "View", ReadOnly = true)]
    public static object ListViewTemplates()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate)
            .OrderBy(v => v.Name).Select(v => (JsonNode)new JsonObject { ["id"] = v.Id.Value, ["name"] = v.Name, ["type"] = v.ViewType.ToString() }).ToArray());
        return new JsonObject { ["templates"] = arr };
    }

    [Tool("list_sheets", "Lists sheets with their placed views.", Category = "View", ReadOnly = true)]
    public static object ListSheets()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().OrderBy(s => s.SheetNumber))
        {
            var vps = new JsonArray();
            foreach (var vid in s.GetAllPlacedViews())
            {
                var v = doc.GetElement(vid) as View;
                if (v != null) vps.Add(new JsonObject { ["id"] = v.Id.Value, ["name"] = v.Name });
            }
            arr.Add(new JsonObject { ["id"] = s.Id.Value, ["number"] = s.SheetNumber, ["name"] = s.Name, ["views"] = vps });
        }
        return new JsonObject { ["sheets"] = arr };
    }

    // ---------------------------------------------------------------- selection
    [Tool("get_selection", "Elements currently selected in the Revit UI.", Category = "Query", ReadOnly = true)]
    public static object GetSelection()
    {
        var ui = Ctx.UiDoc ?? throw new ToolException("No active UI document.");
        var arr = new JsonArray();
        foreach (var id in ui.Selection.GetElementIds()) { var e = ui.Document.GetElement(id); if (e != null) arr.Add(Dto.Brief(e)); }
        return new JsonObject { ["count"] = arr.Count, ["elements"] = arr };
    }

    [Tool("select_elements", "Selects elements in the Revit UI (and optionally zooms to them).", Category = "Query", ReadOnly = true)]
    public static object SelectElements(long[] ids, bool zoom = false)
    {
        var ui = Ctx.UiDoc ?? throw new ToolException("No active UI document.");
        var list = Find.Ids(ids);
        ui.Selection.SetElementIds(list);
        if (zoom && list.Count > 0) ui.ShowElements(list);
        return new JsonObject { ["selected"] = list.Count };
    }

    [Tool("zoom_to_elements", "Zooms the active view to the given elements.", Category = "View", ReadOnly = true)]
    public static object ZoomToElements(long[] ids)
    {
        var ui = Ctx.UiDoc ?? throw new ToolException("No active UI document.");
        ui.ShowElements(Find.Ids(ids));
        return new JsonObject { ["ok"] = true };
    }

    // ---------------------------------------------------------------- geometry & spatial
    [Tool("get_element_geometry", """
        Geometry summary of an element: bounding box, solid count, total volume (m3), surface area (m2), face/edge counts, planar face normals, plus the location curve/point. Use to verify what was built.
        @id Element id.
        @detail_level coarse | medium | fine
        """, Category = "Query", ReadOnly = true)]
    public static object GetElementGeometry(long id, string detailLevel = "medium")
    {
        var doc = Ctx.RequireDoc();
        var e = Find.Element(doc, id);
        var opts = new Options { ComputeReferences = false, DetailLevel = detailLevel == "fine" ? ViewDetailLevel.Fine : detailLevel == "coarse" ? ViewDetailLevel.Coarse : ViewDetailLevel.Medium };
        var ge = e.get_Geometry(opts);
        int solids = 0, faces = 0, edges = 0; double vol = 0, area = 0; var curves = new JsonArray();
        void Walk(GeometryElement g)
        {
            foreach (var go in g)
            {
                if (go is Solid s && s.Volume > 1e-9) { solids++; vol += s.Volume; area += s.SurfaceArea; faces += s.Faces.Size; edges += s.Edges.Size; }
                else if (go is GeometryInstance gi) Walk(gi.GetInstanceGeometry());
                else if (go is Curve c && curves.Count < 20)
                    curves.Add(new JsonObject { ["type"] = c.GetType().Name, ["length_mm"] = U.R(U.ToMm(c.Length), 1), ["start"] = Dto.JsonArr(c.GetEndPoint(0)), ["end"] = Dto.JsonArr(c.GetEndPoint(1)) });
            }
        }
        if (ge != null) Walk(ge);
        var o = new JsonObject
        {
            ["id"] = id,
            ["solids"] = solids,
            ["volume_m3"] = U.R(U.ToM3(vol), 4),
            ["surface_area_m2"] = U.R(U.ToM2(area), 3),
            ["faces"] = faces,
            ["edges"] = edges,
            ["bbox_mm"] = Dto.BBox(e.get_BoundingBox(null)),
        };
        if (curves.Count > 0) o["curves"] = curves;
        return o;
    }

    [Tool("find_elements_near", """
        Finds elements within a radius of a point (uses bounding-box proximity).
        @point [x,y,z] mm.
        @radius_mm Search radius.
        @categories Restrict to these categories.
        @limit Max results.
        """, Category = "Query", ReadOnly = true)]
    public static object FindElementsNear(double[] point, double radiusMm = 1000, string[] categories = null, int limit = 50)
    {
        var doc = Ctx.RequireDoc();
        var p = U.Pt(point);
        double r = U.Mm(radiusMm);
        var ol = new Outline(p - new XYZ(r, r, r), p + new XYZ(r, r, r));
        var col = new FilteredElementCollector(doc).WherePasses(new BoundingBoxIntersectsFilter(ol)).WhereElementIsNotElementType();
        if (categories != null && categories.Length > 0)
        {
            var ids = categories.Select(c => Find.Category(doc, c).Id).ToList();
            col = col.WherePasses(new ElementMulticategoryFilter(ids.Select(i => (BuiltInCategory)(int)i.Value).ToList()));
        }
        var arr = new JsonArray();
        foreach (var e in col.OrderBy(e => { var bb = e.get_BoundingBox(null); return bb == null ? 1e9 : ((bb.Min + bb.Max) / 2 - p).GetLength(); }).Take(limit))
            arr.Add(Dto.Brief(e));
        return new JsonObject { ["count"] = arr.Count, ["elements"] = arr };
    }

    [Tool("find_clashes", """
        Clash detection: finds pairs of elements from set A that geometrically intersect elements from set B (e.g. ducts vs structural framing). Sets are defined by category, or explicit ids.
        @category_a Category of set A.  @category_b Category of set B.
        @ids_a Explicit ids for set A (overrides category_a).
        @limit Max pairs.
        """, Category = "Query", ReadOnly = true)]
    public static object FindClashes(string categoryA = null, string categoryB = null, long[] idsA = null, int limit = 100)
    {
        var doc = Ctx.RequireDoc();
        IEnumerable<Element> A = idsA != null && idsA.Length > 0 ? Find.Elements(doc, idsA)
            : new FilteredElementCollector(doc).OfCategoryId(Find.Category(doc, categoryA).Id).WhereElementIsNotElementType().ToList();
        var catB = Find.Category(doc, categoryB);
        var pairs = new JsonArray();
        int total = 0;
        foreach (var a in A)
        {
            if (a.get_BoundingBox(null) == null) continue;
            ICollection<ElementId> hits;
            try
            {
                hits = new FilteredElementCollector(doc).OfCategoryId(catB.Id).WhereElementIsNotElementType()
                    .WherePasses(new ElementIntersectsElementFilter(a)).ToElementIds();
            }
            catch { continue; }
            foreach (var h in hits)
            {
                if (h == a.Id) continue;
                total++;
                if (pairs.Count < limit)
                {
                    var b = doc.GetElement(h);
                    pairs.Add(new JsonObject { ["a"] = Dto.Brief(a), ["b"] = Dto.Brief(b) });
                }
            }
        }
        return new JsonObject { ["clash_count"] = total, ["pairs"] = pairs };
    }

    // ---------------------------------------------------------------- rooms
    [Tool("list_rooms", """
        Lists rooms (or MEP spaces / areas) with number, name, level, area, volume and department.
        @kind room | space | area
        @level Optional level filter.
        """, Category = "Query", ReadOnly = true)]
    public static object ListRooms(string kind = "room", string level = null)
    {
        var doc = Ctx.RequireProject();
        var bic = kind switch { "space" => BuiltInCategory.OST_MEPSpaces, "area" => BuiltInCategory.OST_Areas, _ => BuiltInCategory.OST_Rooms };
        Level lv = string.IsNullOrWhiteSpace(level) ? null : Find.Level(doc, level);
        var arr = new JsonArray();
        foreach (var e in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType())
        {
            if (e is SpatialElement se)
            {
                if (lv != null && se.LevelId != lv.Id) continue;
                var o = new JsonObject
                {
                    ["id"] = e.Id.Value,
                    ["number"] = e.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(),
                    ["name"] = e.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(),
                    ["level"] = doc.GetElement(se.LevelId)?.Name,
                    ["placed"] = se.Location != null,
                    ["area_m2"] = U.R(U.ToM2(se.Area), 2),
                    ["department"] = e.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString(),
                };
                if (se is Room rm) { o["volume_m3"] = U.R(U.ToM3(rm.Volume), 2); o["enclosed"] = se.Area > 0; }
                arr.Add(o);
            }
        }
        return new JsonObject { ["count"] = arr.Count, ["items"] = arr };
    }
}
