using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

public sealed class LevelSpec
{
    [Desc("Level name, e.g. 'Level 2' or 'L02 - First Floor'")] public string Name;
    [Desc("Elevation in mm above the project base point")] public double ElevationMm;
}

public static class ArchBasicTools
{
    // ================================================================= levels
    [Tool("create_level", """
        Creates a level at an elevation. Optionally creates the matching floor plan and ceiling plan views.
        @name Level name.
        @elevation_mm Elevation in mm.
        @create_plan_views Also create a floor plan view (and ceiling plan if create_ceiling_plan).
        @create_ceiling_plan Also create a reflected ceiling plan.
        """, Category = "Architecture")]
    public static object CreateLevel(string name, double elevationMm, bool createPlanViews = true, bool createCeilingPlan = false)
    {
        var doc = Ctx.RequireProject();
        var lv = MakeLevel(doc, name, elevationMm, createPlanViews, createCeilingPlan);
        return new JsonObject { ["id"] = lv.Id.Value, ["name"] = lv.Name, ["elevation_mm"] = U.R(U.ToMm(lv.Elevation), 1) };
    }

    static Level MakeLevel(Document doc, string name, double elevMm, bool plans, bool ceiling)
    {
        var existing = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Elevation = U.Mm(elevMm);
            return existing;
        }
        var lv = Level.Create(doc, U.Mm(elevMm));
        lv.Name = name;
        if (plans)
        {
            var fpType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
            if (fpType != null) { try { var v = ViewPlan.Create(doc, fpType.Id, lv.Id); } catch { } }
            if (ceiling)
            {
                var cpType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.CeilingPlan);
                if (cpType != null) { try { ViewPlan.Create(doc, cpType.Id, lv.Id); } catch { } }
            }
        }
        return lv;
    }

    [Tool("create_levels", """
        Creates several levels at once (creates floor plan views for each).
        @levels List of {name, elevation_mm}.
        @create_ceiling_plans Also create ceiling plans.
        """, Category = "Architecture")]
    public static object CreateLevels(LevelSpec[] levels, bool createCeilingPlans = false)
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var l in levels)
        {
            var lv = MakeLevel(doc, l.Name, l.ElevationMm, true, createCeilingPlans);
            arr.Add(new JsonObject { ["id"] = lv.Id.Value, ["name"] = lv.Name, ["elevation_mm"] = U.R(U.ToMm(lv.Elevation), 1) });
        }
        return new JsonObject { ["levels"] = arr };
    }

    [Tool("create_building_levels", """
        Generates a stack of storey levels: ground + N floors with constant floor-to-floor height (optionally a different ground-floor height) and plan views.
        @floors Number of levels to create (including ground).
        @floor_height_mm Typical floor-to-floor height (default 3200).
        @first_floor_height_mm Height from level 1 to level 2 if different.
        @base_elevation_mm Elevation of the first level.
        @name_pattern Pattern with {n} (1-based) and {nn} (zero padded), e.g. 'Level {n}' or 'L{nn}'.
        @include_roof Also add a 'Roof' level on top at the same height.
        """, Category = "Architecture")]
    public static object CreateBuildingLevels(int floors, double floorHeightMm = 3200, double? firstFloorHeightMm = null,
        double baseElevationMm = 0, string namePattern = "Level {n}", bool includeRoof = false)
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        double z = baseElevationMm;
        for (int i = 1; i <= floors; i++)
        {
            string nm = namePattern.Replace("{nn}", i.ToString("00")).Replace("{n}", i.ToString());
            var lv = MakeLevel(doc, nm, z, true, false);
            arr.Add(new JsonObject { ["id"] = lv.Id.Value, ["name"] = lv.Name, ["elevation_mm"] = U.R(z, 1) });
            z += (i == 1 && firstFloorHeightMm.HasValue) ? firstFloorHeightMm.Value : floorHeightMm;
        }
        if (includeRoof)
        {
            var lv = MakeLevel(doc, "Roof", z, true, false);
            arr.Add(new JsonObject { ["id"] = lv.Id.Value, ["name"] = lv.Name, ["elevation_mm"] = U.R(z, 1) });
        }
        return new JsonObject { ["levels"] = arr };
    }

    [Tool("modify_level", "Renames a level and/or changes its elevation.", Category = "Architecture")]
    public static object ModifyLevel(string level, string newName = null, double? elevationMm = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        if (elevationMm.HasValue) lv.Elevation = U.Mm(elevationMm.Value);
        if (!string.IsNullOrWhiteSpace(newName)) lv.Name = newName;
        return new JsonObject { ["id"] = lv.Id.Value, ["name"] = lv.Name, ["elevation_mm"] = U.R(U.ToMm(lv.Elevation), 1) };
    }

    // ================================================================= grids
    [Tool("create_grid", """
        Creates a straight grid line.
        @start [x,y] mm.  @end [x,y] mm.  @name Grid name/label (e.g. 'A' or '1').
        """, Category = "Architecture")]
    public static object CreateGrid(double[] start, double[] end, string name = null)
    {
        var doc = Ctx.RequireProject();
        var g = Grid.Create(doc, Line.CreateBound(U.Pt(start, 0), U.Pt(end, 0)));
        if (!string.IsNullOrWhiteSpace(name)) g.Name = name;
        return new JsonObject { ["id"] = g.Id.Value, ["name"] = g.Name };
    }

    [Tool("create_grid_arc", """
        Creates a curved (arc) grid.
        @center [x,y] mm. @radius_mm Radius. @start_angle_deg / @end_angle_deg Angles (CCW from +X).
        """, Category = "Architecture")]
    public static object CreateGridArc(double[] center, double radiusMm, double startAngleDeg, double endAngleDeg, string name = null)
    {
        var doc = Ctx.RequireProject();
        var c = U.Pt(center, 0);
        var arc = Arc.Create(c, U.Mm(radiusMm), U.Deg(startAngleDeg), U.Deg(endAngleDeg), XYZ.BasisX, XYZ.BasisY);
        var g = Grid.Create(doc, arc);
        if (!string.IsNullOrWhiteSpace(name)) g.Name = name;
        return new JsonObject { ["id"] = g.Id.Value, ["name"] = g.Name };
    }

    [Tool("create_grid_system", """
        Creates a complete orthogonal structural/architectural grid in one call.
        Vertical grid lines (running along Y) are positioned at the x_spacings_mm cumulative from origin and labelled with x_labels (default 1,2,3...); horizontal lines (along X) at y_spacings_mm labelled y_labels (default A,B,C...).
        Example: x_spacings_mm=[6000,6000,6000], y_spacings_mm=[5000,5000] gives a 4x3 grid.
        @x_spacings_mm Bay widths along X (N bays => N+1 lines).
        @y_spacings_mm Bay widths along Y.
        @origin [x,y] mm of the first intersection.
        @x_labels Optional labels for vertical lines.  @y_labels Optional labels for horizontal lines.
        @extension_mm How far lines extend beyond the outermost grid (default 2000).
        """, Category = "Architecture")]
    public static object CreateGridSystem(double[] xSpacingsMm, double[] ySpacingsMm, double[] origin = null,
        string[] xLabels = null, string[] yLabels = null, double extensionMm = 2000)
    {
        var doc = Ctx.RequireProject();
        double ox = origin?[0] ?? 0, oy = origin?[1] ?? 0;
        var xs = new List<double> { ox }; foreach (var s in xSpacingsMm) xs.Add(xs[^1] + s);
        var ys = new List<double> { oy }; foreach (var s in ySpacingsMm) ys.Add(ys[^1] + s);
        double x0 = xs[0] - extensionMm, x1 = xs[^1] + extensionMm, y0 = ys[0] - extensionMm, y1 = ys[^1] + extensionMm;
        var arr = new JsonArray();
        for (int i = 0; i < xs.Count; i++)
        {
            var g = Grid.Create(doc, Line.CreateBound(U.Pt(new[] { xs[i], y0 }, 0), U.Pt(new[] { xs[i], y1 }, 0)));
            string nm = xLabels != null && i < xLabels.Length ? xLabels[i] : (i + 1).ToString();
            TrySetName(g, nm);
            arr.Add(new JsonObject { ["id"] = g.Id.Value, ["name"] = g.Name, ["x_mm"] = xs[i] });
        }
        for (int j = 0; j < ys.Count; j++)
        {
            var g = Grid.Create(doc, Line.CreateBound(U.Pt(new[] { x0, ys[j] }, 0), U.Pt(new[] { x1, ys[j] }, 0)));
            string nm = yLabels != null && j < yLabels.Length ? yLabels[j] : ((char)('A' + j % 26)).ToString() + (j >= 26 ? (j / 26).ToString() : "");
            TrySetName(g, nm);
            arr.Add(new JsonObject { ["id"] = g.Id.Value, ["name"] = g.Name, ["y_mm"] = ys[j] });
        }
        return new JsonObject { ["grids"] = arr };
    }

    static void TrySetName(Grid g, string name)
    {
        try { g.Name = name; }
        catch { try { g.Name = name + "_"; } catch { } }
    }

    // ================================================================= walls
    static Wall MakeWall(Document doc, Curve curve, WallType wt, Level baseLv, Level topLv, double? heightMm,
        double baseOffsetMm, double topOffsetMm, string locationLine, bool structural, bool flip)
    {
        double h = heightMm.HasValue ? U.Mm(heightMm.Value) : (topLv != null ? Math.Max(topLv.Elevation - baseLv.Elevation, U.Mm(100)) : U.Mm(3000));
        var w = Wall.Create(doc, curve, wt.Id, baseLv.Id, h, U.Mm(baseOffsetMm), flip, structural);
        if (topLv != null && !heightMm.HasValue)
        {
            w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(topLv.Id);
            w.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.Set(U.Mm(topOffsetMm));
        }
        if (!string.IsNullOrWhiteSpace(locationLine))
        {
            int v = LocationLineValue(locationLine);
            w.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.Set(v);
        }
        return w;
    }

    static int LocationLineValue(string s) => s.Trim().ToLowerInvariant().Replace(' ', '_') switch
    {
        "wall_centerline" or "centerline" or "center" => 0,
        "core_centerline" => 1,
        "finish_face_exterior" or "exterior" or "finish_exterior" => 2,
        "finish_face_interior" or "interior" or "finish_interior" => 3,
        "core_face_exterior" => 4,
        "core_face_interior" => 5,
        _ => throw new ToolException("location_line must be one of: wall_centerline, core_centerline, finish_face_exterior, finish_face_interior, core_face_exterior, core_face_interior"),
    };

    static JsonObject WallInfo(Wall w) => new()
    {
        ["id"] = w.Id.Value,
        ["type"] = w.WallType.Name,
        ["length_mm"] = U.R(U.ToMm(((LocationCurve)w.Location).Curve.Length), 1),
        ["height_mm"] = U.R(U.ToMm(w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0), 1),
        ["level"] = w.Document.GetElement(w.LevelId)?.Name,
    };

    [Tool("create_wall", """
        Creates ONE straight wall. For many connected walls use create_walls.
        Height: give top_level (wall spans base_level -> top_level, plus offsets) OR height_mm (unconnected height).
        @start [x,y] mm. @end [x,y] mm.
        @wall_type Wall type name ('Generic - 200mm', 'Basic Wall: Generic - 200mm') or id. Use list_element_types category=Walls.
        @base_level Base constraint level. @top_level Top constraint level (optional).
        @height_mm Unconnected height if no top_level (default 3000).
        @base_offset_mm Offset from base level. @top_offset_mm Offset from top level.
        @location_line wall_centerline (default), core_centerline, finish_face_exterior, finish_face_interior, core_face_exterior, core_face_interior.
        @structural Mark as structural wall (load-bearing, for the structural discipline).
        @flip Flip wall orientation (swaps exterior/interior side).
        """, Category = "Architecture")]
    public static object CreateWall(double[] start, double[] end, string wallType, string baseLevel, string topLevel = null,
        double? heightMm = null, double baseOffsetMm = 0, double topOffsetMm = 0, string locationLine = null,
        bool structural = false, bool flip = false)
    {
        var doc = Ctx.RequireProject();
        var wt = Find.Type<WallType>(doc, wallType);
        var bl = Find.Level(doc, baseLevel);
        var tl = Find.Level(doc, topLevel, true);
        if (tl == null && !heightMm.HasValue) heightMm = 3000;
        var curve = Line.CreateBound(U.Pt(start, U.ToMm(bl.Elevation)), U.Pt(end, U.ToMm(bl.Elevation)));
        var w = MakeWall(doc, curve, wt, bl, tl, heightMm, baseOffsetMm, topOffsetMm, locationLine, structural, flip);
        return WallInfo(w);
    }

    [Tool("create_walls", """
        Creates a chain of connected walls through the given points (polyline). Set closed=true to close the loop (e.g. a room outline or building perimeter). Corners are auto-joined by Revit.
        @points Ordered [x,y] points in mm.
        @closed Close the loop back to the first point.
        @wall_type, @base_level, @top_level, @height_mm, @base_offset_mm, @top_offset_mm, @location_line, @structural: same meaning as create_wall.
        """, Category = "Architecture")]
    public static object CreateWalls(double[][] points, string wallType, string baseLevel, string topLevel = null,
        double? heightMm = null, bool closed = false, double baseOffsetMm = 0, double topOffsetMm = 0,
        string locationLine = null, bool structural = false)
    {
        var doc = Ctx.RequireProject();
        var wt = Find.Type<WallType>(doc, wallType);
        var bl = Find.Level(doc, baseLevel);
        var tl = Find.Level(doc, topLevel, true);
        if (tl == null && !heightMm.HasValue) heightMm = 3000;
        var pts = Geo.Clean(points.Select(p => U.Pt(p, U.ToMm(bl.Elevation))).ToList(), closed);
        if (pts.Count < 2) throw new ToolException("Need at least 2 distinct points.");
        var curves = Geo.Polyline(pts, closed);
        var arr = new JsonArray();
        foreach (var c in curves)
            arr.Add(WallInfo(MakeWall(doc, c, wt, bl, tl, heightMm, baseOffsetMm, topOffsetMm, locationLine, structural, false)));
        doc.Regenerate();
        return new JsonObject { ["count"] = arr.Count, ["walls"] = arr };
    }

    [Tool("create_curved_wall", """
        Creates an arc wall.
        @center [x,y] mm. @radius_mm @start_angle_deg @end_angle_deg (CCW from +X).
        Other args as create_wall.
        """, Category = "Architecture")]
    public static object CreateCurvedWall(double[] center, double radiusMm, double startAngleDeg, double endAngleDeg, string wallType,
        string baseLevel, string topLevel = null, double? heightMm = null, double baseOffsetMm = 0, double topOffsetMm = 0,
        string locationLine = null, bool structural = false)
    {
        var doc = Ctx.RequireProject();
        var wt = Find.Type<WallType>(doc, wallType);
        var bl = Find.Level(doc, baseLevel);
        var tl = Find.Level(doc, topLevel, true);
        if (tl == null && !heightMm.HasValue) heightMm = 3000;
        var arc = Arc.Create(U.Pt(center, U.ToMm(bl.Elevation)), U.Mm(radiusMm), U.Deg(startAngleDeg), U.Deg(endAngleDeg), XYZ.BasisX, XYZ.BasisY);
        return WallInfo(MakeWall(doc, arc, wt, bl, tl, heightMm, baseOffsetMm, topOffsetMm, locationLine, structural, false));
    }

    [Tool("create_wall_by_profile", """
        Creates a wall from a vertical profile (gable ends, trapezoids, stepped walls). Profile points are given in the wall plane as [u, z]: u = distance along the wall direction from start (mm), z = absolute elevation (mm). The profile must be a closed polygon.
        @start [x,y] mm - start of the wall line. @end [x,y] mm - end of the wall line.
        @profile Polygon [[u,z],...] in mm.
        @wall_type @level @structural: as create_wall.
        """, Category = "Architecture")]
    public static object CreateWallByProfile(double[] start, double[] end, double[][] profile, string wallType, string level, bool structural = false)
    {
        var doc = Ctx.RequireProject();
        var wt = Find.Type<WallType>(doc, wallType);
        var lv = Find.Level(doc, level);
        var s = U.Pt(start, 0); var e = U.Pt(end, 0);
        var dir = (e - s).Normalize();
        var pts = profile.Select(p => s + dir * U.Mm(p[0]) + XYZ.BasisZ * U.Mm(p[1])).ToList();
        var curves = Geo.Polyline(pts, true);
        var w = Wall.Create(doc, curves, wt.Id, lv.Id, structural);
        return WallInfo(w);
    }

    [Tool("set_wall_constraints", """
        Edits wall height/constraints/location line of existing walls.
        @ids Wall ids.
        @base_level, @top_level, @height_mm (unconnected), @base_offset_mm, @top_offset_mm, @location_line, @structural, @room_bounding.
        """, Category = "Architecture")]
    public static object SetWallConstraints(long[] ids, string baseLevel = null, string topLevel = null, double? heightMm = null,
        double? baseOffsetMm = null, double? topOffsetMm = null, string locationLine = null, bool? structural = null, bool? roomBounding = null)
    {
        var doc = Ctx.RequireProject();
        foreach (var id in ids)
        {
            var w = Find.Element<Wall>(doc, id);
            if (baseLevel != null) w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).Set(Find.Level(doc, baseLevel).Id);
            if (topLevel != null) w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(Find.Level(doc, topLevel).Id);
            if (heightMm.HasValue)
            {
                w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(ElementId.InvalidElementId);
                w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).Set(U.Mm(heightMm.Value));
            }
            if (baseOffsetMm.HasValue) w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).Set(U.Mm(baseOffsetMm.Value));
            if (topOffsetMm.HasValue) w.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET).Set(U.Mm(topOffsetMm.Value));
            if (locationLine != null) w.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM).Set(LocationLineValue(locationLine));
            if (structural.HasValue) w.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT).Set(structural.Value ? 1 : 0);
            if (roomBounding.HasValue) w.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.Set(roomBounding.Value ? 1 : 0);
        }
        doc.Regenerate();
        return new JsonObject { ["updated"] = ids.Length };
    }

    [Tool("attach_wall", """
        Attaches the top (or base) of walls to a roof, floor, ceiling or level-based element so they follow its shape (e.g. walls up to a sloped roof).
        @wall_ids Walls. @target_id Roof/floor/ceiling element. @location top | base.
        """, Category = "Architecture")]
    public static object AttachWall(long[] wallIds, long targetId, string location = "top")
    {
        var doc = Ctx.RequireProject();
        var loc = location.Equals("base", StringComparison.OrdinalIgnoreCase) ? AttachmentLocation.Base : AttachmentLocation.Top;
        int n = 0;
        foreach (var id in wallIds)
        {
            var w = Find.Element<Wall>(doc, id);
            if (Wall.IsValidTargetAttachment(doc, new ElementId(targetId))) { w.AddAttachment(new ElementId(targetId), loc); n++; }
        }
        return new JsonObject { ["attached"] = n };
    }

    [Tool("join_walls", """
        Controls how a wall end joins its neighbour at a corner.
        @wall_id Wall. @end 0 (start) or 1 (end). @join_type miter | butt | square_off (default miter). Use allow=false to disallow auto-join at that end.
        """, Category = "Architecture")]
    public static object JoinWalls(long wallId, int end = 0, string joinType = "miter", bool allow = true)
    {
        var doc = Ctx.RequireProject();
        var w = Find.Element<Wall>(doc, wallId);
        if (!allow) { WallUtils.DisallowWallJoinAtEnd(w, end); return new JsonObject { ["ok"] = true }; }
        WallUtils.AllowWallJoinAtEnd(w, end);
        var jt = joinType.ToLowerInvariant() switch { "butt" => JoinType.Abut, "square_off" or "square" => JoinType.SquareOff, _ => JoinType.Miter };
        try { var lc = (LocationCurve)w.Location; lc.set_JoinType(end, jt); } catch { }
        return new JsonObject { ["ok"] = true };
    }
}
