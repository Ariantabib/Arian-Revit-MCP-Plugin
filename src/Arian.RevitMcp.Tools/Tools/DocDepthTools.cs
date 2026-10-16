using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-7: view templates & filters, sheet automation, smart dimensions/tags, schedule export with formulas, Persian/Jalali support, Iranian building-rule checks, CAD/PDF standards.</summary>
public static class DocDepthTools
{
    static double R(double v, int d = 1) => Math.Round(v, d);

    // ====================================================================== view templates & filters
    [Tool("list_view_templates", "Lists view templates (name, view type, scale, discipline) and how many views use each.", Category = "View", ReadOnly = true)]
    public static object ListViewTemplates()
    {
        var doc = Ctx.RequireProject();
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var arr = new JsonArray(views.Where(v => v.IsTemplate).Select(t => (JsonNode)new JsonObject
        {
            ["id"] = t.Id.Value, ["name"] = t.Name, ["view_type"] = t.ViewType.ToString(), ["scale"] = t.Scale, ["detail_level"] = t.DetailLevel.ToString(),
            ["used_by"] = views.Count(v => !v.IsTemplate && v.ViewTemplateId == t.Id)
        }).ToArray());
        return new JsonObject { ["count"] = arr.Count, ["templates"] = arr };
    }

    [Tool("create_view_template", """
        Creates a view template from an existing view's settings (graphics, visibility, filters, detail level), optionally changing scale / detail level / visual style.
        @name Template name. @from_view View to take the settings from. @scale e.g. 100 for 1:100. @detail_level coarse | medium | fine. @visual_style hidden_line | shaded | realistic | wireframe.
        """, Category = "View")]
    public static object CreateViewTemplate(string name, string fromView, int? scale = null, string detailLevel = null, string visualStyle = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, fromView);
        var t = v.CreateViewTemplate();
        t.Name = name;
        if (scale.HasValue) { try { t.Scale = scale.Value; } catch { } }
        if (!string.IsNullOrWhiteSpace(detailLevel)) t.DetailLevel = detailLevel.ToLowerInvariant() switch { "coarse" => ViewDetailLevel.Coarse, "fine" => ViewDetailLevel.Fine, _ => ViewDetailLevel.Medium };
        if (!string.IsNullOrWhiteSpace(visualStyle)) MaterialDepthTools.SetViewVisualStyle(visualStyle, t.Name);
        return new JsonObject { ["id"] = t.Id.Value, ["name"] = t.Name, ["view_type"] = t.ViewType.ToString() };
    }

    [Tool("apply_view_template", "Applies a view template to views. @views View names/ids. @template Template name. Use template='none' to detach.", Category = "View")]
    public static object ApplyViewTemplate(string[] views, string template)
    {
        var doc = Ctx.RequireProject();
        ElementId id = ElementId.InvalidElementId;
        if (!template.Equals("none", StringComparison.OrdinalIgnoreCase))
            id = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().FirstOrDefault(x => x.IsTemplate && x.Name.Equals(template, StringComparison.OrdinalIgnoreCase))?.Id ?? throw new ToolException("View template not found: " + template);
        int ok = 0; var errs = new JsonArray();
        foreach (var vn in views)
        {
            try { Find.View(doc, vn).ViewTemplateId = id; ok++; } catch (Exception ex) { errs.Add(vn + ": " + ex.Message); }
        }
        return new JsonObject { ["applied"] = ok, ["errors"] = errs };
    }

    static string ParamLabel(Document doc, ElementId id)
    {
        if (id.Value < 0) { try { return LabelUtils.GetLabelFor((BuiltInParameter)id.Value); } catch { return null; } }
        return doc.GetElement(id)?.Name;
    }

    [Tool("create_view_filter", """
        Creates a view filter (rule-based) for graphic overrides: elements of the given categories whose parameter matches a condition.
        @name Filter name. @categories Categories e.g. ['Walls','Doors']. @parameter Parameter name (e.g. 'Fire Rating', 'Type Name', 'Comments', 'Mark'). @operator equals | contains | begins_with | greater | less | not_equals (default equals).
        @value Text or number; numbers for length parameters are in mm. @is_length true when @value is a length in mm.
        """, Category = "View")]
    public static object CreateViewFilter(string name, string[] categories, string parameter, string value, string @operator = "equals", bool isLength = false)
    {
        var doc = Ctx.RequireProject();
        var cats = categories.Select(c => Find.Category(doc, c).Id).ToList();
        var pars = ParameterFilterUtilities.GetFilterableParametersInCommon(doc, cats);
        var pid = pars.FirstOrDefault(i => string.Equals(ParamLabel(doc, i), parameter, StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolException($"Parameter '{parameter}' is not filterable for these categories. Examples: {string.Join(", ", pars.Select(i => ParamLabel(doc, i)).Where(x => x != null).Take(25))}");
        bool numeric = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
        string op = (@operator ?? "equals").ToLowerInvariant();
        FilterRule rule;
        if (numeric && op is "equals" or "greater" or "less" or "not_equals" && (isLength || numeric))
        {
            double v = isLength ? U.Mm(num) : num;
            rule = op switch
            {
                "greater" => ParameterFilterRuleFactory.CreateGreaterRule(pid, v, 1e-6),
                "less" => ParameterFilterRuleFactory.CreateLessRule(pid, v, 1e-6),
                "not_equals" => ParameterFilterRuleFactory.CreateNotEqualsRule(pid, v, 1e-6),
                _ => ParameterFilterRuleFactory.CreateEqualsRule(pid, v, 1e-6),
            };
        }
        else
        {
            rule = op switch
            {
                "contains" => ParameterFilterRuleFactory.CreateContainsRule(pid, value),
                "begins_with" => ParameterFilterRuleFactory.CreateBeginsWithRule(pid, value),
                "not_equals" => ParameterFilterRuleFactory.CreateNotEqualsRule(pid, value),
                _ => ParameterFilterRuleFactory.CreateEqualsRule(pid, value),
            };
        }
        var f = ParameterFilterElement.Create(doc, name, cats, new ElementParameterFilter(rule));
        return new JsonObject { ["id"] = f.Id.Value, ["name"] = f.Name };
    }

    [Tool("apply_view_filter", """
        Adds a view filter to a view or view template with graphic overrides.
        @view View or template name. @filter Filter name. @visible Show (default true). @color {r,g,b} line + solid-fill colour. @line_weight 1-16. @transparency 0-100. @halftone bool. @solid_fill true to fill surfaces with the colour.
        """, Category = "View")]
    public static object ApplyViewFilter(string view, string filter, bool visible = true, MaterialTools.RgbColor color = null, int? lineWeight = null, int? transparency = null, bool halftone = false, bool solidFill = false)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var f = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().FirstOrDefault(x => x.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException("Filter not found: " + filter);
        if (!v.GetFilters().Contains(f.Id)) v.AddFilter(f.Id);
        v.SetFilterVisibility(f.Id, visible);
        var o = new OverrideGraphicSettings();
        if (color != null)
        {
            var c = new Color(color.R, color.G, color.B);
            o.SetProjectionLineColor(c); o.SetCutLineColor(c);
            if (solidFill)
            {
                var solid = FillPatternElement.GetFillPatternElementByName(doc, FillPatternTarget.Drafting, "<Solid fill>");
                if (solid != null) { o.SetSurfaceForegroundPatternId(solid.Id); o.SetSurfaceForegroundPatternColor(c); o.SetCutForegroundPatternId(solid.Id); o.SetCutForegroundPatternColor(c); }
            }
        }
        if (lineWeight.HasValue) { o.SetProjectionLineWeight(lineWeight.Value); o.SetCutLineWeight(lineWeight.Value); }
        if (transparency.HasValue) o.SetSurfaceTransparency(Math.Clamp(transparency.Value, 0, 100));
        o.SetHalftone(halftone);
        v.SetFilterOverrides(f.Id, o);
        return new JsonObject { ["view"] = v.Name, ["filter"] = f.Name, ["visible"] = visible };
    }

    // ====================================================================== sheets
    static (XYZ min, XYZ max) SheetArea(Document doc, ViewSheet sheet, double marginMm, double reserveRightMm, double reserveBottomMm)
    {
        var tb = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
        var bb = tb?.get_BoundingBox(sheet);
        XYZ lo = bb?.Min ?? new XYZ(0, 0, 0), hi = bb?.Max ?? new XYZ(U.Mm(420), U.Mm(297), 0);
        return (new XYZ(lo.X + U.Mm(marginMm), lo.Y + U.Mm(marginMm + reserveBottomMm), 0), new XYZ(hi.X - U.Mm(marginMm + reserveRightMm), hi.Y - U.Mm(marginMm), 0));
    }

    static JsonArray LayoutViewports(Document doc, ViewSheet sheet, double marginMm, double reserveRightMm, double reserveBottomMm, int? columns)
    {
        var (lo, hi) = SheetArea(doc, sheet, marginMm, reserveRightMm, reserveBottomMm);
        var vps = sheet.GetAllViewports().Select(i => doc.GetElement(i)).OfType<Viewport>().ToList();
        var notes = new JsonArray();
        if (vps.Count == 0) return notes;
        int cols = columns ?? (int)Math.Ceiling(Math.Sqrt(vps.Count)); int rows = (int)Math.Ceiling(vps.Count / (double)cols);
        double cw = (hi.X - lo.X) / cols, ch = (hi.Y - lo.Y) / rows;
        for (int i = 0; i < vps.Count; i++)
        {
            int c = i % cols, r = rows - 1 - i / cols;   // first view top-left
            var centre = new XYZ(lo.X + cw * (c + 0.5), lo.Y + ch * (r + 0.5), 0);
            var vp = vps[i]; vp.SetBoxCenter(centre);
            var ol = vp.GetBoxOutline();
            double w = ol.MaximumPoint.X - ol.MinimumPoint.X, h = ol.MaximumPoint.Y - ol.MinimumPoint.Y;
            if (w > cw * 1.001 || h > ch * 1.001) notes.Add($"'{doc.GetElement(vp.ViewId).Name}' ({U.ToMm(w):0}x{U.ToMm(h):0} mm) is larger than its cell ({U.ToMm(cw):0}x{U.ToMm(ch):0} mm) — use a smaller scale or fewer views per sheet.");
        }
        return notes;
    }

    [Tool("create_sheet_set", """
        Creates sheets for a list of views and lays the viewports out automatically inside the title-block area (grid, centred, no overlap checks beyond size warnings).
        @views View names/ids (plans, sections, schedules...). @titleblock Title block type (default: first). @number_prefix e.g. 'A-' @start_number First number (default 101) — numbers are prefix + counter. @per_sheet Views per sheet (default 1). @sheet_name Fixed sheet name (default: the view's name).
        @reserve_right_mm Space kept free for the title strip at the right (default 70). @margin_mm Border margin (default 15).
        """, Category = "View")]
    public static object CreateSheetSet(string[] views, string titleblock = null, string numberPrefix = "A-", int startNumber = 101, int perSheet = 1, string sheetName = null, double reserveRightMm = 70, double marginMm = 15)
    {
        var doc = Ctx.RequireProject();
        var res = new JsonArray(); int n = startNumber;
        for (int i = 0; i < views.Length; i += Math.Max(1, perSheet))
        {
            Activity.Check();
            var group = views.Skip(i).Take(Math.Max(1, perSheet)).ToList();
            string number = numberPrefix + n++;
            string name = sheetName ?? (group.Count == 1 ? Find.View(doc, group[0]).Name : "Sheet " + number);
            ViewTools.CreateSheet(number, name, titleblock);
            var sheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().First(s => s.SheetNumber == number);
            var errs = new JsonArray();
            foreach (var vn in group)
            {
                try
                {
                    var v = Find.View(doc, vn);
                    if (v is ViewSchedule vs) ScheduleSheetInstance.Create(doc, sheet.Id, vs.Id, XYZ.Zero);
                    else Viewport.Create(doc, sheet.Id, v.Id, XYZ.Zero);
                }
                catch (Exception ex) { errs.Add($"{vn}: {ex.Message}"); }
            }
            var notes = LayoutViewports(doc, sheet, marginMm, reserveRightMm, 0, null);
            res.Add(new JsonObject { ["sheet"] = number, ["name"] = name, ["views"] = group.Count - errs.Count, ["errors"] = errs, ["layout_notes"] = notes });
        }
        return new JsonObject { ["sheets"] = res.Count, ["results"] = res };
    }

    [Tool("arrange_viewports", "Re-lays out the viewports already on a sheet in a grid inside the title-block area. @sheet Sheet number/name/id. @columns Number of columns (default: square-ish). @reserve_right_mm @margin_mm as in create_sheet_set.", Category = "View")]
    public static object ArrangeViewports(string sheet, int? columns = null, double reserveRightMm = 70, double marginMm = 15)
    {
        var doc = Ctx.RequireProject();
        var s = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(x => x.SheetNumber.Equals(sheet, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(sheet, StringComparison.OrdinalIgnoreCase) || x.Id.Value.ToString() == sheet) ?? throw new ToolException("Sheet not found.");
        var notes = LayoutViewports(doc, s, marginMm, reserveRightMm, 0, columns);
        return new JsonObject { ["sheet"] = s.SheetNumber, ["viewports"] = s.GetAllViewports().Count, ["notes"] = notes };
    }

    [Tool("create_sheet_index", "Creates a Sheet List schedule (Sheet Number, Sheet Name, Current Revision...) — the drawing index for the title/cover sheet. @name Schedule name (default 'Sheet Index'). @extra_fields More fields e.g. ['Drawn By','Sheet Issue Date'].", Category = "View")]
    public static object CreateSheetIndex(string name = "Sheet Index", string[] extraFields = null)
    {
        var doc = Ctx.RequireProject();
        var s = ViewSchedule.CreateSheetList(doc);
        s.Name = name;
        var fields = s.Definition.GetSchedulableFields();
        var added = new JsonArray();
        foreach (var fn in new[] { "Sheet Number", "Sheet Name" }.Concat(extraFields ?? Array.Empty<string>()))
        {
            var sf = fields.FirstOrDefault(f => f.GetName(doc).Equals(fn, StringComparison.OrdinalIgnoreCase));
            if (sf != null) { s.Definition.AddField(sf); added.Add(fn); }
        }
        return new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["fields"] = added };
    }

    [Tool("add_revision", """
        Adds a revision (e.g. 'Issued for construction') and optionally assigns it to sheets so it appears in their title-block revision schedules.
        @description @date Text date (e.g. '1405/07/24'). @issued_by @issued_to. @sheets Sheet numbers to attach it to (or ['all']). @issued Mark as issued (locks it).
        """, Category = "View")]
    public static object AddRevision(string description, string date = null, string issuedBy = null, string issuedTo = null, string[] sheets = null, bool issued = false)
    {
        var doc = Ctx.RequireProject();
        var rev = Revision.Create(doc);
        rev.Description = description;
        rev.RevisionDate = date ?? DateTime.Now.ToString("yyyy-MM-dd");
        if (issuedBy != null) rev.IssuedBy = issuedBy;
        if (issuedTo != null) rev.IssuedTo = issuedTo;
        int n = 0;
        if (sheets != null && sheets.Length > 0)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            foreach (var s in sheets.Contains("all", StringComparer.OrdinalIgnoreCase) ? all : all.Where(x => sheets.Contains(x.SheetNumber, StringComparer.OrdinalIgnoreCase)))
            {
                var ids = s.GetAdditionalRevisionIds().ToList(); ids.Add(rev.Id); s.SetAdditionalRevisionIds(ids); n++;
            }
        }
        if (issued) rev.Issued = true;
        return new JsonObject { ["revision_id"] = rev.Id.Value, ["sequence"] = rev.SequenceNumber, ["sheets_updated"] = n };
    }

    // ====================================================================== smart dimensions & tags
    [Tool("dimension_grids", "Dimensions the grids of a plan view automatically: a chain along each grid direction plus an overall dimension. @view Plan view. @offset_mm Distance of the first dimension line from the grid ends (default 1500).", Category = "View")]
    public static object DimensionGrids(string view, double offsetMm = 1500)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var grids = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>().Where(g => g.Curve is Line).ToList();
        if (grids.Count < 2) throw new ToolException("Need at least two grids visible in the view.");
        double off = U.Mm(offsetMm);
        var made = new JsonArray();
        void Chain(List<Grid> list, bool vertical)
        {
            if (list.Count < 2) return;
            var sorted = vertical ? list.OrderBy(g => ((Line)g.Curve).Origin.X).ToList() : list.OrderBy(g => ((Line)g.Curve).Origin.Y).ToList();
            var all = grids.Select(g => ((Line)g.Curve)).SelectMany(l => new[] { l.GetEndPoint(0), l.GetEndPoint(1) }).ToList();
            double ext = vertical ? all.Max(p => p.Y) : all.Min(p => p.X);
            var ra = new ReferenceArray(); foreach (var g in sorted) ra.Append(new Reference(g));
            Line Ln(double o, double a, double b) => vertical ? Line.CreateBound(new XYZ(a, ext + o, 0), new XYZ(b, ext + o, 0)) : Line.CreateBound(new XYZ(ext - o, a, 0), new XYZ(ext - o, b, 0));
            double a0 = vertical ? ((Line)sorted[0].Curve).Origin.X : ((Line)sorted[0].Curve).Origin.Y, b0 = vertical ? ((Line)sorted[^1].Curve).Origin.X : ((Line)sorted[^1].Curve).Origin.Y;
            try { made.Add(doc.Create.NewDimension(v, Ln(off, a0, b0), ra).Id.Value); } catch (Exception ex) { made.Add("chain: " + ex.Message); }
            if (sorted.Count > 2)
            {
                var r2 = new ReferenceArray(); r2.Append(new Reference(sorted[0])); r2.Append(new Reference(sorted[^1]));
                try { made.Add(doc.Create.NewDimension(v, Ln(off * 1.8, a0, b0), r2).Id.Value); } catch (Exception ex) { made.Add("overall: " + ex.Message); }
            }
        }
        Chain(grids.Where(g => Math.Abs(((Line)g.Curve).Direction.Y) > 0.9).ToList(), true);
        Chain(grids.Where(g => Math.Abs(((Line)g.Curve).Direction.X) > 0.9).ToList(), false);
        return new JsonObject { ["dimensions"] = made.Count(x => x is JsonValue jv && jv.TryGetValue<long>(out _)), ["items"] = made };
    }

    static IEnumerable<Face> PlanarFaces(Element e, View v)
    {
        var ge = e.get_Geometry(new Options { ComputeReferences = true, View = v });
        IEnumerable<Face> Walk(GeometryElement g)
        {
            foreach (var go in g)
            {
                if (go is GeometryInstance gi) { foreach (var f in Walk(gi.GetInstanceGeometry())) yield return f; }
                else if (go is Solid s && s.Volume > 1e-9) foreach (Face f in s.Faces) if (f is PlanarFace) yield return f;
            }
        }
        return ge == null ? Enumerable.Empty<Face>() : Walk(ge);
    }

    [Tool("dimension_openings", """
        Dimensions the doors/windows of straight walls in a plan view: a chain from wall end → opening → opening → wall end along each wall that hosts openings.
        @view Plan view. @offset_mm Distance of the dimension line from the wall (default 600). @side exterior | interior (side of the wall normal; default exterior = left of the wall direction).
        """, Category = "View")]
    public static object DimensionOpenings(string view, double offsetMm = 600, string side = "exterior")
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var walls = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Wall)).Cast<Wall>().Where(w => (w.Location as LocationCurve)?.Curve is Line).ToList();
        var inst = new FilteredElementCollector(doc, v.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(f => f.Host is Wall && (f.Category?.Id.Value == (long)BuiltInCategory.OST_Doors || f.Category?.Id.Value == (long)BuiltInCategory.OST_Windows)).ToList();
        int made = 0; var errs = new JsonArray();
        foreach (var w in walls)
        {
            var ops = inst.Where(f => f.Host.Id == w.Id).ToList(); if (ops.Count == 0) continue;
            var ln = (Line)((LocationCurve)w.Location).Curve; var d = ln.Direction; var n = new XYZ(-d.Y, d.X, 0) * (side == "interior" ? -1 : 1);
            try
            {
                var faces = PlanarFaces(w, v).Where(f => Math.Abs(((PlanarFace)f).FaceNormal.DotProduct(d)) > 0.99).ToList();
                if (faces.Count < 2) continue;
                double T(Face f) => ((PlanarFace)f).Origin.DotProduct(d);
                var first = faces.OrderBy(T).First(); var last = faces.OrderBy(T).Last();
                var items = new List<(double t, Reference r)> { (T(first), first.Reference) };
                foreach (var o in ops)
                {
                    foreach (var rt in new[] { FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right })
                    {
                        var rr = o.GetReferences(rt).FirstOrDefault(); if (rr == null) continue;
                        var bb = o.get_BoundingBox(v); if (bb == null) continue;
                        double t = ((bb.Min + bb.Max) / 2).DotProduct(d);
                        items.Add((t + (rt == FamilyInstanceReferenceType.Left ? -1 : 1) * 1e-6, rr));
                    }
                }
                items.Add((T(last), last.Reference));
                var ordered = items.OrderBy(i => i.t).ToList();
                var ra = new ReferenceArray(); foreach (var it in ordered) ra.Append(it.r);
                var p0 = ln.GetEndPoint(0) + n * U.Mm(offsetMm); var p1 = ln.GetEndPoint(1) + n * U.Mm(offsetMm);
                doc.Create.NewDimension(v, Line.CreateBound(new XYZ(p0.X, p0.Y, 0), new XYZ(p1.X, p1.Y, 0)), ra); made++;
            }
            catch (Exception ex) { if (errs.Count < 5) errs.Add($"wall {w.Id.Value}: {ex.Message}"); }
        }
        return new JsonObject { ["dimension_chains"] = made, ["errors"] = errs };
    }

    static View ViewOrSheet(Document doc, string key)
    {
        var sh = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(x => x.SheetNumber.Equals(key, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        return sh ?? Find.View(doc, key);
    }

    static (double x0, double y0, double x1, double y1)? Box(Element e, View v) { var b = e.get_BoundingBox(v); return b == null ? null : (b.Min.X, b.Min.Y, b.Max.X, b.Max.Y); }
    static bool Overlap((double x0, double y0, double x1, double y1) a, (double x0, double y0, double x1, double y1) b) => a.x0 < b.x1 && b.x0 < a.x1 && a.y0 < b.y1 && b.y0 < a.y1;

    [Tool("check_tag_overlaps", "Finds tags (and text notes) overlapping each other in a view, and optionally fixes them by nudging the later one upward until it is free. @view View. @fix true to move overlapping tags. @gap_mm Gap kept between tags when fixing (default 5 paper mm × scale).", Category = "View")]
    public static object CheckTagOverlaps(string view, bool fix = false, double? gapMm = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).Cast<Element>().Concat(new FilteredElementCollector(doc, v.Id).OfClass(typeof(TextNote))).ToList();
        var pairs = new JsonArray(); int moved = 0;
        double gap = U.Mm(gapMm ?? 5.0 * Math.Max(1, v.Scale));
        for (int pass = 0; pass < (fix ? 8 : 1); pass++)
        {
            bool any = false; pairs.Clear();
            for (int i = 0; i < tags.Count; i++)
                for (int j = i + 1; j < tags.Count; j++)
                {
                    var a = Box(tags[i], v); var b = Box(tags[j], v); if (a == null || b == null || !Overlap(a.Value, b.Value)) continue;
                    any = true; pairs.Add(new JsonObject { ["a"] = tags[i].Id.Value, ["b"] = tags[j].Id.Value });
                    if (fix)
                    {
                        double dy = a.Value.y1 - b.Value.y0 + gap;
                        try
                        {
                            if (tags[j] is IndependentTag it) { it.TagHeadPosition = it.TagHeadPosition + new XYZ(0, dy, 0); moved++; }
                            else if (tags[j] is TextNote tn) { tn.Coord = tn.Coord + new XYZ(0, dy, 0); moved++; }
                        }
                        catch { }
                    }
                }
            if (!any) break;
            if (fix) doc.Regenerate();
        }
        return new JsonObject { ["elements_checked"] = tags.Count, ["overlapping_pairs"] = pairs.Count, ["moved"] = moved, ["pairs"] = new JsonArray(pairs.Take(50).Select(x => x.DeepClone()).ToArray()) };
    }

    // ====================================================================== schedules: export with formulas
    sealed class CalcCol { public string name { get; set; } public string formula { get; set; } }

    static string ColLetter(int i) { string s = ""; i++; while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; } return s; }

    static bool TryNum(string s, out double d)
    {
        d = 0; if (string.IsNullOrWhiteSpace(s)) return false;
        var m = Regex.Match(s.Trim(), @"^(-?\d[\d,]*\.?\d*)\s*[^\d\s]*\S*$");
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }

    [Tool("export_schedule", """
        Exports a schedule to CSV, XLSX (real Excel file with live formulas) or JSON. Optionally adds calculated columns (Excel formulas over other columns) and total rows — the way to do formulas/calculated values that Revit schedules cannot.
        @schedule Schedule name/id. @path Output file (extension decides if @format omitted). @format csv | xlsx | json. @calculated [{name, formula}] — formula over column headers e.g. 'Area * 1500000' or '[Unit Price] * Count'. @totals Column headers to sum in a final row (xlsx/csv).
        """, Category = "View", ReadOnly = true)]
    public static object ExportSchedule(string schedule, string path, string format = null, JsonArray calculated = null, string[] totals = null)
    {
        var data = (JsonObject)ScheduleTools.GetScheduleData(schedule, 100000);
        var heads = data["columns"]!.AsArray().Select(x => x?.GetValue<string>() ?? "").ToList();
        var rows = data["rows"]!.AsArray().Select(r => r!.AsArray().Select(x => x?.GetValue<string>() ?? "").ToList()).ToList();
        format = (format ?? System.IO.Path.GetExtension(path).TrimStart('.')).ToLowerInvariant();
        var calc = calculated?.Select(c => new CalcCol { name = c!["name"]!.GetValue<string>(), formula = c["formula"]!.GetValue<string>() }).ToList() ?? new();
        int baseCols = heads.Count;
        // numeric detection
        var isNum = Enumerable.Range(0, baseCols).Select(c => rows.Count > 0 && rows.Count(r => TryNum(r.ElementAtOrDefault(c), out _)) >= rows.Count(r => !string.IsNullOrWhiteSpace(r.ElementAtOrDefault(c))) * 0.8 && rows.Any(r => TryNum(r.ElementAtOrDefault(c), out _))).ToList();
        string Map(string f, int row)
        {
            return Regex.Replace(f, @"\[([^\]]+)\]|[A-Za-z_][A-Za-z0-9_]*", m =>
            {
                string n = m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                int idx = heads.FindIndex(h => h.Trim().Equals(n.Trim(), StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? ColLetter(idx) + row : m.Value;
            });
        }
        double EvalRow(string f, List<string> r)
        {
            var e = ExParser.Parse(f);
            return e.Eval(n => { int idx = heads.FindIndex(h => h.Trim().Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)); return idx >= 0 && TryNum(r.ElementAtOrDefault(idx), out var d) ? d : 0; });
        }
        var calcVals = calc.Select(c => rows.Select(r => { try { return EvalRow(c.formula, r); } catch { return double.NaN; } }).ToList()).ToList();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
        if (format == "json")
        {
            var arr = new JsonArray();
            for (int i = 0; i < rows.Count; i++)
            {
                var o = new JsonObject(); for (int c = 0; c < baseCols; c++) o[heads[c]] = rows[i].ElementAtOrDefault(c);
                for (int k = 0; k < calc.Count; k++) o[calc[k].name] = calcVals[k][i];
                arr.Add(o);
            }
            System.IO.File.WriteAllText(path, new JsonObject { ["schedule"] = data["name"]?.DeepClone(), ["rows"] = arr }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
        }
        else if (format == "csv")
        {
            var sb = new StringBuilder();
            string Esc(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            sb.AppendLine(string.Join(",", heads.Concat(calc.Select(c => c.name)).Select(Esc)));
            for (int i = 0; i < rows.Count; i++) sb.AppendLine(string.Join(",", rows[i].Select(Esc).Concat(calcVals.Select(cv => double.IsNaN(cv[i]) ? "" : R(cv[i], 4).ToString(CultureInfo.InvariantCulture)))));
            if (totals != null && totals.Length > 0)
            {
                var cells = new List<string>();
                for (int c = 0; c < baseCols + calc.Count; c++)
                {
                    string h = c < baseCols ? heads[c] : calc[c - baseCols].name;
                    if (totals.Any(t => t.Equals(h, StringComparison.OrdinalIgnoreCase)))
                        cells.Add(R(c < baseCols ? rows.Sum(r => TryNum(r.ElementAtOrDefault(c), out var d) ? d : 0) : calcVals[c - baseCols].Where(x => !double.IsNaN(x)).Sum(), 4).ToString(CultureInfo.InvariantCulture));
                    else cells.Add(c == 0 ? "Total" : "");
                }
                sb.AppendLine(string.Join(",", cells));
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
        else if (format == "xlsx")
        {
            var sheet = new StringBuilder();
            string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
            string Cell(int c, int r, string text, double? num, string formula)
            {
                string refc = ColLetter(c) + r;
                if (formula != null) return $"<c r=\"{refc}\"><f>{X(formula)}</f>{(num.HasValue && !double.IsNaN(num.Value) ? $"<v>{num.Value.ToString("R", CultureInfo.InvariantCulture)}</v>" : "")}</c>";
                if (num.HasValue) return $"<c r=\"{refc}\"><v>{num.Value.ToString("R", CultureInfo.InvariantCulture)}</v></c>";
                return $"<c r=\"{refc}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{X(text ?? "")}</t></is></c>";
            }
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView rightToLeft=\"0\" workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetData>");
            sheet.Append("<row r=\"1\">");
            for (int c = 0; c < baseCols + calc.Count; c++) sheet.Append(Cell(c, 1, c < baseCols ? heads[c] : calc[c - baseCols].name, null, null));
            sheet.Append("</row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int er = i + 2; sheet.Append($"<row r=\"{er}\">");
                for (int c = 0; c < baseCols; c++)
                {
                    string t = rows[i].ElementAtOrDefault(c) ?? "";
                    sheet.Append(isNum[c] && TryNum(t, out var d) ? Cell(c, er, t, d, null) : Cell(c, er, t, null, null));
                }
                for (int k = 0; k < calc.Count; k++) sheet.Append(Cell(baseCols + k, er, null, calcVals[k][i], Map(calc[k].formula, er)));
                sheet.Append("</row>");
            }
            if (totals != null && totals.Length > 0 && rows.Count > 0)
            {
                int tr = rows.Count + 2; sheet.Append($"<row r=\"{tr}\">");
                for (int c = 0; c < baseCols + calc.Count; c++)
                {
                    string h = c < baseCols ? heads[c] : calc[c - baseCols].name;
                    if (totals.Any(t => t.Equals(h, StringComparison.OrdinalIgnoreCase)))
                        sheet.Append(Cell(c, tr, null, c < baseCols ? rows.Sum(r => TryNum(r.ElementAtOrDefault(c), out var d) ? d : 0) : calcVals[c - baseCols].Where(x => !double.IsNaN(x)).Sum(), $"SUM({ColLetter(c)}2:{ColLetter(c)}{rows.Count + 1})"));
                    else sheet.Append(Cell(c, tr, c == 0 ? "Total" : "", null, null));
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            void Add(string name, string content) { var e = zip.CreateEntry(name); using var w = new System.IO.StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(content); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{X(new string((data["name"]?.GetValue<string>() ?? "Schedule").Where(ch => !"[]:*?/\\".Contains(ch)).Take(31).ToArray()))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        else throw new ToolException("format must be csv | xlsx | json");
        return new JsonObject { ["path"] = path, ["format"] = format, ["rows"] = rows.Count, ["calculated_columns"] = calc.Count };
    }

    // ====================================================================== Persian / Jalali
    static readonly string[] JMonths = { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

    static (int y, int m, int d) ToJalali(int gy, int gm, int gd)
    {
        int[] gdm = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };
        int gy2 = gm > 2 ? gy + 1 : gy;
        int days = 355666 + 365 * gy + (gy2 + 3) / 4 - (gy2 + 99) / 100 + (gy2 + 399) / 400 + gd + gdm[gm - 1];
        int jy = -1595 + 33 * (days / 12053); days %= 12053;
        jy += 4 * (days / 1461); days %= 1461;
        if (days > 365) { jy += (days - 1) / 365; days = (days - 1) % 365; }
        int jm = days < 186 ? 1 + days / 31 : 7 + (days - 186) / 30;
        int jd = 1 + (days < 186 ? days % 31 : (days - 186) % 30);
        return (jy, jm, jd);
    }

    static string FaDigits(string s) => new string(s.Select(c => c >= '0' && c <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());

    [Tool("jalali_date", "Converts a Gregorian date to the Persian (Jalali/Shamsi) calendar. @date ISO date 'yyyy-MM-dd' (default today). @persian_digits Use ۱۲۳ digits. @style numeric (1405/07/24) | long (۲۴ مهر ۱۴۰۵).", Category = "View", ReadOnly = true, NoDocument = true)]
    public static object JalaliDate(string date = null, bool persianDigits = false, string style = "numeric")
    {
        var dt = date == null ? DateTime.Now : DateTime.Parse(date, CultureInfo.InvariantCulture);
        var (y, m, d) = ToJalali(dt.Year, dt.Month, dt.Day);
        string text = style == "long" ? $"{d} {JMonths[m - 1]} {y}" : $"{y}/{m:00}/{d:00}";
        return new JsonObject { ["jalali"] = persianDigits ? FaDigits(text) : text, ["year"] = y, ["month"] = m, ["day"] = d, ["month_name"] = JMonths[m - 1] };
    }

    [Tool("to_persian_digits", "Converts Latin digits in a text to Persian digits (0123 → ۰۱۲۳). @text Text.", Category = "View", ReadOnly = true, NoDocument = true)]
    public static object ToPersianDigits(string text) => new JsonObject { ["text"] = FaDigits(text) };

    [Tool("create_text_note_fa", """
        Places a Persian (RTL) text note: right-aligned, RTL-marked, Persian digits optional, using a dedicated text type 'Arian Persian' (font Tahoma by default — it shapes Persian correctly in Revit).
        @view View/sheet. @point [x,y] mm (the RIGHT edge of the text because it is right-aligned). @text Persian text. @digits Convert digits to Persian (default true). @font Font name. @size_mm Text height on paper (default 3).
        """, Category = "View")]
    public static object CreateTextNoteFa(string view, double[] point, string text, bool digits = true, string font = "Tahoma", double sizeMm = 3, double? widthMm = null)
    {
        var doc = Ctx.RequireProject();
        var v = ViewOrSheet(doc, view);
        string typeName = $"Arian Persian {font} {sizeMm}";
        var tt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(t => t.Name == typeName);
        if (tt == null)
        {
            var first = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
            tt = (TextNoteType)first.Duplicate(typeName);
            tt.get_Parameter(BuiltInParameter.TEXT_FONT)?.Set(font);
            tt.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(U.Mm(sizeMm));
        }
        string t2 = digits ? FaDigits(text) : text;
        t2 = string.Join("\r", t2.Replace("\r\n", "\n").Split('\n').Select(l => "‏" + l));
        var opts = new TextNoteOptions(tt.Id) { HorizontalAlignment = HorizontalTextAlignment.Right };
        var tn = widthMm.HasValue ? TextNote.Create(doc, v.Id, U.Pt(point, 0), U.Mm(widthMm.Value), t2, opts) : TextNote.Create(doc, v.Id, U.Pt(point, 0), t2, opts);
        return new JsonObject { ["id"] = tn.Id.Value, ["type"] = tt.Name };
    }

    [Tool("set_sheet_dates_jalali", "Writes the Jalali (Persian) date into sheets' issue-date field. @sheets Sheet numbers (or ['all']). @date ISO Gregorian date (default today). @persian_digits Use Persian digits (default true). @parameter Sheet parameter to fill (default 'Sheet Issue Date').", Category = "View")]
    public static object SetSheetDatesJalali(string[] sheets, string date = null, bool persianDigits = true, string parameter = "Sheet Issue Date")
    {
        var doc = Ctx.RequireProject();
        var j = (JsonObject)JalaliDate(date, persianDigits);
        string text = j["jalali"]!.GetValue<string>();
        var all = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
        int n = 0;
        foreach (var s in sheets.Contains("all", StringComparer.OrdinalIgnoreCase) ? all : all.Where(x => sheets.Contains(x.SheetNumber, StringComparer.OrdinalIgnoreCase)))
        {
            var p = s.LookupParameter(parameter);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) { p.Set(text); n++; }
        }
        return new JsonObject { ["date"] = text, ["sheets_updated"] = n };
    }

    // ====================================================================== building rule checks (Iranian + custom)
    static double TypeLen(Element inst, Document doc, params string[] names)
    {
        var t = doc.GetElement(inst.GetTypeId());
        foreach (var n in names)
        {
            var p = t?.LookupParameter(n) ?? inst.LookupParameter(n);
            if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0) return U.ToMm(p.AsDouble());
        }
        return 0;
    }

    [Tool("check_building_rules", """
        Checks the model against configurable building-code rules and returns violations with element ids — a first-pass compliance check (default limits follow typical Iranian residential practice: Mabhas 4 general requirements, stairs, daylight; verify against the current edition and project use).
        @profile iran_residential (default) | iran_public. @limits Optional overrides: {min_ceiling_mm, min_bedroom_m2, min_living_m2, min_kitchen_m2, min_bathroom_m2, min_door_mm, min_wc_door_mm, min_stair_width_mm, max_riser_mm, min_tread_mm, min_railing_mm, min_daylight_ratio, max_wwr_percent}.
        """, Category = "Manage", ReadOnly = true)]
    public static object CheckBuildingRules(string profile = "iran_residential", JsonObject limits = null)
    {
        var doc = Ctx.RequireProject();
        bool pub = profile.Equals("iran_public", StringComparison.OrdinalIgnoreCase);
        var lim = new Dictionary<string, double>
        {
            ["min_ceiling_mm"] = 2400, ["min_bedroom_m2"] = 9, ["min_living_m2"] = 12, ["min_kitchen_m2"] = 5, ["min_bathroom_m2"] = 2.5, ["min_door_mm"] = pub ? 900 : 800, ["min_wc_door_mm"] = 700,
            ["min_stair_width_mm"] = pub ? 1200 : 1000, ["max_riser_mm"] = 180, ["min_tread_mm"] = 280, ["min_railing_mm"] = 1000, ["min_daylight_ratio"] = 0.125, ["max_wwr_percent"] = 40,
        };
        if (limits != null) foreach (var kv in limits) if (kv.Value != null && lim.ContainsKey(kv.Key)) lim[kv.Key] = kv.Value.GetValue<double>();
        var viol = new JsonArray(); int checks = 0;
        void V(string rule, Element e, string msg) => viol.Add(new JsonObject { ["rule"] = rule, ["id"] = e?.Id.Value, ["message"] = msg });
        bool Has(string name, params string[] keys) => keys.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
        // rooms
        var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>().Where(r => r.Area > 0).ToList();
        var phase = doc.Phases.Size > 0 ? doc.Phases.get_Item(doc.Phases.Size - 1) : null;
        var winByRoom = new Dictionary<long, double>();
        foreach (var w in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            double area = TypeLen(w, doc, "Width", "Rough Width") * TypeLen(w, doc, "Height", "Rough Height") / 1e6;
            Room r = null; try { r = phase != null ? (w.get_FromRoom(phase) ?? w.get_ToRoom(phase)) : null; } catch { }
            if (r != null && area > 0) winByRoom[r.Id.Value] = winByRoom.GetValueOrDefault(r.Id.Value) + area;
        }
        foreach (var r in rooms)
        {
            string name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""; double a = U.ToM2(r.Area);
            bool habitable = Has(name, "bed", "living", "خواب", "نشیمن", "پذیرایی", "office", "اتاق");
            if (habitable)
            {
                checks++;
                double h = U.ToMm(r.UnboundedHeight);
                if (h > 0 && h < lim["min_ceiling_mm"]) V("min_ceiling_height", r, $"{name}: height {h:0} mm < {lim["min_ceiling_mm"]:0} mm");
                if (winByRoom.TryGetValue(r.Id.Value, out var wa) || r.Area > 0) { double ratio = winByRoom.GetValueOrDefault(r.Id.Value) / a; checks++; if (ratio < lim["min_daylight_ratio"] && winByRoom.Count > 0) V("min_daylight_ratio", r, $"{name}: window area / floor area = {ratio:0.00} < {lim["min_daylight_ratio"]:0.000}"); }
            }
            if (Has(name, "bed", "خواب") && a < lim["min_bedroom_m2"]) { checks++; V("min_bedroom_area", r, $"{name}: {a:0.0} m² < {lim["min_bedroom_m2"]} m²"); }
            if (Has(name, "living", "نشیمن", "پذیرایی") && a < lim["min_living_m2"]) { checks++; V("min_living_area", r, $"{name}: {a:0.0} m² < {lim["min_living_m2"]} m²"); }
            if (Has(name, "kitchen", "آشپزخانه") && a < lim["min_kitchen_m2"]) { checks++; V("min_kitchen_area", r, $"{name}: {a:0.0} m² < {lim["min_kitchen_m2"]} m²"); }
            if (Has(name, "bath", "حمام") && a < lim["min_bathroom_m2"]) { checks++; V("min_bathroom_area", r, $"{name}: {a:0.0} m² < {lim["min_bathroom_m2"]} m²"); }
        }
        // doors
        foreach (var d in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().Cast<FamilyInstance>())
        {
            double w = TypeLen(d, doc, "Width", "Rough Width"); if (w <= 0) continue; checks++;
            Room to = null; try { to = phase != null ? d.get_ToRoom(phase) : null; } catch { }
            string rn = to?.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            double min = Has(rn, "wc", "toilet", "bath", "حمام", "سرویس") ? lim["min_wc_door_mm"] : lim["min_door_mm"];
            if (w < min) V("min_door_width", d, $"door width {w:0} mm < {min:0} mm" + (rn != "" ? $" (to {rn})" : ""));
        }
        // stairs
        foreach (Stairs s in new FilteredElementCollector(doc).OfClass(typeof(Stairs)))
        {
            checks++;
            double riser = U.ToMm(s.ActualRiserHeight), tread = U.ToMm(s.ActualTreadDepth);
            if (riser > lim["max_riser_mm"] + 0.5) V("max_riser_height", s, $"riser {riser:0.0} mm > {lim["max_riser_mm"]:0} mm");
            if (tread > 0 && tread < lim["min_tread_mm"] - 0.5) V("min_tread_depth", s, $"tread {tread:0} mm < {lim["min_tread_mm"]:0} mm");
            foreach (var rid in s.GetStairsRuns()) { var run = (StairsRun)doc.GetElement(rid); double w = U.ToMm(run.ActualRunWidth); if (w < lim["min_stair_width_mm"] - 0.5) V("min_stair_width", s, $"run width {w:0} mm < {lim["min_stair_width_mm"]:0} mm"); }
        }
        // railings
        foreach (var rl in new FilteredElementCollector(doc).OfClass(typeof(Railing)).Cast<Railing>())
        {
            checks++;
            try { var rt = doc.GetElement(rl.GetTypeId()) as RailingType; double h = U.ToMm(rt?.TopRailHeight ?? 0); if (h > 0 && h < lim["min_railing_mm"] - 0.5) V("min_railing_height", rl, $"top rail height {h:0} mm < {lim["min_railing_mm"]:0} mm"); } catch { }
        }
        // window-to-wall ratio (advisory for energy rules)
        double ext = 0, win = 0;
        foreach (var w in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(w => w.WallType.Function == WallFunction.Exterior)) ext += U.ToM2(w.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0);
        foreach (var w in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType()) win += TypeLen(w, doc, "Width", "Rough Width") * TypeLen(w, doc, "Height", "Rough Height") / 1e6;
        double wwr = ext > 0 ? win / (ext + win) * 100 : 0;
        if (ext > 0 && wwr > lim["max_wwr_percent"]) viol.Add(new JsonObject { ["rule"] = "max_window_to_wall_ratio", ["message"] = $"WWR {wwr:0.0}% > {lim["max_wwr_percent"]:0}% (energy, Mabhas 19 style advisory)" });
        return new JsonObject { ["profile"] = profile, ["checks_run"] = checks, ["violations"] = viol.Count, ["window_to_wall_percent"] = R(wwr), ["limits"] = new JsonObject(lim.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value))), ["items"] = viol, ["note"] = "Default limits are typical values; confirm against the current Iranian national building regulations (Mabhas 4, 19, 22 ...) and the project's use before relying on the result." };
    }

    // ====================================================================== CAD / PDF standards
    [Tool("export_dwg_standard", """
        Exports views/sheets to DWG with a standard layer mapping and clean settings.
        @folder @views View/sheet names/ids/numbers. @layer_standard AIA | ISO13567 | CP83 | BS1192 (default AIA). @merged_views true = one DWG per sheet with xrefs merged (default true) . @file_name Base file name.
        """, Category = "View", ReadOnly = true)]
    public static object ExportDwgStandard(string folder, string[] views, string layerStandard = "AIA", bool mergedViews = true, string fileName = "export")
    {
        var doc = Ctx.RequireProject();
        System.IO.Directory.CreateDirectory(folder);
        var ids = views.Select(v => (new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(x => x.SheetNumber.Equals(v, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(v, StringComparison.OrdinalIgnoreCase)) as View ?? Find.View(doc, v)).Id).ToList();
        var opt = new DWGExportOptions { MergedViews = mergedViews, SharedCoords = false };
        var notes = new JsonArray();
        try { opt.LayerMapping = layerStandard; } catch (Exception ex) { notes.Add("layer mapping '" + layerStandard + "' not applied: " + ex.Message); }
        var t0 = DateTime.Now.AddSeconds(-2);
        bool ok = doc.Export(folder, fileName, ids, opt);
        var files = new JsonArray(new System.IO.DirectoryInfo(folder).GetFiles("*.dwg").Where(f => f.LastWriteTime >= t0).Select(f => (JsonNode)new JsonObject { ["file"] = f.FullName, ["bytes"] = f.Length }).ToArray());
        return new JsonObject { ["exported"] = ok, ["layer_standard"] = layerStandard, ["files"] = files, ["notes"] = notes };
    }

    [Tool("export_sheets_pdf", "Exports sheets to PDF, selecting them by number prefix. @folder @number_prefix e.g. 'A-' (default all sheets) @combine One combined PDF (default false = one PDF per sheet) @file_name Name of the combined file.", Category = "View", ReadOnly = true)]
    public static object ExportSheetsPdf(string folder, string numberPrefix = null, bool combine = false, string fileName = "Drawing set")
    {
        var doc = Ctx.RequireProject();
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => numberPrefix == null || s.SheetNumber.StartsWith(numberPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.SheetNumber).ToList();
        if (sheets.Count == 0) throw new ToolException("No sheets match.");
        return ViewTools.ExportPdf(folder, fileName, sheets.Select(s => s.Id.Value.ToString()).ToArray(), combine);
    }
}
