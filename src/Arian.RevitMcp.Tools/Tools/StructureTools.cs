using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace Arian.RevitMcp.Tools;

public static class StructureTools
{
    // ----------------------------------------------------------------- grid helpers
    sealed class GridLine { public Grid G; public XYZ P, D; public string Name => G.Name; }

    static List<GridLine> StraightGrids(Document doc, string[] names)
    {
        var res = new List<GridLine>();
        foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)))
        {
            if (g.Curve is not Line ln) continue;
            if (names != null && names.Length > 0 && !names.Contains(g.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var d = ln.Direction; d = new XYZ(d.X, d.Y, 0).Normalize();
            res.Add(new GridLine { G = g, P = ln.GetEndPoint(0), D = d });
        }
        return res;
    }

    /// <summary>Intersection of two infinite 2D lines; returns null if parallel.</summary>
    static XYZ Intersect(GridLine a, GridLine b, out double ta)
    {
        ta = 0;
        double cross = a.D.X * b.D.Y - a.D.Y * b.D.X;
        if (Math.Abs(cross) < 1e-6) return null;
        double dx = b.P.X - a.P.X, dy = b.P.Y - a.P.Y;
        ta = (dx * b.D.Y - dy * b.D.X) / cross;
        return new XYZ(a.P.X + a.D.X * ta, a.P.Y + a.D.Y * ta, 0);
    }

    [Tool("get_grid_intersections", "Returns all intersection points between the straight grid lines (mm) with their grid names — to place columns/footings precisely.", Category = "Structure", ReadOnly = true)]
    public static object GetGridIntersections(string[] gridNames = null)
    {
        var doc = Ctx.RequireProject();
        var gs = StraightGrids(doc, gridNames);
        var arr = new JsonArray();
        for (int i = 0; i < gs.Count; i++)
            for (int j = i + 1; j < gs.Count; j++)
            {
                var p = Intersect(gs[i], gs[j], out _);
                if (p != null) arr.Add(new JsonObject { ["grids"] = gs[i].Name + "/" + gs[j].Name, ["point"] = Dto.JsonArr(p) });
            }
        return new JsonObject { ["count"] = arr.Count, ["intersections"] = arr };
    }

    // ----------------------------------------------------------------- columns
    [Tool("create_structural_column", """
        Places ONE structural column between two levels.
        @point [x,y] mm. @column_type Type 'Family: Type' (list_element_types category='Structural Columns').
        @base_level @top_level Levels. @base_offset_mm @top_offset_mm. @rotation_deg. @parameters Extra instance parameters.
        """, Category = "Structure")]
    public static object CreateStructuralColumn(double[] point, string columnType, string baseLevel, string topLevel, double baseOffsetMm = 0,
        double topOffsetMm = 0, double rotationDeg = 0, Dictionary<string, JsonNode> parameters = null)
    {
        var doc = Ctx.RequireProject();
        var fi = Place.One(doc, new PlaceSpec
        {
            Type = columnType, Point = point, Level = baseLevel, TopLevel = topLevel, BaseOffsetMm = baseOffsetMm, TopOffsetMm = topOffsetMm,
            RotationDeg = rotationDeg, Parameters = parameters, StructuralType = "column",
        });
        doc.Regenerate();
        return Dto.Brief(fi);
    }

    [Tool("create_columns_at_grid_intersections", """
        Places structural columns at grid intersections (all grids, or only those named).
        @column_type, @base_level, @top_level, @base_offset_mm, @top_offset_mm as create_structural_column.
        @grid_names Restrict to these grid names (empty = all grids). @skip_points [[x,y],...] intersections to skip (mm, 50 mm tolerance).
        @rotation_deg Rotation of every column.
        """, Category = "Structure")]
    public static object CreateColumnsAtGridIntersections(string columnType, string baseLevel, string topLevel, string[] gridNames = null,
        double baseOffsetMm = 0, double topOffsetMm = 0, double rotationDeg = 0, double[][] skipPoints = null)
    {
        var doc = Ctx.RequireProject();
        var gs = StraightGrids(doc, gridNames);
        var pts = new List<XYZ>();
        for (int i = 0; i < gs.Count; i++)
            for (int j = i + 1; j < gs.Count; j++)
            { var p = Intersect(gs[i], gs[j], out _); if (p != null) pts.Add(p); }
        var skip = (skipPoints ?? Array.Empty<double[]>()).Select(s => U.Pt(s)).ToList();
        pts = pts.Where(p => !skip.Any(s => new XYZ(s.X, s.Y, 0).DistanceTo(new XYZ(p.X, p.Y, 0)) < U.Mm(50))).ToList();
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var p in pts)
        {
            try
            {
                var fi = Place.One(doc, new PlaceSpec { Type = columnType, Point = new[] { U.ToMm(p.X), U.ToMm(p.Y) }, Level = baseLevel, TopLevel = topLevel,
                    BaseOffsetMm = baseOffsetMm, TopOffsetMm = topOffsetMm, RotationDeg = rotationDeg, StructuralType = "column" });
                ids.Add(fi.Id.Value);
            }
            catch (Exception ex) { errs.Add(ex.Message); }
        }
        doc.Regenerate();
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    // ----------------------------------------------------------------- beams & braces
    [Tool("create_beam", """
        Places ONE structural framing member (beam/girder/joist/brace) between two points.
        @start [x,y,z] mm. @end [x,y,z] mm. z absolute (default: level elevation + z_offset_mm).
        @beam_type Type 'Family: Type' (list_element_types category='Structural Framing'). @level Reference level.
        @z_offset_mm Offset above the level when z is not given in the points. @structural_type beam (default) | brace.
        @parameters Extra instance parameters (e.g. 'Start Level Offset', 'z Justification').
        """, Category = "Structure")]
    public static object CreateBeam(double[] start, double[] end, string beamType, string level, double zOffsetMm = 0, string structuralType = "beam",
        Dictionary<string, JsonNode> parameters = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        double z = U.ToMm(lv.Elevation) + zOffsetMm;
        double[] s = start.Length > 2 ? start : new[] { start[0], start[1], z };
        double[] e = end.Length > 2 ? end : new[] { end[0], end[1], z };
        var fi = Place.One(doc, new PlaceSpec { Type = beamType, Point = s, End = e, Level = level, Parameters = parameters, StructuralType = structuralType });
        doc.Regenerate();
        return Dto.Brief(fi);
    }

    public sealed class BeamSpec
    {
        public double[] Start; public double[] End; public string Type; public double ZOffsetMm;
    }

    [Tool("create_beams", "Places many beams at once: [{start:[x,y], end:[x,y], type, z_offset_mm}] on a level.", Category = "Structure")]
    public static object CreateBeams(BeamSpec[] beams, string level, string defaultType = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var b in beams)
        {
            try
            {
                double z = U.ToMm(lv.Elevation) + b.ZOffsetMm;
                var fi = Place.One(doc, new PlaceSpec { Type = b.Type ?? defaultType, Point = new[] { b.Start[0], b.Start[1], z }, End = new[] { b.End[0], b.End[1], z }, Level = level, StructuralType = "beam" });
                ids.Add(fi.Id.Value);
            }
            catch (Exception ex) { errs.Add(ex.Message); }
        }
        doc.Regenerate();
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    [Tool("create_beams_along_grids", """
        Generates a beam framing layout: one beam between every pair of consecutive grid intersections along each grid line (a classic column-line frame).
        @beam_type Beam type. @level Reference level (beam top/centre elevation = level + z_offset_mm).
        @z_offset_mm Elevation above the level. @grid_names Restrict to these grids (empty = all).
        @direction both | x (lines running along X) | y (lines running along Y).
        """, Category = "Structure")]
    public static object CreateBeamsAlongGrids(string beamType, string level, double zOffsetMm = 0, string[] gridNames = null, string direction = "both")
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        double z = U.ToMm(lv.Elevation) + zOffsetMm;
        var gs = StraightGrids(doc, gridNames);
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var a in gs)
        {
            bool alongX = Math.Abs(a.D.X) > Math.Abs(a.D.Y);
            if (direction == "x" && !alongX) continue;
            if (direction == "y" && alongX) continue;
            var ts = new List<(double t, XYZ p)>();
            foreach (var b in gs)
            {
                if (b == a) continue;
                var p = Intersect(a, b, out var ta);
                if (p != null) ts.Add((ta, p));
            }
            ts = ts.OrderBy(x => x.t).ToList();
            for (int i = 0; i + 1 < ts.Count; i++)
            {
                if (ts[i].p.DistanceTo(ts[i + 1].p) < U.Mm(300)) continue;
                try
                {
                    var fi = Place.One(doc, new PlaceSpec
                    {
                        Type = beamType, Level = level, StructuralType = "beam",
                        Point = new[] { U.ToMm(ts[i].p.X), U.ToMm(ts[i].p.Y), z },
                        End = new[] { U.ToMm(ts[i + 1].p.X), U.ToMm(ts[i + 1].p.Y), z },
                    });
                    ids.Add(fi.Id.Value);
                }
                catch (Exception ex) { errs.Add(a.Name + ": " + ex.Message); }
            }
        }
        doc.Regenerate();
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    // ----------------------------------------------------------------- foundations
    [Tool("create_isolated_footing", "Places an isolated (pad) footing at a point under a column.", Category = "Structure")]
    public static object CreateIsolatedFooting(double[] point, string footingType, string level, double elevationOffsetMm = 0)
    {
        var doc = Ctx.RequireProject();
        var fi = Place.One(doc, new PlaceSpec { Type = footingType, Point = point, Level = level, ElevationMm = elevationOffsetMm, StructuralType = "footing" });
        return Dto.Brief(fi);
    }

    [Tool("create_footings_under_columns", """
        Places an isolated footing under every structural column (or the given ones).
        @footing_type Footing type. @level Foundation level (default: the column's base level). @column_ids Optional restriction.
        @elevation_offset_mm Offset of the footing relative to the level (negative = deeper).
        """, Category = "Structure")]
    public static object CreateFootingsUnderColumns(string footingType, string level = null, long[] columnIds = null, double elevationOffsetMm = 0)
    {
        var doc = Ctx.RequireProject();
        var cols = columnIds != null && columnIds.Length > 0
            ? Find.Elements(doc, columnIds).OfType<FamilyInstance>().ToList()
            : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var c in cols)
        {
            if (c.Location is not LocationPoint lp) continue;
            try
            {
                var lvName = level ?? doc.GetElement(c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId() ?? c.LevelId)?.Name;
                var fi = Place.One(doc, new PlaceSpec { Type = footingType, Point = new[] { U.ToMm(lp.Point.X), U.ToMm(lp.Point.Y) }, Level = lvName, ElevationMm = elevationOffsetMm, StructuralType = "footing" });
                ids.Add(fi.Id.Value);
            }
            catch (Exception ex) { errs.Add($"column {c.Id.Value}: {ex.Message}"); }
        }
        doc.Regenerate();
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    [Tool("create_strip_footing", "Creates a continuous wall footing under walls. @footing_type: continuous footing type name; @wall_ids: structural walls.", Category = "Structure")]
    public static object CreateStripFooting(string footingType, long[] wallIds)
    {
        var doc = Ctx.RequireProject();
        var t = Find.Type<WallFoundationType>(doc, footingType);
        var ids = new JsonArray();
        foreach (var w in wallIds)
        {
            var f = WallFoundation.Create(doc, t.Id, new ElementId(w));
            ids.Add(f.Id.Value);
        }
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids };
    }

    [Tool("create_foundation_slab", """
        Creates a foundation slab / raft / structural floor from a polygon. Use a floor type from category 'Structural Foundations' for a foundation slab (list_element_types category='Structural Foundations' / 'Floors').
        @boundary Polygon mm. @floor_type @level. @offset_mm Offset from level (usually negative).
        """, Category = "Structure")]
    public static object CreateFoundationSlab(double[][] boundary, string floorType, string level, double offsetMm = 0)
    {
        var doc = Ctx.RequireProject();
        var ft = Find.Type<FloorType>(doc, floorType);
        var lv = Find.Level(doc, level);
        var f = Floor.Create(doc, new List<CurveLoop> { Geo.Loop(boundary.Select(p => U.Pt(p)).ToList(), lv.Elevation) }, ft.Id, lv.Id);
        if (offsetMm != 0) f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(U.Mm(offsetMm));
        f.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.Set(1);
        return Dto.Brief(f);
    }

    [Tool("create_truss", "Creates a truss along a line using a truss type. @truss_type: truss type name or id (OST_Truss, e.g. M_Howe Flat Truss: Standard; list with list_element_types category OST_Truss); @level for the work plane.", Category = "Structure")]
    public static object CreateTruss(double[] start, double[] end, string trussType, string level)
    {
        var doc = Ctx.RequireProject();
        var t = Find.Type(doc, trussType, BuiltInCategory.OST_Truss);
        var lv = Find.Level(doc, level);
        double z = lv.Elevation;
        var sp = SketchPlane.Create(doc, lv.Id);
        var truss = Truss.Create(doc, t.Id, sp.Id, Line.CreateBound(U.Pt(start, U.ToMm(z)), U.Pt(end, U.ToMm(z))));
        return Dto.Brief(truss);
    }

    // ----------------------------------------------------------------- rebar
    [Tool("list_rebar_types", "Lists rebar bar types (diameters), shapes and cover types available.", Category = "Structure", ReadOnly = true)]
    public static object ListRebarTypes()
    {
        var doc = Ctx.RequireProject();
        var bars = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .Select(b => (JsonNode)new JsonObject { ["id"] = b.Id.Value, ["name"] = b.Name, ["diameter_mm"] = U.R(U.ToMm(b.BarModelDiameter), 1) }).ToArray());
        var cover = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).Cast<RebarCoverType>()
            .Select(c => (JsonNode)new JsonObject { ["id"] = c.Id.Value, ["name"] = c.Name, ["cover_mm"] = U.R(U.ToMm(c.CoverDistance), 1) }).ToArray());
        var shapes = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>().Take(60)
            .Select(s => (JsonNode)new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name }).ToArray());
        return new JsonObject { ["bar_types"] = bars, ["cover_types"] = cover, ["shapes"] = shapes };
    }

    [Tool("create_rebar", """
        Creates a rebar bar (or a set) from a polyline shape inside a concrete host (column, beam, wall, floor, footing).
        @host_id Concrete element id. @bar_type Bar type name e.g. '16 Ø' / '#5' (list_rebar_types).
        @points Bar centreline polyline [[x,y,z],...] mm in model coordinates (straight segments; bends/hooks are not included).
        @normal Normal [x,y,z] of the plane containing the bar shape.
        @style standard | stirrup (closed ties).
        @set_count Optional: number of bars in a set; with @set_spacing_mm spacing. The set repeats along the bar's normal direction (flip the sign of 'normal' to repeat the other way).
        """, Category = "Structure")]
    public static object CreateRebar(long hostId, string barType, double[][] points, double[] normal, string style = "standard",
        int? setCount = null, double setSpacingMm = 150)
    {
        var doc = Ctx.RequireProject();
        var host = Find.Element(doc, hostId);
        var bt = Find.Type<RebarBarType>(doc, barType);
        var pts = points.Select(p => U.Pt(p)).ToList();
        var curves = new List<Curve>();
        for (int i = 0; i + 1 < pts.Count; i++) curves.Add(Line.CreateBound(pts[i], pts[i + 1]));
        if (style == "stirrup" && pts.Count > 2 && pts[0].DistanceTo(pts[^1]) > Geo.MinLen) curves.Add(Line.CreateBound(pts[^1], pts[0]));
        var rs = style == "stirrup" ? RebarStyle.StirrupTie : RebarStyle.Standard;
        var rb = Rebar.CreateFromCurves(doc, rs, bt, host, U.Vec(normal).Normalize(), curves, new BarTerminationsData(doc), true, true);
        if (rb == null) throw new ToolException("Revit could not create the rebar from these curves (check host/normal/shape).");
        if (setCount.HasValue && setCount.Value > 1)
        {
            var acc = rb.GetShapeDrivenAccessor();
            acc.SetLayoutAsNumberWithSpacing(setCount.Value, U.Mm(setSpacingMm), true, true, true);
        }
        return Dto.Brief(rb);
    }

    [Tool("create_column_reinforcement", """
        Adds a standard reinforcement cage to a RECTANGULAR, vertical, axis-aligned concrete column: longitudinal bars (4 corners + extra along faces) and stirrups at spacing.
        @column_id Column. @long_bar_type @stirrup_type Bar types. @cover_mm Clear cover. @bars_per_face Bars on each face including corners (min 2 => 4 corner bars; 3 => 8 bars; ...).
        @stirrup_spacing_mm Spacing of ties.
        """, Category = "Structure")]
    public static object CreateColumnReinforcement(long columnId, string longBarType, string stirrupType, double coverMm = 40, int barsPerFace = 2, double stirrupSpacingMm = 150)
    {
        var doc = Ctx.RequireProject();
        var col = Find.Element(doc, columnId);
        var bb = col.get_BoundingBox(null) ?? throw new ToolException("Column has no geometry.");
        var lb = Find.Type<RebarBarType>(doc, longBarType); var sb = Find.Type<RebarBarType>(doc, stirrupType);
        double cov = U.Mm(coverMm), dL = lb.BarModelDiameter, dS = sb.BarModelDiameter;
        double x0 = bb.Min.X + cov + dS + dL / 2, x1 = bb.Max.X - cov - dS - dL / 2;
        double y0 = bb.Min.Y + cov + dS + dL / 2, y1 = bb.Max.Y - cov - dS - dL / 2;
        double z0 = bb.Min.Z + cov, z1 = bb.Max.Z - cov;
        if (x1 <= x0 || y1 <= y0) throw new ToolException("Column section too small for this cover/bar size.");
        var made = new JsonArray();
        var pos = new List<(double x, double y)>();
        int n = Math.Max(2, barsPerFace);
        for (int i = 0; i < n; i++)
        {
            double fx = x0 + (x1 - x0) * i / (n - 1), fy = y0 + (y1 - y0) * i / (n - 1);
            pos.Add((fx, y0)); pos.Add((fx, y1));
            if (i > 0 && i < n - 1) { pos.Add((x0, fy)); pos.Add((x1, fy)); }
        }
        foreach (var (x, y) in pos.Distinct())
        {
            var rb = Rebar.CreateFromCurves(doc, RebarStyle.Standard, lb, col, XYZ.BasisX, new List<Curve> { Line.CreateBound(new XYZ(x, y, z0), new XYZ(x, y, z1)) }, new BarTerminationsData(doc), true, true);
            if (rb != null) made.Add(rb.Id.Value);
        }
        // stirrups
        var loop = new List<Curve>
        {
            Line.CreateBound(new XYZ(x0 - dL / 2 - dS / 2, y0 - dL / 2 - dS / 2, z0), new XYZ(x1 + dL / 2 + dS / 2, y0 - dL / 2 - dS / 2, z0)),
            Line.CreateBound(new XYZ(x1 + dL / 2 + dS / 2, y0 - dL / 2 - dS / 2, z0), new XYZ(x1 + dL / 2 + dS / 2, y1 + dL / 2 + dS / 2, z0)),
            Line.CreateBound(new XYZ(x1 + dL / 2 + dS / 2, y1 + dL / 2 + dS / 2, z0), new XYZ(x0 - dL / 2 - dS / 2, y1 + dL / 2 + dS / 2, z0)),
            Line.CreateBound(new XYZ(x0 - dL / 2 - dS / 2, y1 + dL / 2 + dS / 2, z0), new XYZ(x0 - dL / 2 - dS / 2, y0 - dL / 2 - dS / 2, z0)),
        };
        var st = Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, sb, col, XYZ.BasisZ, loop, new BarTerminationsData(doc), true, true);
        if (st != null)
        {
            st.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(U.Mm(stirrupSpacingMm), z1 - z0, true, true, true);
            made.Add(st.Id.Value);
        }
        return new JsonObject { ["rebar_ids"] = made, ["longitudinal_bars"] = pos.Distinct().Count(), ["stirrup_spacing_mm"] = stirrupSpacingMm };
    }

    // ----------------------------------------------------------------- loads / analytical
    static ElementId Analytical(Document doc, long physicalId)
    {
        var mgr = AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
        var id = mgr.GetAssociatedElementId(new ElementId(physicalId));
        if (id != null && id != ElementId.InvalidElementId) return id;
        // Elements created through the API often have no analytical element yet: create and associate one on demand.
        var e = Find.Element(doc, physicalId);
        ElementId created = null;
        if (e is Floor || e is Wall || e is FootPrintRoof)
        {
            var loop = AnalyticalLoop(e);
            if (loop != null) created = AnalyticalPanel.Create(doc, loop).Id;
        }
        else if (e.Location is LocationCurve lc) created = AnalyticalMember.Create(doc, lc.Curve).Id;
        else if (e.Location is LocationPoint lp && e.get_BoundingBox(null) is { } bb)
            created = AnalyticalMember.Create(doc, Line.CreateBound(new XYZ(lp.Point.X, lp.Point.Y, bb.Min.Z), new XYZ(lp.Point.X, lp.Point.Y, bb.Max.Z))).Id;
        if (created == null)
            throw new ToolException($"Element {physicalId} has no analytical member and none could be created (supported: columns, beams, braces, walls, floors, slabs).");
        mgr.AddAssociation(created, e.Id);
        return created;
    }

    /// <summary>Mid-plane outline of a floor / wall for an analytical panel.</summary>
    static CurveLoop AnalyticalLoop(Element e)
    {
        if (e is Floor fl)
        {
            var top = HostObjectUtils.GetTopFaces(fl).FirstOrDefault();
            if (top == null || fl.GetGeometryObjectFromReference(top) is not PlanarFace pf) return null;
            var outer = pf.GetEdgesAsCurveLoops().OrderByDescending(l => l.GetExactLength()).First();
            double t = fl.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0;
            return CurveLoop.CreateViaTransform(outer, Transform.CreateTranslation(new XYZ(0, 0, -t / 2)));
        }
        if (e is Wall w && w.Location is LocationCurve lc && e.get_BoundingBox(null) is { } bb)
        {
            var c = lc.Curve; double z0 = bb.Min.Z, z1 = bb.Max.Z;
            XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
            var l = new CurveLoop();
            l.Append(Line.CreateBound(new XYZ(a.X, a.Y, z0), new XYZ(b.X, b.Y, z0)));
            l.Append(Line.CreateBound(new XYZ(b.X, b.Y, z0), new XYZ(b.X, b.Y, z1)));
            l.Append(Line.CreateBound(new XYZ(b.X, b.Y, z1), new XYZ(a.X, a.Y, z1)));
            l.Append(Line.CreateBound(new XYZ(a.X, a.Y, z1), new XYZ(a.X, a.Y, z0)));
            return l;
        }
        return null;
    }

    static double KN(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Kilonewtons);
    static double KNperM(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.KilonewtonsPerMeter);
    static double KNperM2(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.KilonewtonsPerSquareMeter);

    [Tool("list_load_cases", "Lists structural load cases and natures.", Category = "Structure", ReadOnly = true)]
    public static object ListLoadCases()
    {
        var doc = Ctx.RequireProject();
        var cases = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(LoadCase)).Cast<LoadCase>().Select(c =>
            (JsonNode)new JsonObject { ["id"] = c.Id.Value, ["name"] = c.Name, ["number"] = c.Number, ["nature"] = doc.GetElement(c.NatureId)?.Name }).ToArray());
        return new JsonObject { ["load_cases"] = cases };
    }

    [Tool("create_point_load", """
        Applies a point load on an analytical structural member (column/beam/wall...) at a point. The analytical member is created and associated automatically if the physical element has none.
        @host_id Physical structural element id. @point [x,y,z] mm. @force_kn [fx,fy,fz] in kN (gravity = negative z). @moment_knm Optional [mx,my,mz] in kN·m.
        @load_case Optional load case name (list_load_cases), e.g. 'DL1' / 'LL1'.
        """, Category = "Structure")]
    public static object CreatePointLoad(long hostId, double[] point, double[] forceKn, double[] momentKnm = null, string loadCase = null)
    {
        var doc = Ctx.RequireProject();
        var aid = Analytical(doc, hostId);
        var f = new XYZ(KN(forceKn[0]), KN(forceKn[1]), KN(forceKn[2]));
        var m = momentKnm == null ? XYZ.Zero : new XYZ(UnitUtils.ConvertToInternalUnits(momentKnm[0], UnitTypeId.KilonewtonMeters), UnitUtils.ConvertToInternalUnits(momentKnm[1], UnitTypeId.KilonewtonMeters), UnitUtils.ConvertToInternalUnits(momentKnm[2], UnitTypeId.KilonewtonMeters));
        var load = PointLoad.Create(doc, aid, U.Pt(point), f, m, null);
        SetLoadCase(doc, load, loadCase);
        return Dto.Brief(load);
    }

    [Tool("create_line_load", "Applies a line (distributed) load along a line on an analytical member. force_kn_per_m [fx,fy,fz] in kN/m (gravity = negative z). load_case: optional load case name (list_load_cases).", Category = "Structure")]
    public static object CreateLineLoad(long hostId, double[] start, double[] end, double[] forceKnPerM, string loadCase = null)
    {
        var doc = Ctx.RequireProject();
        var aid = Analytical(doc, hostId);
        var f = new XYZ(KNperM(forceKnPerM[0]), KNperM(forceKnPerM[1]), KNperM(forceKnPerM[2]));
        var load = LineLoad.Create(doc, aid, Line.CreateBound(U.Pt(start), U.Pt(end)), f, XYZ.Zero, null);
        SetLoadCase(doc, load, loadCase);
        return Dto.Brief(load);
    }

    [Tool("create_area_load", "Applies a uniform area load over a polygon on an analytical surface (floor/slab). pressure_kn_per_m2 [fx,fy,fz] (gravity = negative z). The polygon is projected onto the analytical panel's plane. load_case: optional load case name.", Category = "Structure")]
    public static object CreateAreaLoad(long hostId, double[][] boundary, double[] pressureKnPerM2, string loadCase = null)
    {
        var doc = Ctx.RequireProject();
        var aid = Analytical(doc, hostId);
        var pts = boundary.Select(p => U.Pt(p)).ToList();
        if (doc.GetElement(aid) is AnalyticalSurfaceBase panel)
        {
            var pl = panel.GetOuterContour().GetPlane();
            pts = pts.Select(p => p - pl.Normal * (p - pl.Origin).DotProduct(pl.Normal)).ToList();
        }
        var loop = Geo.Loop(pts, null);
        var f = new XYZ(KNperM2(pressureKnPerM2[0]), KNperM2(pressureKnPerM2[1]), KNperM2(pressureKnPerM2[2]));
        var load = AreaLoad.Create(doc, aid, new List<CurveLoop> { loop }, f, null);
        SetLoadCase(doc, load, loadCase);
        return Dto.Brief(load);
    }

    static void SetLoadCase(Document doc, LoadBase load, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var lc = new FilteredElementCollector(doc).OfClass(typeof(LoadCase)).Cast<LoadCase>().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolException($"Load case '{name}' not found (list_load_cases).");
        load.get_Parameter(BuiltInParameter.LOAD_CASE_ID)?.Set(lc.Id);
    }

    [Tool("set_structural_usage", """
        Sets structural properties on framing/columns/walls/floors: structural material, usage (girder/horizontal bracing/joist/purlin/kicker bracing/vertical bracing...), analytical model on (creates + associates an analytical member/panel if missing) / off (deletes it), enable rebar cover.
        @ids Elements. @material Structural material name. @usage Beam structural usage.
        """, Category = "Structure")]
    public static object SetStructuralUsage(long[] ids, string material = null, string usage = null, bool? enableAnalytical = null)
    {
        var doc = Ctx.RequireProject();
        var m = material != null ? Find.Material(doc, material) : null;
        int n = 0;
        foreach (var e in Find.Elements(doc, ids))
        {
            if (m != null) { var p = e.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM); if (p != null && !p.IsReadOnly) p.Set(m.Id); }
            if (usage != null && e is FamilyInstance fi)
            {
                var p = e.get_Parameter(BuiltInParameter.INSTANCE_STRUCT_USAGE_PARAM);
                if (p != null && Enum.TryParse<StructuralInstanceUsage>(usage.Replace(" ", ""), true, out var u)) { try { p.Set((int)u); } catch { } }
            }
            if (enableAnalytical == true) { try { Analytical(doc, e.Id.Value); } catch (ToolException) { } }
            else if (enableAnalytical == false)
            {
                var mgr = AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                var aid = mgr.GetAssociatedElementId(e.Id);
                if (aid != null && aid != ElementId.InvalidElementId) doc.Delete(aid);
            }
            n++;
        }
        return new JsonObject { ["updated"] = n };
    }

    [Tool("get_structural_summary", """
        Structural takeoff: for columns, framing, foundations, structural floors and walls — counts, total volume (m3) and length grouped by type and material. Use it to review the structural system.
        """, Category = "Structure", ReadOnly = true)]
    public static object GetStructuralSummary()
    {
        var doc = Ctx.RequireProject();
        var res = new JsonObject();
        foreach (var (name, bic) in new[] { ("columns", BuiltInCategory.OST_StructuralColumns), ("framing", BuiltInCategory.OST_StructuralFraming), ("foundations", BuiltInCategory.OST_StructuralFoundation),
                     ("floors", BuiltInCategory.OST_Floors), ("walls", BuiltInCategory.OST_Walls) })
        {
            var groups = new Dictionary<string, (int n, double vol, double len)>();
            foreach (var e in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType())
            {
                if ((bic == BuiltInCategory.OST_Floors || bic == BuiltInCategory.OST_Walls) &&
                    e.get_Parameter(bic == BuiltInCategory.OST_Walls ? BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT : BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() != 1) continue;
                var t = doc.GetElement(e.GetTypeId());
                string key = (t as ElementType)?.FamilyName + ": " + t?.Name;
                var matP = e.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (matP != null && matP.AsElementId() != ElementId.InvalidElementId) key += " [" + doc.GetElement(matP.AsElementId())?.Name + "]";
                double v = e.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble() ?? 0;
                double l = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? e.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)?.AsDouble() ?? 0;
                groups.TryGetValue(key, out var g);
                groups[key] = (g.n + 1, g.vol + v, g.len + l);
            }
            var arr = new JsonArray(groups.OrderByDescending(g => g.Value.n).Select(g => (JsonNode)new JsonObject
            {
                ["type"] = g.Key, ["count"] = g.Value.n, ["volume_m3"] = U.R(U.ToM3(g.Value.vol), 2), ["total_length_m"] = U.R(U.ToMm(g.Value.len) / 1000, 2),
            }).ToArray());
            res[name] = arr;
        }
        return res;
    }
}
