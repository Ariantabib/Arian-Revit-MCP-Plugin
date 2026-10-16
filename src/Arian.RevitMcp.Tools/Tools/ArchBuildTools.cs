using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arian.RevitMcp.Tools;

public static class ArchBuildTools
{
    // ================================================================= floors / ceilings / roofs
    [Tool("create_floor", """
        Creates a floor slab from a boundary polygon (flat, or sloped with slope_percent). Pass openings for holes (e.g. stair/shaft openings).
        @boundary Closed polygon [[x,y],...] in mm (3+ points, no need to repeat the first point).
        @floor_type Floor type name/id (list_element_types category=Floors).
        @level Level the floor sits on. @offset_mm Height offset from the level (negative = below, e.g. -50 for a finish).
        @structural Mark structural (structural floor/slab).
        @openings Optional list of closed polygons (holes) inside the boundary.
        @slope_percent Optional slope (rise/run x 100) applied along slope_direction.
        @slope_direction [dx,dy] direction of the rise (default [0,1]).
        """, Category = "Architecture")]
    public static object CreateFloor(double[][] boundary, string floorType, string level, double offsetMm = 0, bool structural = false,
        double[][][] openings = null, double? slopePercent = null, double[] slopeDirection = null)
    {
        var doc = Ctx.RequireProject();
        var ft = Find.Type<FloorType>(doc, floorType);
        var lv = Find.Level(doc, level);
        double z = lv.Elevation;
        var loops = new List<CurveLoop> { Geo.Loop(boundary.Select(p => U.Pt(p)).ToList(), z) };
        if (openings != null) foreach (var o in openings) loops.Add(Geo.Loop(o.Select(p => U.Pt(p)).ToList(), z));
        Floor f;
        if (slopePercent.HasValue && Math.Abs(slopePercent.Value) > 1e-6)
        {
            var pts = Geo.Clean(boundary.Select(p => U.Pt(p, 0)).ToList(), true);
            var dir = slopeDirection != null ? U.Vec(slopeDirection).Normalize() : XYZ.BasisY;
            // slope arrow: from the lowest point of the boundary along the direction
            var origin = pts.OrderBy(p => p.DotProduct(dir)).First();
            var len = pts.Max(p => p.DotProduct(dir)) - origin.DotProduct(dir);
            var a = new XYZ(origin.X, origin.Y, z);
            var arrow = Line.CreateBound(a, a + dir * Math.Max(len, 1));
            f = Floor.Create(doc, loops, ft.Id, lv.Id, structural, arrow, slopePercent.Value / 100.0);
        }
        else f = Floor.Create(doc, loops, ft.Id, lv.Id);
        if (offsetMm != 0) f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(U.Mm(offsetMm));
        if (structural) f.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.Set(1);
        doc.Regenerate();
        var o2 = Dto.Brief(f);
        o2["area_m2"] = U.R(U.ToM2(f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0), 2);
        return o2;
    }

    [Tool("create_ceiling", """
        Creates a ceiling from a boundary polygon.
        @boundary Polygon [[x,y],...] mm. @ceiling_type Ceiling type name/id. @level Level. @height_offset_mm Height above the level (e.g. 2700).
        """, Category = "Architecture")]
    public static object CreateCeiling(double[][] boundary, string ceilingType, string level, double heightOffsetMm = 2700)
    {
        var doc = Ctx.RequireProject();
        var ct = Find.Type<CeilingType>(doc, ceilingType);
        var lv = Find.Level(doc, level);
        var loop = Geo.Loop(boundary.Select(p => U.Pt(p)).ToList(), lv.Elevation);
        var c = Ceiling.Create(doc, new List<CurveLoop> { loop }, ct.Id, lv.Id);
        c.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM)?.Set(U.Mm(heightOffsetMm));
        return Dto.Brief(c);
    }

    [Tool("create_roof", """
        Creates a footprint roof. All edges get the slope (hip roof) unless sloped_edges lists which edge indices slope (gable roofs: slope only the long edges, e.g. [0,2]). Edge i runs from point i to point i+1.
        @boundary Roof footprint polygon [[x,y],...] mm (include eave overhang in the polygon).
        @roof_type Roof type name/id. @level Base level. @offset_mm Base offset from level (e.g. the wall-top height above that level).
        @slope_degrees Roof pitch in degrees (default 30). @sloped_edges Indices of edges that define slope (default: all). @flat Make a flat roof (no slope).
        """, Category = "Architecture")]
    public static object CreateRoof(double[][] boundary, string roofType, string level, double offsetMm = 0, double slopeDegrees = 30,
        int[] slopedEdges = null, bool flat = false)
    {
        var doc = Ctx.RequireProject();
        var rt = Find.Type<RoofType>(doc, roofType);
        var lv = Find.Level(doc, level);
        var ca = Geo.CurveArray(boundary.Select(p => U.Pt(p)).ToList(), lv.Elevation);
        ModelCurveArray mcs = new ModelCurveArray();
        var roof = doc.Create.NewFootPrintRoof(ca, lv, rt, out mcs);
        int i = 0;
        double tan = Math.Tan(U.Deg(slopeDegrees));
        foreach (ModelCurve mc in mcs)
        {
            bool slope = !flat && (slopedEdges == null || slopedEdges.Contains(i));
            roof.set_DefinesSlope(mc, slope);
            if (slope) roof.set_SlopeAngle(mc, tan);
            i++;
        }
        if (offsetMm != 0) roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.Set(U.Mm(offsetMm));
        doc.Regenerate();
        var o = Dto.Brief(roof);
        o["edges"] = i;
        return o;
    }

    [Tool("create_roof_extrusion", """
        Creates an extruded roof from an OPEN profile drawn in a vertical plane (e.g. a barrel vault or gable ridge profile) swept along the plane normal.
        @plane_origin [x,y] mm: a point on the vertical plane. @plane_direction [dx,dy]: horizontal direction of the plane (profile u axis).
        @profile Open polyline [[u,z],...] in mm: u = distance along plane_direction from plane_origin, z = absolute elevation.
        @extrude_from_mm / @extrude_to_mm Extrusion distances along the plane normal (left of plane_direction positive).
        @roof_type @level.
        """, Category = "Architecture")]
    public static object CreateRoofExtrusion(double[] planeOrigin, double[] planeDirection, double[][] profile, double extrudeFromMm, double extrudeToMm, string roofType, string level)
    {
        var doc = Ctx.RequireProject();
        var rt = Find.Type<RoofType>(doc, roofType);
        var lv = Find.Level(doc, level);
        var o = U.Pt(planeOrigin, 0);
        var dir = U.Vec(planeDirection).Normalize();
        var view = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);
        var rp = doc.Create.NewReferencePlane(o, o + dir, XYZ.BasisZ, view);
        var ca = new CurveArray();
        var pts = profile.Select(p => o + dir * U.Mm(p[0]) + XYZ.BasisZ * U.Mm(p[1])).ToList();
        for (int i = 0; i < pts.Count - 1; i++) ca.Append(Line.CreateBound(pts[i], pts[i + 1]));
        var roof = doc.Create.NewExtrusionRoof(ca, rp, lv, rt, U.Mm(extrudeFromMm), U.Mm(extrudeToMm));
        return Dto.Brief(roof);
    }

    // ================================================================= openings
    [Tool("create_shaft_opening", """
        Creates a vertical shaft opening cutting through floors/roofs/ceilings between two levels (stairs, lifts, MEP shafts).
        @boundary Polygon [[x,y],...] mm. @base_level @top_level Levels the shaft spans. @top_offset_mm Offset above top level.
        """, Category = "Architecture")]
    public static object CreateShaftOpening(double[][] boundary, string baseLevel, string topLevel, double topOffsetMm = 0)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel);
        var ca = Geo.CurveArray(boundary.Select(p => U.Pt(p)).ToList(), bl.Elevation);
        var op = doc.Create.NewOpening(bl, tl, ca);
        if (topOffsetMm != 0) op.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.Set(U.Mm(topOffsetMm));
        return Dto.Brief(op);
    }

    [Tool("create_wall_opening", """
        Cuts a rectangular opening in a wall (without a door/window family).
        @wall_id Host wall. @corner1 / @corner2 [x,y,z] mm: opposite corners of the opening (z = absolute elevation).
        """, Category = "Architecture")]
    public static object CreateWallOpening(long wallId, double[] corner1, double[] corner2)
    {
        var doc = Ctx.RequireProject();
        var w = Find.Element<Wall>(doc, wallId);
        var op = doc.Create.NewOpening(w, U.Pt(corner1), U.Pt(corner2));
        return Dto.Brief(op);
    }

    [Tool("create_floor_opening", "Cuts an opening in an existing floor/roof/ceiling using a polygon (vertical cut).", Category = "Architecture")]
    public static object CreateFloorOpening(long hostId, double[][] boundary, bool perpendicularToFace = false)
    {
        var doc = Ctx.RequireProject();
        var host = Find.Element(doc, hostId);
        var ca = Geo.CurveArray(boundary.Select(p => U.Pt(p)).ToList(), (host.get_BoundingBox(null)?.Min.Z) ?? 0);
        var op = doc.Create.NewOpening(host, ca, perpendicularToFace);
        return Dto.Brief(op);
    }

    // ================================================================= families (doors, windows, furniture, equipment...)
    [Tool("place_family_instance", """
        Places ONE family instance of any kind (door, window, furniture, light, equipment, column, beam, generic model...). Handles level-based, wall-hosted, face-based, line-based and two-level families automatically from the family's placement type.
        Doors/windows: pass host_id (wall) + distance_from_start_mm, or just a point near a wall (auto-detects the wall). Windows: set elevation_mm to the sill height.
        Line-based (beams, braces): pass point (start) and end.
        """, Category = "Architecture")]
    public static object PlaceFamilyInstance(PlaceSpec spec)
    {
        var doc = Ctx.RequireProject();
        var fi = Place.One(doc, spec);
        doc.Regenerate();
        var o = Dto.Brief(fi);
        if (fi.Location is LocationPoint lp) o["point"] = Dto.JsonArr(lp.Point);
        return o;
    }

    [Tool("place_family_instances", "Places MANY family instances in one call (each item as in place_family_instance). Failures are reported per item and do not stop the rest.", Category = "Architecture")]
    public static object PlaceFamilyInstances(PlaceSpec[] items)
    {
        var doc = Ctx.RequireProject();
        var ok = new JsonArray(); var errs = new JsonArray();
        int i = 0;
        foreach (var s in items)
        {
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                var fi = Place.One(doc, s);
                sub.Commit();
                ok.Add(new JsonObject { ["index"] = i, ["id"] = fi.Id.Value, ["type"] = s.Type });
            }
            catch (Exception ex) { sub.RollBack(); errs.Add(new JsonObject { ["index"] = i, ["type"] = s.Type, ["error"] = ex.Message }); }
            i++;
        }
        doc.Regenerate();
        return new JsonObject { ["placed"] = ok.Count, ["failed"] = errs.Count, ["items"] = ok, ["errors"] = errs };
    }

    // ================================================================= rooms
    [Tool("create_room", """
        Creates a room at a point inside an enclosed area (walls/separators must enclose it).
        @point [x,y] mm. @level Level. @name Room name. @number Room number. @department Optional department.
        """, Category = "Architecture")]
    public static object CreateRoom(double[] point, string level, string name = null, string number = null, string department = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var room = doc.Create.NewRoom(lv, new UV(U.Mm(point[0]), U.Mm(point[1])));
        SetRoomProps(room, name, number, department);
        doc.Regenerate();
        return RoomInfo(room);
    }

    static void SetRoomProps(Room room, string name, string number, string department)
    {
        if (!string.IsNullOrWhiteSpace(name)) room.get_Parameter(BuiltInParameter.ROOM_NAME)?.Set(name);
        if (!string.IsNullOrWhiteSpace(number)) room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.Set(number);
        if (!string.IsNullOrWhiteSpace(department)) room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.Set(department);
    }

    static JsonObject RoomInfo(Room r) => new()
    {
        ["id"] = r.Id.Value,
        ["number"] = r.Number,
        ["name"] = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(),
        ["area_m2"] = U.R(U.ToM2(r.Area), 2),
        ["level"] = r.Level?.Name,
        ["enclosed"] = r.Area > 0,
    };

    [Tool("create_rooms_automatically", "Places a room in EVERY enclosed area of a level that has no room yet.", Category = "Architecture")]
    public static object CreateRoomsAutomatically(string level)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var ids = doc.Create.NewRooms2(lv);
        doc.Regenerate();
        var arr = new JsonArray();
        foreach (var id in ids) if (doc.GetElement(id) is Room r) arr.Add(RoomInfo(r));
        return new JsonObject { ["created"] = arr.Count, ["rooms"] = arr };
    }

    public sealed class RoomSpec
    {
        public double[] Point; public string Name; public string Number; public string Department;
    }

    [Tool("create_rooms", "Creates several named rooms at once: [{point:[x,y], name, number, department}] on one level.", Category = "Architecture")]
    public static object CreateRooms(string level, RoomSpec[] rooms)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var ok = new JsonArray(); var errs = new JsonArray();
        foreach (var s in rooms)
        {
            try
            {
                var r = doc.Create.NewRoom(lv, new UV(U.Mm(s.Point[0]), U.Mm(s.Point[1])));
                SetRoomProps(r, s.Name, s.Number, s.Department);
                ok.Add(RoomInfo(r));
            }
            catch (Exception ex) { errs.Add(new JsonObject { ["name"] = s.Name, ["error"] = ex.Message }); }
        }
        doc.Regenerate();
        return new JsonObject { ["created"] = ok.Count, ["rooms"] = ok, ["errors"] = errs };
    }

    [Tool("create_room_separation", "Draws room separation lines (an invisible boundary) along a polyline on a level, to subdivide open plans into rooms.", Category = "Architecture")]
    public static object CreateRoomSeparation(string level, double[][] points, bool closed = false)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var view = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel?.Id == lv.Id)
                   ?? throw new ToolException("No floor plan exists for this level (needed to draw room separation lines). Use create_floor_plan.");
        var sp = SketchPlane.Create(doc, lv.Id);
        var ca = new CurveArray();
        foreach (var c in Geo.Polyline(points.Select(p => U.Pt(p, U.ToMm(lv.Elevation))).ToList(), closed)) ca.Append(c);
        var created = doc.Create.NewRoomBoundaryLines(sp, ca, view);
        return new JsonObject { ["created"] = created.Size };
    }

    [Tool("tag_rooms", "Places room tags on all placed rooms of a level in its floor plan.", Category = "Architecture")]
    public static object TagRooms(string level, string view = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var v = view != null ? Find.View(doc, view) : new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(p => !p.IsTemplate && p.ViewType == ViewType.FloorPlan && p.GenLevel?.Id == lv.Id) ?? throw new ToolException("No floor plan for that level.");
        int n = 0;
        foreach (Room r in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).Cast<Room>())
        {
            if (r.Location is not LocationPoint lp) continue;
            doc.Create.NewRoomTag(new LinkElementId(r.Id), new UV(lp.Point.X, lp.Point.Y), v.Id);
            n++;
        }
        return new JsonObject { ["tagged"] = n };
    }

    [Tool("set_room_properties", "Sets name/number/department/occupancy/finish parameters on rooms.", Category = "Architecture")]
    public static object SetRoomProperties(long[] ids, string name = null, string number = null, string department = null, Dictionary<string, JsonNode> parameters = null)
    {
        var doc = Ctx.RequireProject();
        foreach (var id in ids)
        {
            var r = Find.Element<Room>(doc, id);
            SetRoomProps(r, name, number, department);
            if (parameters != null) Par.SetMany(r, parameters);
        }
        return new JsonObject { ["updated"] = ids.Length };
    }

    [Tool("get_room_boundary", "Returns a room's boundary segments (mm) — to place furniture, floors, ceilings or walls that follow the room.", Category = "Query", ReadOnly = true)]
    public static object GetRoomBoundary(long roomId)
    {
        var doc = Ctx.RequireProject();
        var r = Find.Element<SpatialElement>(doc, roomId);
        var loops = new JsonArray();
        var segs = r.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish });
        if (segs != null)
            foreach (var loop in segs)
            {
                var pts = new JsonArray();
                foreach (var s in loop) pts.Add(Dto.JsonArr(s.GetCurve().GetEndPoint(0)));
                loops.Add(pts);
            }
        var bb = r.get_BoundingBox(null);
        return new JsonObject { ["id"] = roomId, ["area_m2"] = U.R(U.ToM2(r.Area), 2), ["boundary_loops"] = loops, ["bbox_mm"] = Dto.BBox(bb) };
    }

    // ================================================================= stairs & railings
    [Tool("create_stairs", """
        Creates a component stair between two levels made of straight runs connected by automatic landings. Provide 1 run for a straight stair, 2 for L/U shaped, etc. Each run is a baseline from start to end (mm); the stair climbs from base_level to top_level.
        IMPORTANT geometry rule: a run's baseline length = (risers in that run - 1) x tread depth. The total riser count must be about floor_height / riser_height (e.g. 3200 mm / 170 mm = 19 risers => about 18 x 280 = 5040 mm of runs). Revit warns when risers come out too tall. For a plain straight stair prefer create_straight_stairs, which computes the length for you.
        @runs List of {start:[x,y], end:[x,y]} runs in walking order (the first run starts at the base level).
        @base_level @top_level Levels connected. @stairs_type Stair type name (optional). @width_mm Run width (default 1100).
        @max_riser_mm / @tread_depth_mm Applied to a duplicated stair type (default 175 / 280).
        """, Category = "Architecture", NoTransaction = true)]
    public static object CreateStairs(StairRun[] runs, string baseLevel, string topLevel, string stairsType = null, double widthMm = 1100,
        double maxRiserMm = 175, double treadDepthMm = 280)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel);
        var lines = runs.Select(r => (Line.CreateBound(U.Pt(r.Start, U.ToMm(bl.Elevation)), U.Pt(r.End, U.ToMm(bl.Elevation))), r.WidthMm ?? widthMm)).ToList();
        return BuildStairs(doc, lines, bl, tl, stairsType, maxRiserMm, treadDepthMm);
    }

    [Tool("create_straight_stairs", """
        Creates a straight stair and computes everything for you: riser count from the floor height, run length from the tread depth. The stair starts at 'start' (bottom riser) and climbs in direction_deg.
        @start [x,y] mm of the bottom of the stair. @direction_deg Walking direction in plan, degrees CCW from +X (0 = east, 90 = north).
        @base_level @top_level Levels. @width_mm Stair width. @riser_mm Target riser height (default 170). @tread_mm Tread depth (default 280).
        @stairs_type Optional stair type to base on.
        """, Category = "Architecture", NoTransaction = true)]
    public static object CreateStraightStairs(double[] start, double directionDeg, string baseLevel, string topLevel, double widthMm = 1100,
        double riserMm = 170, double treadMm = 280, string stairsType = null)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel);
        double heightMm = U.ToMm(tl.Elevation - bl.Elevation);
        int n = (int)Math.Ceiling(heightMm / riserMm - 1e-6);
        double lengthMm = (n - 1) * treadMm;
        var a = U.Pt(start, U.ToMm(bl.Elevation));
        var dir = new XYZ(Math.Cos(U.Deg(directionDeg)), Math.Sin(U.Deg(directionDeg)), 0);
        var b = a + dir * U.Mm(lengthMm);
        return BuildStairs(doc, new List<(Line, double)> { (Line.CreateBound(a, b), widthMm) }, bl, tl, stairsType, Math.Max(riserMm + 5, heightMm / n + 1), treadMm);
    }

    internal static object BuildStairs(Document doc, List<(Line line, double widthMm)> runs, Level bl, Level tl, string stairsType, double maxRiserMm, double treadMm)
    {
        ElementId stairsId = null;
        string note = null;
        ElementType baseType = stairsType != null ? Find.Type<StairsType>(doc, stairsType) : null;
        using (var scope = new StairsEditScope(doc, "Arian MCP: create stairs"))
        {
            stairsId = scope.Start(bl.Id, tl.Id);
            using (var t = new Transaction(doc, "stairs runs"))
            {
                t.Start();
                var stairs = (Stairs)doc.GetElement(stairsId);
                // duplicate the stair type so the riser/tread rules match the requested geometry without changing the shared type
                var st = (StairsType)(baseType ?? doc.GetElement(stairs.GetTypeId()));
                string nm = $"Arian Stair {maxRiserMm:0}x{treadMm:0}";
                var tgt = new FilteredElementCollector(doc).OfClass(typeof(StairsType)).Cast<StairsType>().FirstOrDefault(x => x.Name == nm) ?? (StairsType)st.Duplicate(nm);
                try { tgt.MaxRiserHeight = U.Mm(maxRiserMm); tgt.MinTreadDepth = U.Mm(treadMm); } catch { }
                stairs.ChangeTypeId(tgt.Id);
                try { stairs.DesiredRisersNumber = (int)Math.Ceiling(U.ToMm(tl.Elevation - bl.Elevation) / (maxRiserMm - 5) - 1e-6); } catch { }
                var runIds = new List<ElementId>();
                StairsRun prevRun = null;
                foreach (var (line, w) in runs)
                {
                    var run = StairsRun.CreateStraightRun(doc, stairsId, line, StairsRunJustification.Center);
                    try { run.ActualRunWidth = U.Mm(w); } catch { }
                    if (prevRun != null)
                    {
                        // a following run must start at the elevation where the previous one ends (landing height)
                        try
                        {
                            double h2 = run.TopElevation - run.BaseElevation;
                            run.TopElevation = prevRun.TopElevation + h2;
                            run.BaseElevation = prevRun.TopElevation;
                        }
                        catch (Exception ex) { note = "Run elevation: " + ex.Message; }
                    }
                    prevRun = run;
                    runIds.Add(run.Id);
                }
                for (int i = 0; i + 1 < runIds.Count; i++)
                {
                    try { StairsLanding.CreateAutomaticLanding(doc, runIds[i], runIds[i + 1]); }
                    catch (Exception ex) { note = "Automatic landing between runs " + i + " and " + (i + 1) + " failed: " + ex.Message; }
                }
                t.Commit();
            }
            scope.Commit(new StairsFailureHandler());
        }
        var s2 = (Stairs)doc.GetElement(stairsId);
        var o = Dto.Brief(s2);
        o["risers"] = s2.ActualRisersNumber;
        o["riser_height_mm"] = U.R(U.ToMm(s2.ActualRiserHeight), 1);
        o["tread_depth_mm"] = U.R(U.ToMm(s2.ActualTreadDepth), 1);
        o["runs"] = s2.GetStairsRuns().Count;
        double needMm = Math.Ceiling(U.ToMm(tl.Elevation - bl.Elevation) / maxRiserMm);
        if (U.ToMm(s2.ActualRiserHeight) > maxRiserMm + 1)
            note = $"Riser height {U.ToMm(s2.ActualRiserHeight):0} mm exceeds {maxRiserMm:0} mm: the run(s) are too short. You need about {needMm} risers, i.e. total run length ≈ {(needMm - 1) * treadMm:0} mm (excluding landings)." + (note != null ? " " + note : "");
        if (note != null) o["note"] = note;
        return o;
    }

    public sealed class StairRun
    {
        [Desc("[x,y] mm start of run baseline")] public double[] Start;
        [Desc("[x,y] mm end of run baseline")] public double[] End;
        [Desc("Optional run width mm")] public double? WidthMm;
    }

    sealed class StairsFailureHandler : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            foreach (var m in fa.GetFailureMessages())
                if (m.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(m);
            return FailureProcessingResult.Continue;
        }
    }

    [Tool("create_railing", """
        Creates a railing/guardrail/balustrade along a path (balconies, edges, stair landings), or on a host stair/ramp.
        @path Polyline [[x,y],...] mm (open chain). @railing_type Railing type name. @level Level.
        @host_id Alternative: stair/ramp element id to add railings to (path ignored).
        @elevation_mm Height offset of the path above the level. @closed Close the path.
        """, Category = "Architecture")]
    public static object CreateRailing(string railingType, string level = null, double[][] path = null, long? hostId = null, double elevationMm = 0, bool closed = false)
    {
        var doc = Ctx.RequireProject();
        var rt = Find.Type<RailingType>(doc, railingType);
        if (hostId.HasValue)
        {
            var ids = Railing.Create(doc, new ElementId(hostId.Value), rt.Id, RailingPlacementPosition.Treads);
            return new JsonObject { ["railing_ids"] = Dto.IdList(ids) };
        }
        var lv = Find.Level(doc, level);
        var curves = Geo.Polyline(path.Select(p => U.Pt(p, U.ToMm(lv.Elevation) + elevationMm)).ToList(), closed);
        var loop = new CurveLoop();
        foreach (var c in curves) loop.Append(c);
        var r = Railing.Create(doc, loop, rt.Id, lv.Id);
        return Dto.Brief(r);
    }

    // ================================================================= massing & direct geometry
    [Tool("create_mass_extrusion", """
        Creates a solid extruded from a polygon as a DirectShape — ideal for concept massing, site volumes, or any geometry without a family. Appears in the chosen category (Generic Models by default, or Mass).
        @boundary Polygon [[x,y],...] mm. @base_z_mm Base elevation. @height_mm Extrusion height.
        @category Category name (default 'Generic Models'; try 'Mass', 'Walls', 'Floors', 'Roofs', 'Furniture' ...).
        @name Name/Mark. @material Optional material name applied to all faces.
        """, Category = "Architecture")]
    public static object CreateMassExtrusion(double[][] boundary, double heightMm, double baseZMm = 0, string category = "Generic Models", string name = null, string material = null)
    {
        var doc = Ctx.RequireProject();
        var pts = boundary.Select(p => U.Pt(p)).ToList();
        var loop = Geo.Loop(pts, U.Mm(baseZMm));
        var solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, U.Mm(heightMm));
        var cat = Find.Category(doc, category);
        var ds = DirectShape.CreateElement(doc, cat.Id);
        ds.SetShape(new List<GeometryObject> { solid });
        if (!string.IsNullOrWhiteSpace(name)) { ds.Name = name; ds.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(name); }
        if (!string.IsNullOrWhiteSpace(material)) PaintAll(doc, ds, Find.Material(doc, material));
        var o = Dto.Brief(ds);
        o["volume_m3"] = U.R(U.ToM3(solid.Volume), 3);
        return o;
    }

    public static void PaintAll(Document doc, Element e, Material m)
    {
        var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        foreach (var go in e.get_Geometry(opts))
            if (go is Solid s)
                foreach (Face f in s.Faces) { try { doc.Paint(e.Id, f, m.Id); } catch { } }
    }

    [Tool("create_mesh_shape", """
        Creates arbitrary 3D geometry from a triangle/quad mesh as a DirectShape (imported sculpted forms, roofs, terrain chunks...).
        @vertices [[x,y,z],...] mm. @faces Index lists into vertices, each face 3 or 4 indices, counter-clockwise seen from outside (e.g. [[0,1,2],[0,2,3]]).
        @category Category (default Generic Models). @name Name. @material Material name.
        """, Category = "Architecture")]
    public static object CreateMeshShape(double[][] vertices, int[][] faces, string category = "Generic Models", string name = null, string material = null)
    {
        var doc = Ctx.RequireProject();
        var vs = vertices.Select(v => U.Pt(v)).ToList();
        var b = new TessellatedShapeBuilder { Target = TessellatedShapeBuilderTarget.AnyGeometry, Fallback = TessellatedShapeBuilderFallback.Mesh };
        ElementId matId = string.IsNullOrWhiteSpace(material) ? ElementId.InvalidElementId : Find.Material(doc, material).Id;
        b.OpenConnectedFaceSet(false);
        foreach (var f in faces) b.AddFace(new TessellatedFace(f.Select(i => vs[i]).ToList(), matId));
        b.CloseConnectedFaceSet();
        b.Build();
        var res = b.GetBuildResult();
        var ds = DirectShape.CreateElement(doc, Find.Category(doc, category).Id);
        ds.SetShape(res.GetGeometricalObjects());
        if (!string.IsNullOrWhiteSpace(name)) ds.Name = name;
        return Dto.Brief(ds);
    }

    // ================================================================= site
    [Tool("create_toposurface", """
        Creates a topography surface from 3D points (site terrain).
        @points [[x,y,z],...] mm (3+ points, z = terrain elevation).
        """, Category = "Architecture")]
    public static object CreateToposurface(double[][] points)
    {
        var doc = Ctx.RequireProject();
        var t = TopographySurface.Create(doc, points.Select(p => U.Pt(p)).ToList());
        return Dto.Brief(t);
    }

    [Tool("create_building_pad", "Creates a building pad (flat graded area cut into topography) from a polygon at a level.", Category = "Architecture")]
    public static object CreateBuildingPad(double[][] boundary, string level, string padType = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var pt = padType != null ? Find.Type<BuildingPadType>(doc, padType) : new FilteredElementCollector(doc).OfClass(typeof(BuildingPadType)).Cast<BuildingPadType>().FirstOrDefault()
                 ?? throw new ToolException("No building pad type found.");
        var loop = Geo.Loop(boundary.Select(p => U.Pt(p)).ToList(), lv.Elevation);
        var pad = BuildingPad.Create(doc, pt.Id, lv.Id, new List<CurveLoop> { loop });
        return Dto.Brief(pad);
    }
}
