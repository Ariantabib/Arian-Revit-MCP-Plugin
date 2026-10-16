using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-2 architecture: edges, sweeps, ramps, shaped stairs, areas, finishes, site sub-regions, CAD to walls.</summary>
public static class ArchDetailTools
{
    // ------------------------------------------------------------------ edge helpers
    /// <summary>Edge references of a host's solid that are horizontal and lie at its lowest (or highest) level.</summary>
    static ReferenceArray HorizontalEdges(Element host, bool lowest)
    {
        var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        var list = new List<(Edge e, double z)>();
        void Walk(GeometryElement ge)
        {
            foreach (var go in ge)
            {
                if (go is Solid s && s.Volume > 1e-9)
                    foreach (Edge e in s.Edges)
                    {
                        var c = e.AsCurve();
                        if (c is not Line ln) continue;
                        var a = ln.GetEndPoint(0); var b = ln.GetEndPoint(1);
                        if (Math.Abs(a.Z - b.Z) > 1e-4 || e.Reference == null) continue;
                        list.Add((e, a.Z));
                    }
                else if (go is GeometryInstance gi) Walk(gi.GetInstanceGeometry());
            }
        }
        Walk(host.get_Geometry(opts));
        if (list.Count == 0) throw new ToolException("No horizontal edges with references found on the element.");
        double z0 = lowest ? list.Min(x => x.z) : list.Max(x => x.z);
        var arr = new ReferenceArray();
        foreach (var (e, z) in list) if (Math.Abs(z - z0) < 1e-3) arr.Append(e.Reference);
        return arr;
    }

    [Tool("create_slab_edge", """
        Adds a slab edge (fascia/thickened edge profile) along the perimeter of a floor.
        @floor_id Floor. @edge_type Slab edge type name (list_element_types category='OST_EdgeSlab'). @position bottom (default, lowest horizontal edges) | top.
        """, Category = "Architecture")]
    public static object CreateSlabEdge(long floorId, string edgeType, string position = "bottom")
    {
        var doc = Ctx.RequireProject();
        var f = Find.Element<Floor>(doc, floorId);
        var t = Find.Type<SlabEdgeType>(doc, edgeType);
        var se = doc.Create.NewSlabEdge(t, HorizontalEdges(f, position != "top"));
        return Dto.Brief(se);
    }

    [Tool("create_fascia", "Adds a fascia along the lowest (eave) edges of a roof or the edge of a floor. @roof_id host element. @fascia_type Type name (list_element_types category='OST_Fascia').", Category = "Architecture")]
    public static object CreateFascia(long roofId, string fasciaType)
    {
        var doc = Ctx.RequireProject();
        var host = Find.Element(doc, roofId);
        var t = Find.Type<FasciaType>(doc, fasciaType);
        return Dto.Brief(doc.Create.NewFascia(t, HorizontalEdges(host, true)));
    }

    [Tool("create_gutter", "Adds a gutter along the lowest (eave) edges of a roof. @gutter_type Type name (list_element_types category='OST_Gutter').", Category = "Architecture")]
    public static object CreateGutter(long roofId, string gutterType)
    {
        var doc = Ctx.RequireProject();
        var host = Find.Element(doc, roofId);
        var t = Find.Type<GutterType>(doc, gutterType);
        return Dto.Brief(doc.Create.NewGutter(t, HorizontalEdges(host, true)));
    }

    [Tool("create_wall_sweep", """
        Adds a horizontal (or vertical) sweep (cornice, skirting, plinth, band) or reveal (groove) to walls.
        @wall_ids Walls. @profile_type Type name: category 'Wall Sweeps' (list_element_types category='OST_Cornices'; the same types serve sweeps and reveals).
        @kind sweep | reveal. @distance_mm Height of the profile origin above the wall base (or from the top when from_top=true). @side exterior | interior. @vertical true for vertical sweeps.
        """, Category = "Architecture")]
    public static object CreateWallSweep(long[] wallIds, string profileType, string kind = "sweep", double distanceMm = 0, string side = "exterior", bool fromTop = false, bool vertical = false)
    {
        var doc = Ctx.RequireProject();
        bool reveal = kind.Equals("reveal", StringComparison.OrdinalIgnoreCase);
        var t = Find.Type(doc, profileType, BuiltInCategory.OST_Cornices);
        var ids = new JsonArray();
        foreach (var id in wallIds)
        {
            var w = Find.Element<Wall>(doc, id);
            var info = new WallSweepInfo(reveal ? WallSweepType.Reveal : WallSweepType.Sweep, vertical);
            info.Distance = U.Mm(distanceMm);
            info.DistanceMeasuredFrom = fromTop ? DistanceMeasuredFrom.Top : DistanceMeasuredFrom.Base;
            info.WallSide = side.Equals("interior", StringComparison.OrdinalIgnoreCase) ? WallSide.Interior : WallSide.Exterior;
            var ws = WallSweep.Create(w, t.Id, info);
            ids.Add(ws.Id.Value);
        }
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids };
    }

    // ------------------------------------------------------------------ ramps
    [Tool("create_ramp", """
        Creates a ramp as a SLOPED floor slab (the Revit API cannot create native Ramp elements) between two points, with optional side railings.
        @start [x,y] mm bottom of the ramp. @end [x,y] mm top of the ramp. @width_mm Ramp width. @base_level Level at the bottom. @rise_mm Height climbed (default: up to top_level or 1000).
        @top_level Optional level at the top. @floor_type Floor type. @railings Add railings on both sides (railing_type needed).
        Disabled-access ramps: slope <= 8.3% (1:12); the result reports the slope.
        """, Category = "Architecture")]
    public static object CreateRamp(double[] start, double[] end, double widthMm, string baseLevel, string floorType, double? riseMm = null, string topLevel = null, string railingType = null, bool railings = false)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel);
        var tl = Find.Level(doc, topLevel, true);
        double rise = riseMm ?? (tl != null ? U.ToMm(tl.Elevation - bl.Elevation) : 1000);
        var s = U.Pt(start, 0); var e = U.Pt(end, 0);
        var dir = (e - s); double len = dir.GetLength(); dir = dir.Normalize();
        var n = XYZ.BasisZ.CrossProduct(dir);
        double hw = U.Mm(widthMm) / 2;
        var pts = new List<XYZ> { s - n * hw, e - n * hw, e + n * hw, s + n * hw };
        var loop = Geo.Loop(pts, bl.Elevation);
        double slope = rise / U.ToMm(len);
        var a = new XYZ(s.X, s.Y, bl.Elevation);
        var arrow = Line.CreateBound(a, a + dir * len);
        var ft = Find.Type<FloorType>(doc, floorType);
        var floor = Floor.Create(doc, new List<CurveLoop> { loop }, ft.Id, bl.Id, false, arrow, slope);
        var o = Dto.Brief(floor);
        o["slope_percent"] = U.R(slope * 100, 2);
        o["length_mm"] = U.R(U.ToMm(len), 0);
        if (slope > 0.0834) o["warning"] = "Slope exceeds 8.3% (1:12): not compliant for disabled access.";
        if (railings && railingType != null)
        {
            var rt = Find.Type<RailingType>(doc, railingType);
            var ids = new JsonArray();
            foreach (var side in new[] { -1, 1 })
            {
                var p0 = s + n * hw * side; var p1 = e + n * hw * side;
                var path = new CurveLoop();
                path.Append(Line.CreateBound(new XYZ(p0.X, p0.Y, bl.Elevation), new XYZ(p1.X, p1.Y, bl.Elevation + U.Mm(rise))));
                try { ids.Add(Railing.Create(doc, path, rt.Id, bl.Id).Id.Value); } catch (Exception ex) { o["railing_note"] = ex.Message; }
            }
            o["railing_ids"] = ids;
        }
        return o;
    }

    // ------------------------------------------------------------------ shaped stairs
    [Tool("create_l_stairs", """
        Creates an L-shaped stair (two runs + landing) with risers computed from the floor height. The first run starts at 'start' heading direction_deg; after the landing the stair turns 90 degrees to the turn side.
        @start [x,y] mm. @direction_deg heading of run 1. @turn left | right. @base_level @top_level. @width_mm (default 1100). @riser_mm (170) @tread_mm (280).
        """, Category = "Architecture", NoTransaction = true)]
    public static object CreateLStairs(double[] start, double directionDeg, string baseLevel, string topLevel, string turn = "left", double widthMm = 1100, double riserMm = 170, double treadMm = 280)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel);
        double h = U.ToMm(tl.Elevation - bl.Elevation);
        int n = (int)Math.Ceiling(h / riserMm - 1e-6);
        int n1 = (n + 1) / 2, n2 = n - n1;
        double l1 = Math.Max(1, n1 - 1) * treadMm, l2 = Math.Max(1, n2 - 1) * treadMm;
        double z = U.ToMm(bl.Elevation);
        var d = new XYZ(Math.Cos(U.Deg(directionDeg)), Math.Sin(U.Deg(directionDeg)), 0);
        var nrm = turn.Equals("right", StringComparison.OrdinalIgnoreCase) ? new XYZ(d.Y, -d.X, 0) : new XYZ(-d.Y, d.X, 0);
        double w = U.Mm(widthMm);
        var s = U.Pt(start, z);
        var e1 = s + d * U.Mm(l1);
        var c = e1 + d * (w / 2);                 // landing centre
        var s2 = c + nrm * (w / 2);
        var e2 = s2 + nrm * U.Mm(l2);
        var runs = new List<(Line, double)> { (Line.CreateBound(s, e1), widthMm), (Line.CreateBound(s2, e2), widthMm) };
        return ArchBuildTools.BuildStairs(doc, runs, bl, tl, null, riserMm + 5, treadMm);
    }

    [Tool("create_u_stairs", """
        Creates a U-shaped (switchback) stair: two parallel runs joined by a landing (typical stair core). Run 1 starts at 'start' heading direction_deg; run 2 returns alongside on the turn side.
        @start [x,y] mm. @direction_deg. @turn left | right. @base_level @top_level. @width_mm (1100) @gap_mm gap between runs (100). @riser_mm @tread_mm.
        """, Category = "Architecture", NoTransaction = true)]
    public static object CreateUStairs(double[] start, double directionDeg, string baseLevel, string topLevel, string turn = "left", double widthMm = 1100, double gapMm = 100, double riserMm = 170, double treadMm = 280)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel);
        double h = U.ToMm(tl.Elevation - bl.Elevation);
        int n = (int)Math.Ceiling(h / riserMm - 1e-6);
        int n1 = (n + 1) / 2, n2 = n - n1;
        double l1 = Math.Max(1, n1 - 1) * treadMm, l2 = Math.Max(1, n2 - 1) * treadMm;
        double z = U.ToMm(bl.Elevation);
        var d = new XYZ(Math.Cos(U.Deg(directionDeg)), Math.Sin(U.Deg(directionDeg)), 0);
        var nrm = turn.Equals("right", StringComparison.OrdinalIgnoreCase) ? new XYZ(d.Y, -d.X, 0) : new XYZ(-d.Y, d.X, 0);
        double w = U.Mm(widthMm), g = U.Mm(gapMm);
        var s = U.Pt(start, z);
        var e1 = s + d * U.Mm(l1);
        var s2 = e1 + nrm * (w + g);              // run 2 starts level with the end of run 1; Revit's automatic landing then spans beyond both runs
        var e2 = s2 - d * U.Mm(l2);
        var runs = new List<(Line, double)> { (Line.CreateBound(s, e1), widthMm), (Line.CreateBound(s2, e2), widthMm) };
        return ArchBuildTools.BuildStairs(doc, runs, bl, tl, null, riserMm + 5, treadMm);
    }

    // ------------------------------------------------------------------ areas & finishes
    [Tool("create_areas_from_rooms", """
        Creates Area-plan areas (gross/net area scheme) from the rooms of a level: an area plan view, boundary lines following each room's finish boundary, and an Area with the room's name and number.
        @level Level. @scheme Area scheme name (default: the first, usually 'Gross Building').
        """, Category = "Architecture")]
    public static object CreateAreasFromRooms(string level, string scheme = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var sch = scheme != null ? new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().FirstOrDefault(s => s.Name.Equals(scheme, StringComparison.OrdinalIgnoreCase))
            : new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().FirstOrDefault();
        if (sch == null) throw new ToolException("No area scheme found in the project.");
        var plan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.AreaPlan && v.GenLevel?.Id == lv.Id && v.AreaScheme?.Id == sch.Id)
            ?? ViewPlan.CreateAreaPlan(doc, sch.Id, lv.Id);
        var sp = SketchPlane.Create(doc, lv.Id);
        var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.LevelId == lv.Id && r.Area > 0).ToList();
        var result = new JsonArray(); var errs = new JsonArray();
        foreach (var r in rooms)
        {
            try
            {
                var loops = r.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish });
                if (loops != null)
                    foreach (var loop in loops)
                        foreach (var seg in loop)
                            try { doc.Create.NewAreaBoundaryLine(sp, seg.GetCurve(), plan); } catch { }
                var p = (r.Location as LocationPoint)?.Point ?? XYZ.Zero;
                var area = doc.Create.NewArea(plan, new UV(p.X, p.Y));
                var nm = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                if (nm != null) area.get_Parameter(BuiltInParameter.ROOM_NAME)?.Set(nm);
                area.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.Set(r.Number);
                result.Add(new JsonObject { ["area_id"] = area.Id.Value, ["room"] = nm });
            }
            catch (Exception ex) { errs.Add($"room {r.Id.Value}: {ex.Message}"); }
        }
        doc.Regenerate();
        double total = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().OfType<SpatialElement>().Where(a => a.LevelId == lv.Id).Sum(a => a.Area);
        return new JsonObject { ["area_plan"] = plan.Name, ["areas"] = result, ["total_area_m2"] = U.R(U.ToM2(total), 2), ["errors"] = errs };
    }

    [Tool("apply_room_finishes", """
        Writes finish information on rooms (Floor/Wall/Ceiling/Base Finish parameters) and optionally creates the finish floor and ceiling elements. Use for finish schedules.
        @room_ids Rooms (default: all rooms of 'level'). @level Level (when room_ids is empty).
        @floor_finish @wall_finish @ceiling_finish @base_finish Text values (e.g. 'Ceramic tile 60x60').
        @by_name Optional map by room-name substring -> {floor, wall, ceiling, base}, e.g. {"Bath":{"floor":"Ceramic","wall":"Tile to 2.1m"}} (overrides the generic values).
        """, Category = "Architecture")]
    public static object ApplyRoomFinishes(long[] roomIds = null, string level = null, string floorFinish = null, string wallFinish = null, string ceilingFinish = null,
        string baseFinish = null, Dictionary<string, Dictionary<string, string>> byName = null)
    {
        var doc = Ctx.RequireProject();
        IEnumerable<Room> rooms;
        if (roomIds != null && roomIds.Length > 0) rooms = Find.Elements(doc, roomIds).OfType<Room>();
        else
        {
            var lv = Find.Level(doc, level);
            rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.LevelId == lv.Id);
        }
        int n = 0;
        foreach (var r in rooms)
        {
            string f = floorFinish, w = wallFinish, c = ceilingFinish, b = baseFinish;
            var nm = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            if (byName != null)
                foreach (var kv in byName)
                    if (nm.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (kv.Value.TryGetValue("floor", out var v1)) f = v1;
                        if (kv.Value.TryGetValue("wall", out var v2)) w = v2;
                        if (kv.Value.TryGetValue("ceiling", out var v3)) c = v3;
                        if (kv.Value.TryGetValue("base", out var v4)) b = v4;
                    }
            if (f != null) r.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.Set(f);
            if (w != null) r.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL)?.Set(w);
            if (c != null) r.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING)?.Set(c);
            if (b != null) r.get_Parameter(BuiltInParameter.ROOM_FINISH_BASE)?.Set(b);
            n++;
        }
        return new JsonObject { ["rooms_updated"] = n };
    }

    // ------------------------------------------------------------------ site
    [Tool("create_site_subregion", """
        Creates a sub-region (paving, lawn, road area) on an existing toposurface from a polygon, optionally with a material.
        @boundary Polygon [[x,y],...] mm. @topo_id Toposurface id (default: the first one). @material Optional material name for the sub-region.
        """, Category = "Architecture")]
    public static object CreateSiteSubregion(double[][] boundary, long? topoId = null, string material = null)
    {
        var doc = Ctx.RequireProject();
        var topo = topoId.HasValue ? Find.Element(doc, topoId.Value) : new FilteredElementCollector(doc).OfClass(typeof(TopographySurface)).FirstOrDefault()
            ?? throw new ToolException("No toposurface in the project. Use create_toposurface first.");
        var loop = Geo.Loop(boundary.Select(p => U.Pt(p)).ToList(), null);
        var sr = SiteSubRegion.Create(doc, new List<CurveLoop> { loop }, topo.Id);
        var surf = sr.TopographySurface;
        if (!string.IsNullOrWhiteSpace(material)) surf?.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(Find.Material(doc, material).Id);
        return Dto.Brief(surf ?? topo);
    }

    // ------------------------------------------------------------------ CAD to model
    static IEnumerable<(Curve c, string layer)> CadCurves(ImportInstance imp)
    {
        var ge = imp.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        IEnumerable<(Curve, string)> Walk(GeometryElement g)
        {
            foreach (var go in g)
            {
                if (go is GeometryInstance gi) { foreach (var x in Walk(gi.GetInstanceGeometry())) yield return x; continue; }
                string layer = null;
                if (go.GraphicsStyleId != ElementId.InvalidElementId)
                    layer = (imp.Document.GetElement(go.GraphicsStyleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name;
                if (go is Curve c) yield return (c, layer);
                else if (go is PolyLine pl)
                {
                    var pts = pl.GetCoordinates();
                    for (int i = 0; i + 1 < pts.Count; i++)
                        if (pts[i].DistanceTo(pts[i + 1]) > Geo.MinLen) yield return (Line.CreateBound(pts[i], pts[i + 1]), layer);
                }
            }
        }
        return Walk(ge);
    }

    [Tool("list_cad_layers", "Lists the layers of an imported/linked CAD file with the number of curves on each — use before convert_cad_layer_to_walls. @cad_id: id from list_links.", Category = "Manage", ReadOnly = true)]
    public static object ListCadLayers(long cadId)
    {
        var doc = Ctx.RequireProject();
        var imp = Find.Element<ImportInstance>(doc, cadId);
        var groups = CadCurves(imp).GroupBy(x => x.layer ?? "(none)").OrderByDescending(g => g.Count());
        return new JsonObject { ["layers"] = new JsonArray(groups.Select(g => (JsonNode)new JsonObject { ["layer"] = g.Key, ["curves"] = g.Count(), ["length_m"] = U.R(U.ToMm(g.Sum(x => x.c.Length)) / 1000, 1) }).ToArray()) };
    }

    [Tool("convert_cad_layer_to_walls", """
        Traces the lines/arcs of a CAD layer (e.g. 'A-WALL') into real Revit walls — turns a DWG plan into a model. Duplicate/overlapping segments are skipped.
        @cad_id Imported/linked CAD instance. @layers Layer names to convert (substring match, case-insensitive). @wall_type @base_level @top_level or @height_mm.
        @min_length_mm Ignore tiny segments (default 100). @max_walls Safety limit (default 2000).
        """, Category = "Architecture")]
    public static object ConvertCadLayerToWalls(long cadId, string[] layers, string wallType, string baseLevel, string topLevel = null, double heightMm = 3000, double minLengthMm = 100, int maxWalls = 2000)
    {
        var doc = Ctx.RequireProject();
        var imp = Find.Element<ImportInstance>(doc, cadId);
        var wt = Find.Type<WallType>(doc, wallType);
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel, true);
        var seen = new HashSet<string>();
        int made = 0, skipped = 0; var ids = new JsonArray();
        foreach (var (c, layer) in CadCurves(imp))
        {
            if (layer == null || !layers.Any(l => layer.Contains(l, StringComparison.OrdinalIgnoreCase))) continue;
            if (U.ToMm(c.Length) < minLengthMm) { skipped++; continue; }
            var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
            string key = $"{Math.Round(Math.Min(a.X, b.X), 3)}_{Math.Round(Math.Min(a.Y, b.Y), 3)}_{Math.Round(Math.Max(a.X, b.X), 3)}_{Math.Round(Math.Max(a.Y, b.Y), 3)}";
            if (!seen.Add(key)) { skipped++; continue; }
            Curve flat = c is Line ? Line.CreateBound(new XYZ(a.X, a.Y, bl.Elevation), new XYZ(b.X, b.Y, bl.Elevation))
                : c is Arc arc ? Arc.Create(new XYZ(a.X, a.Y, bl.Elevation), new XYZ(b.X, b.Y, bl.Elevation), new XYZ(arc.Evaluate(0.5, true).X, arc.Evaluate(0.5, true).Y, bl.Elevation)) : null;
            if (flat == null) { skipped++; continue; }
            try
            {
                var w = Wall.Create(doc, flat, wt.Id, bl.Id, U.Mm(tl != null ? U.ToMm(tl.Elevation - bl.Elevation) : heightMm), 0, false, false);
                if (tl != null) w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(tl.Id);
                ids.Add(w.Id.Value); made++;
                if (made >= maxWalls) break;
            }
            catch { skipped++; }
        }
        return new JsonObject { ["walls_created"] = made, ["skipped"] = skipped, ["ids"] = new JsonArray(ids.Take(300).Select(x => (JsonNode)x.GetValue<long>()).ToArray()) };
    }

    [Tool("convert_cad_double_lines_to_walls", """
        Converts a DWG plan whose walls are drawn as DOUBLE lines (two parallel lines per wall — the usual case) into real walls: parallel line pairs become one centerline wall whose thickness equals the gap
        (wall types 'CAD wall NNN' are created from the base type per thickness). @cad_id Imported CAD instance. @layers Layer names (substring). @wall_type Base wall type. @base_level @top_level or @height_mm.
        @min_thickness_mm (default 60) @max_thickness_mm (default 500) gap range of a wall. @min_length_mm shortest wall (default 300). @max_walls Safety limit (default 3000). @dry_run true = only report counts.
        """, Category = "Architecture")]
    public static object ConvertCadDoubleLinesToWalls(long cadId, string[] layers, string wallType, string baseLevel, string topLevel = null, double heightMm = 3000,
        double minThicknessMm = 60, double maxThicknessMm = 500, double minLengthMm = 300, int maxWalls = 3000, bool dryRun = false)
    {
        var doc = Ctx.RequireProject();
        var imp = Find.Element<ImportInstance>(doc, cadId);
        var baseType = Find.Type<WallType>(doc, wallType);
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel, true);
        // straight segments of the chosen layers, in feet
        var segs = new List<(XYZ a, XYZ b)>();
        foreach (var (c, layer) in CadCurves(imp))
        {
            if (layer == null || !layers.Any(l => layer.Contains(l, StringComparison.OrdinalIgnoreCase))) continue;
            if (c is Line && c.Length > U.Mm(minLengthMm * 0.5)) segs.Add((c.GetEndPoint(0), c.GetEndPoint(1)));
        }
        // group by direction angle (0.5 degree buckets, modulo 180)
        double minT = U.Mm(minThicknessMm), maxT = U.Mm(maxThicknessMm), minL = U.Mm(minLengthMm);
        var items = segs.Select(s =>
        {
            var a0 = s.a; var b0 = s.b;
            var d = new XYZ(b0.X - a0.X, b0.Y - a0.Y, 0); double len = d.GetLength(); d = d / len;
            if (d.Y < 0 || (Math.Abs(d.Y) < 1e-12 && d.X < 0)) { d = -d; a0 = s.b; }
            double ang = Math.Atan2(d.Y, d.X);
            if (ang > Math.PI - 0.003) ang -= Math.PI;   // near-horizontal lines pointing either way share one group
            return (a: a0, d, len, ang);
        }).OrderBy(i => i.ang).ToList();
        // cluster by direction (0.17 degree)
        var clusters = new List<List<(XYZ a, XYZ d, double len, double ang)>>();
        foreach (var it in items)
        {
            if (clusters.Count > 0 && it.ang - clusters[^1][^1].ang < 0.003) clusters[^1].Add(it); else clusters.Add(new() { it });
        }
        var walls = new List<(XYZ p, XYZ q, double t)>();
        foreach (var grp in clusters)
        {
            var g = grp.ToList();
            var d = new XYZ(Math.Cos(g.Average(x => x.ang)), Math.Sin(g.Average(x => x.ang)), 0); var n = new XYZ(-d.Y, d.X, 0);
            // (offset across, [t0,t1] along)
            var proj = g.Select(x => (off: x.a.DotProduct(n), t0: x.a.DotProduct(d), t1: x.a.DotProduct(d) + x.len)).OrderBy(x => x.off).ToList();
            var found = new List<(double off, double gap, double t0, double t1)>();
            for (int i = 0; i < proj.Count; i++)
                for (int j = i + 1; j < proj.Count; j++)
                {
                    double gap = proj[j].off - proj[i].off;
                    if (gap > maxT) break;
                    if (gap < minT) continue;
                    double t0 = Math.Max(proj[i].t0, proj[j].t0), t1 = Math.Min(proj[i].t1, proj[j].t1);
                    if (t1 - t0 >= minL) found.Add(((proj[i].off + proj[j].off) / 2, gap, t0, t1));
                }
            // merge overlapping results with the same centre offset and thickness
            foreach (var m in found.GroupBy(f => (Math.Round(U.ToMm(f.off) / 20), Math.Round(U.ToMm(f.gap) / 10))))
            {
                double off = m.Average(x => x.off), gap = m.Average(x => x.gap);
                double cur0 = double.NaN, cur1 = double.NaN;
                foreach (var x in m.OrderBy(x => x.t0))
                {
                    if (double.IsNaN(cur0)) { cur0 = x.t0; cur1 = x.t1; }
                    else if (x.t0 <= cur1 + U.Mm(50)) cur1 = Math.Max(cur1, x.t1);
                    else { walls.Add((d * cur0 + n * off, d * cur1 + n * off, gap)); cur0 = x.t0; cur1 = x.t1; }
                }
                if (!double.IsNaN(cur0)) walls.Add((d * cur0 + n * off, d * cur1 + n * off, gap));
            }
        }
        if (dryRun) return new JsonObject { ["segments"] = segs.Count, ["walls_found"] = walls.Count, ["thicknesses_mm"] = new JsonArray(walls.Select(w => Math.Round(U.ToMm(w.t) / 10) * 10).Distinct().OrderBy(x => x).Select(x => (JsonNode)x).ToArray()) };
        var types = new Dictionary<int, WallType>();
        WallType TypeFor(double gapFt)
        {
            int mm = (int)(Math.Round(U.ToMm(gapFt) / 5) * 5);
            if (types.TryGetValue(mm, out var t)) return t;
            string name = "CAD wall " + mm;
            t = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(x => x.Name == name);
            if (t == null)
            {
                t = (WallType)baseType.Duplicate(name);
                var cs = baseType.GetCompoundStructure();
                ElementId mat = cs?.GetMaterialId(0) ?? ElementId.InvalidElementId;
                t.SetCompoundStructure(CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, U.Mm(mm), mat));
            }
            return types[mm] = t;
        }
        int made = 0, failed = 0; var ids = new JsonArray();
        double z = bl.Elevation;
        foreach (var w in walls)
        {
            Activity.Check();
            if (made >= maxWalls) break;
            try
            {
                var line = Line.CreateBound(new XYZ(w.p.X, w.p.Y, z), new XYZ(w.q.X, w.q.Y, z));
                var wall = Wall.Create(doc, line, TypeFor(w.t).Id, bl.Id, U.Mm(tl != null ? U.ToMm(tl.Elevation - bl.Elevation) : heightMm), 0, false, false);
                if (tl != null) wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(tl.Id);
                try { WallUtils.DisallowWallJoinAtEnd(wall, 0); WallUtils.DisallowWallJoinAtEnd(wall, 1); } catch { }
                ids.Add(wall.Id.Value); made++;
            }
            catch { failed++; }
        }
        return new JsonObject { ["walls_created"] = made, ["failed"] = failed, ["wall_types_created"] = types.Count, ["ids"] = new JsonArray(ids.Take(200).Select(x => (JsonNode)x.GetValue<long>()).ToArray()) };
    }
}
