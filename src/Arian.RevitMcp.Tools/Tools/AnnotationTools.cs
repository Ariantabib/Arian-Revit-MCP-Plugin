using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arian.RevitMcp.Tools;

public static class AnnotationTools
{
    [Tool("create_text_note", """
        Places a text note in a view.
        @view View name/id. @point [x,y] mm position in model coordinates (for sheets: sheet coordinates). @text The text. @text_type Text note type (default first). @width_mm Optional wrapping width.
        """, Category = "View")]
    public static object CreateTextNote(string view, double[] point, string text, string textType = null, double? widthMm = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var tt = textType != null ? Find.Type<TextNoteType>(doc, textType) : new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
        var opts = new TextNoteOptions(tt.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
        TextNote tn = widthMm.HasValue
            ? TextNote.Create(doc, v.Id, U.Pt(point, 0), U.Mm(widthMm.Value), text, opts)
            : TextNote.Create(doc, v.Id, U.Pt(point, 0), text, opts);
        return new JsonObject { ["id"] = tn.Id.Value };
    }

    [Tool("list_annotation_types", "Lists text note types, dimension types, tag types of a category, filled region types, line styles.", Category = "View", ReadOnly = true)]
    public static object ListAnnotationTypes(string kind = "text", string category = null)
    {
        var doc = Ctx.RequireProject();
        IEnumerable<Element> els = kind.ToLowerInvariant() switch
        {
            "text" => new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)),
            "dimension" => new FilteredElementCollector(doc).OfClass(typeof(DimensionType)),
            "filled_region" => new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)),
            "tag" => new FilteredElementCollector(doc).WhereElementIsElementType().OfCategoryId(Find.Category(doc, (category ?? "Door") + " Tags").Id),
            "line_style" => doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories.Cast<Category>().Select(c => doc.GetElement(c.Id) ?? null).Where(x => x != null),
            _ => throw new ToolException("kind: text | dimension | filled_region | tag | line_style"),
        };
        if (kind == "line_style")
            return new JsonObject { ["line_styles"] = new JsonArray(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories.Cast<Category>().Select(c => (JsonNode)c.Name).ToArray()) };
        return new JsonObject { ["types"] = new JsonArray(els.Select(e => (JsonNode)new JsonObject { ["id"] = e.Id.Value, ["name"] = e.Name }).ToArray()) };
    }

    [Tool("tag_elements", """
        Tags elements in a view with their category tag (door numbers, wall types, room tags are separate: tag_rooms).
        @view View. @ids Elements to tag (or use category to tag everything of that category visible in the view). @category Category name.
        @tag_type Optional tag family type. @add_leader Add leaders. @offset_mm Offset of the tag from the element centre [dx,dy].
        """, Category = "View")]
    public static object TagElements(string view, long[] ids = null, string category = null, string tagType = null, bool addLeader = false, double[] offsetMm = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        IEnumerable<Element> els;
        if (ids != null && ids.Length > 0) els = Find.Elements(doc, ids);
        else if (!string.IsNullOrWhiteSpace(category)) els = new FilteredElementCollector(doc, v.Id).OfCategoryId(Find.Category(doc, category).Id).WhereElementIsNotElementType().ToList();
        else throw new ToolException("Give ids or a category.");
        var off = offsetMm != null ? new XYZ(U.Mm(offsetMm[0]), U.Mm(offsetMm[1]), 0) : XYZ.Zero;
        int n = 0; var errs = new JsonArray();
        foreach (var e in els)
        {
            try
            {
                var bb = e.get_BoundingBox(v) ?? e.get_BoundingBox(null);
                var c = bb != null ? (bb.Min + bb.Max) / 2 : XYZ.Zero;
                var tag = IndependentTag.Create(doc, v.Id, new Reference(e), addLeader, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, c + off);
                if (!string.IsNullOrWhiteSpace(tagType))
                {
                    try { tag.ChangeTypeId(Find.Type(doc, tagType).Id); }
                    catch (Autodesk.Revit.Exceptions.ArgumentException) { doc.Delete(tag.Id); throw new ToolException($"Tag type '{tagType}' cannot tag {e.Category?.Name} elements (use list_annotation_types kind=tag)."); }
                }
                n++;
            }
            catch (Exception ex) { if (errs.Count < 3) errs.Add(ex.Message); }
        }
        return new JsonObject { ["tagged"] = n, ["errors"] = errs };
    }

    /// <summary>Reference of a wall's location line: the wall's own geometry exposes it as a (non-visible) Line with a LINEAR reference.</summary>
    static Reference WallCentreRef(Wall w, View v, Curve loc)
    {
        var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, View = v };
        var mid = loc.Evaluate(0.5, true);
        Reference best = null; double bd = double.MaxValue;
        foreach (var go in w.get_Geometry(opt))
        {
            if (go is not Line ln || ln.Reference == null) continue;
            double d = ln.Project(mid)?.Distance ?? double.MaxValue;
            if (d < bd) { bd = d; best = ln.Reference; }
        }
        return best ?? loc.Reference;
    }

    [Tool("create_dimension", """
        Creates a linear dimension string in a view between elements: grids, walls (centreline or faces), columns/families (centre planes) or levels (in elevation/section views).
        @view View (plan/section/elevation). @ids Elements in measurement order. @direction x | y | z: the direction being measured (the dimension line runs along it). @offset_mm Distance of the dimension line from the elements. @faces For walls: dimension between wall FACES instead of centrelines.
        """, Category = "View")]
    public static object CreateDimension(string view, long[] ids, string direction = "x", double offsetMm = 1000, bool faces = false)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var dirVec = direction.ToLowerInvariant() switch { "y" => XYZ.BasisY, "z" => XYZ.BasisZ, _ => XYZ.BasisX };
        var refs = new ReferenceArray();
        var pts = new List<XYZ>();
        foreach (var e in Find.Elements(doc, ids))
        {
            Reference r = null; XYZ p = null;
            switch (e)
            {
                case Grid g: r = new Reference(g); p = g.Curve.GetEndPoint(0); break;
                case Level l: r = new Reference(l); p = new XYZ(0, 0, l.Elevation); break;
                case Wall w when w.Location is LocationCurve lc:
                    if (faces)
                    {
                        var sides = HostObjectUtils.GetSideFaces(w, ShellLayerType.Interior);
                        r = sides.FirstOrDefault();
                    }
                    else r = WallCentreRef(w, v, lc.Curve);
                    p = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;
                    break;
                case FamilyInstance fi:
                    var kind = Math.Abs(dirVec.DotProduct(fi.HandOrientation)) > 0.7 ? FamilyInstanceReferenceType.CenterLeftRight : FamilyInstanceReferenceType.CenterFrontBack;
                    if (dirVec.Z != 0) kind = FamilyInstanceReferenceType.CenterElevation;
                    r = fi.GetReferences(kind).FirstOrDefault();
                    p = (fi.Location as LocationPoint)?.Point;
                    break;
            }
            if (r == null || p == null) throw new ToolException($"Element {e.Id.Value} ({e.Category?.Name}) cannot be dimensioned by this tool (try grids, levels, walls, or placed families).");
            refs.Append(r); pts.Add(p);
        }
        double min = pts.Min(p => p.DotProduct(dirVec)), max = pts.Max(p => p.DotProduct(dirVec));
        XYZ perp = dirVec.Z != 0 ? XYZ.BasisX : (dirVec.X != 0 ? XYZ.BasisY : XYZ.BasisX);
        double perpPos = pts.Max(p => p.DotProduct(perp)) + U.Mm(offsetMm);
        var origin = perp * perpPos;
        var line = Line.CreateBound(origin + dirVec * min, origin + dirVec * max);
        var dim = doc.Create.NewDimension(v, line, refs);
        return new JsonObject { ["id"] = dim.Id.Value, ["value_mm"] = dim.Value.HasValue ? U.R(U.ToMm(dim.Value.Value), 1) : null, ["segments"] = dim.NumberOfSegments };
    }

    [Tool("create_detail_lines", """
        Draws 2D detail lines (or a closed polygon) in a view.
        @view View. @points [[x,y],...] mm. @closed Close the shape. @line_style Optional line style name (list_annotation_types kind=line_style).
        """, Category = "View")]
    public static object CreateDetailLines(string view, double[][] points, bool closed = false, string lineStyle = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var pts = Geo.Clean(points.Select(p => U.Pt(p, 0)).ToList(), closed);
        GraphicsStyle gs = null;
        if (!string.IsNullOrWhiteSpace(lineStyle))
        {
            var sub = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories.Cast<Category>().FirstOrDefault(c => c.Name.Equals(lineStyle, StringComparison.OrdinalIgnoreCase));
            gs = sub?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
        var ids = new JsonArray();
        var curves = Geo.Polyline(pts, closed);
        foreach (var c in curves)
        {
            var dc = doc.Create.NewDetailCurve(v, c);
            if (gs != null) dc.LineStyle = gs;
            ids.Add(dc.Id.Value);
        }
        return new JsonObject { ["lines"] = ids.Count, ["ids"] = ids };
    }

    [Tool("create_filled_region", "Creates a filled region (hatch/solid fill) from a polygon in a view (e.g. insulation hatch, colour blocks). @region_type: filled region type name (list_annotation_types kind=filled_region).", Category = "View")]
    public static object CreateFilledRegion(string view, double[][] boundary, string regionType = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var rt = regionType != null ? Find.Type<FilledRegionType>(doc, regionType) : new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().First();
        var loop = Geo.Loop(boundary.Select(p => U.Pt(p, 0)).ToList(), 0);
        var fr = FilledRegion.Create(doc, rt.Id, v.Id, new List<CurveLoop> { loop });
        return new JsonObject { ["id"] = fr.Id.Value };
    }

    [Tool("create_revision_cloud", "Creates a revision cloud around a polygon in a view (creates a revision if none exists).", Category = "View")]
    public static object CreateRevisionCloud(string view, double[][] boundary, string description = "Revision")
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var rev = Revision.GetAllRevisionIds(doc).Select(i => doc.GetElement(i) as Revision).LastOrDefault() ?? Revision.Create(doc);
        if (rev != null && string.IsNullOrEmpty(rev.Description)) rev.Description = description;
        var curves = Geo.Polyline(boundary.Select(p => U.Pt(p, 0)).ToList(), true);
        var rc = RevisionCloud.Create(doc, v, rev.Id, curves);
        return new JsonObject { ["id"] = rc.Id.Value, ["revision"] = rev.Description };
    }
}
