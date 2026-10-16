using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arian.RevitMcp.Tools;

public sealed class RoomRect
{
    [Desc("Room name e.g. 'Living Room'")] public string Name;
    [Desc("Room number e.g. '101'")] public string Number;
    [Desc("Lower-left corner [x,y] mm")] public double[] Origin;
    [Desc("Size along X in mm")] public double WidthMm;
    [Desc("Size along Y in mm")] public double DepthMm;
    public string Department;
}

public static class ArchLayoutTools
{
    [Tool("create_plan_from_rooms", """
        Turns a room PROGRAM expressed as rectangles into a modelled plan: walls around/between all rectangles (shared edges become ONE wall, overlaps are merged), then a named Room in each rectangle. Ideal for schematic layouts: describe each room as origin + width x depth.
        Exterior walls (edges used by one room) use exterior_wall_type, shared edges use interior_wall_type.
        @level Base level. @top_level Optional top level (else height_mm). @rooms Room rectangles (rooms must not overlap; share edges to be adjacent).
        @exterior_wall_type @interior_wall_type Wall type names. @height_mm Wall height when no top_level (default 3000).
        @create_floor Also create a floor under each room. @floor_type Required if create_floor.
        Doors are not placed here (use place_family_instance afterwards).
        """, Category = "Architecture")]
    public static object CreatePlanFromRooms(string level, RoomRect[] rooms, string exteriorWallType, string interiorWallType = null, string topLevel = null,
        double heightMm = 3000, bool createFloor = false, string floorType = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var tl = Find.Level(doc, topLevel, true);
        var ext = Find.Type<WallType>(doc, exteriorWallType);
        var inn = interiorWallType != null ? Find.Type<WallType>(doc, interiorWallType) : ext;

        // collect axis-aligned edges: key = (isHorizontal, coordinate) -> list of (a, b, room index)
        const double Q = 1.0; // mm quantisation
        var edges = new Dictionary<(bool h, long c), List<(double a, double b, int room)>>();
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            double x0 = r.Origin[0], y0 = r.Origin[1], x1 = x0 + r.WidthMm, y1 = y0 + r.DepthMm;
            int ri = i;
            void Add(bool h, double c, double a, double b)
            {
                var key = (h, (long)Math.Round(c / Q));
                if (!edges.TryGetValue(key, out var l)) edges[key] = l = new();
                l.Add((Math.Min(a, b), Math.Max(a, b), ri));
            }
            Add(true, y0, x0, x1); Add(true, y1, x0, x1); Add(false, x0, y0, y1); Add(false, x1, y0, y1);
        }
        // split each line at all breakpoints and count how many rooms use each atomic piece
        var segs = new List<(bool h, double c, double a, double b, bool shared)>();
        foreach (var kv in edges)
        {
            var pts = kv.Value.SelectMany(e => new[] { e.a, e.b }).Distinct().OrderBy(x => x).ToList();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double a = pts[i], b = pts[i + 1];
                if (b - a < 1) continue;
                int users = kv.Value.Where(e => e.a <= a + 0.5 && e.b >= b - 0.5).Select(e => e.room).Distinct().Count();
                if (users == 0) continue;
                segs.Add((kv.Key.h, kv.Key.c * Q, a, b, users > 1));
            }
        }
        // merge consecutive atomic pieces of the same kind into longer walls
        var merged = new List<(bool h, double c, double a, double b, bool shared)>();
        foreach (var g in segs.GroupBy(s => (s.h, s.c, s.shared)))
        {
            var list = g.OrderBy(s => s.a).ToList();
            double ca = list[0].a, cb = list[0].b;
            for (int i = 1; i < list.Count; i++)
            {
                if (Math.Abs(list[i].a - cb) < 1) cb = list[i].b;
                else { merged.Add((g.Key.h, g.Key.c, ca, cb, g.Key.shared)); ca = list[i].a; cb = list[i].b; }
            }
            merged.Add((g.Key.h, g.Key.c, ca, cb, g.Key.shared));
        }
        var wallIds = new JsonArray();
        double z = U.ToMm(lv.Elevation);
        double wallH = tl != null ? U.ToMm(tl.Elevation - lv.Elevation) : heightMm;
        foreach (var m in merged)
        {
            XYZ p0 = m.h ? U.Pt(new[] { m.a, m.c }, z) : U.Pt(new[] { m.c, m.a }, z);
            XYZ p1 = m.h ? U.Pt(new[] { m.b, m.c }, z) : U.Pt(new[] { m.c, m.b }, z);
            var wt = m.shared ? inn : ext;
            var w = Wall.Create(doc, Line.CreateBound(p0, p1), wt.Id, lv.Id, U.Mm(wallH), 0, false, false);
            if (tl != null) w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(tl.Id);
            wallIds.Add(w.Id.Value);
        }
        doc.Regenerate();
        var roomInfo = new JsonArray();
        var errs = new JsonArray();
        foreach (var r in rooms)
        {
            try
            {
                var c = new UV(U.Mm(r.Origin[0] + r.WidthMm / 2), U.Mm(r.Origin[1] + r.DepthMm / 2));
                var room = doc.Create.NewRoom(lv, c);
                if (!string.IsNullOrWhiteSpace(r.Name)) room.get_Parameter(BuiltInParameter.ROOM_NAME)?.Set(r.Name);
                if (!string.IsNullOrWhiteSpace(r.Number)) room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.Set(r.Number);
                if (!string.IsNullOrWhiteSpace(r.Department)) room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.Set(r.Department);
                doc.Regenerate();
                roomInfo.Add(new JsonObject { ["id"] = room.Id.Value, ["name"] = r.Name, ["area_m2"] = U.R(U.ToM2(room.Area), 2), ["target_m2"] = U.R(r.WidthMm * r.DepthMm / 1e6, 2) });
                if (createFloor && floorType != null)
                {
                    var ft = Find.Type<FloorType>(doc, floorType);
                    var loop = Geo.Loop(new List<XYZ>
                    {
                        U.Pt(new[] { r.Origin[0], r.Origin[1] }), U.Pt(new[] { r.Origin[0] + r.WidthMm, r.Origin[1] }),
                        U.Pt(new[] { r.Origin[0] + r.WidthMm, r.Origin[1] + r.DepthMm }), U.Pt(new[] { r.Origin[0], r.Origin[1] + r.DepthMm }),
                    }, lv.Elevation);
                    Floor.Create(doc, new List<CurveLoop> { loop }, ft.Id, lv.Id);
                }
            }
            catch (Exception ex) { errs.Add($"{r.Name}: {ex.Message}"); }
        }
        return new JsonObject { ["walls"] = wallIds.Count, ["wall_ids"] = wallIds, ["rooms"] = roomInfo, ["errors"] = errs };
    }

    [Tool("create_building_shell", """
        Creates a complete rectangular building shell in ONE call: levels, perimeter walls on every storey, floors, roof. Great starting point for Phase 1 massing/schematic models; refine afterwards.
        @width_mm (X) @depth_mm (Y) @floors Storeys @floor_height_mm Floor-to-floor.
        @wall_type @floor_type @roof_type Types. @roof_kind flat | gable | hip. @roof_slope_deg Pitch for gable/hip (default 25). @overhang_mm Eave overhang (default 500).
        @origin [x,y] mm of the lower-left corner. @ground_floor_slab Create the ground-floor slab too.
        @create_levels Create levels 'Level 1..N' + 'Roof'. Set false to USE EXISTING levels (e.g. a template's own levels): give level_names, or the lowest 'floors' levels are used and the next one becomes the roof level.
        @level_names Existing level names for the storeys, bottom to top. @roof_level_name Existing level for the roof (default: the level after the last storey, or a new 'Roof' level).
        """, Category = "Architecture")]
    public static object CreateBuildingShell(double widthMm, double depthMm, int floors, string wallType, string floorType, string roofType, double floorHeightMm = 3200,
        string roofKind = "gable", double roofSlopeDeg = 25, double overhangMm = 500, double[] origin = null, bool groundFloorSlab = true, bool createLevels = true,
        string[] levelNames = null, string roofLevelName = null)
    {
        var doc = Ctx.RequireProject();
        double ox = origin?[0] ?? 0, oy = origin?[1] ?? 0;
        var levels = new List<Level>();
        Level roofLevel;
        if (levelNames != null && levelNames.Length >= floors)
        {
            levels = levelNames.Take(floors).Select(n => Find.Level(doc, n)).ToList();
            roofLevel = roofLevelName != null ? Find.Level(doc, roofLevelName) : (levelNames.Length > floors ? Find.Level(doc, levelNames[floors]) : null);
        }
        else if (createLevels)
        {
            ArchBasicTools.CreateBuildingLevels(floors, floorHeightMm, null, 0, "Level {n}", true);
            for (int i = 1; i <= floors; i++) levels.Add(Find.Level(doc, "Level " + i));
            roofLevel = Find.Level(doc, "Roof", true);
        }
        else
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            if (all.Count < floors) throw new ToolException($"The project has only {all.Count} levels; pass create_levels=true or fewer floors.");
            levels = all.Take(floors).ToList();
            roofLevel = roofLevelName != null ? Find.Level(doc, roofLevelName) : (all.Count > floors ? all[floors] : null);
        }
        if (roofLevel == null)
        {
            var top = levels[^1];
            roofLevel = Level.Create(doc, top.Elevation + U.Mm(floorHeightMm));
            try { roofLevel.Name = "Roof"; } catch { }
        }
        var pts = new[] { new[] { ox, oy }, new[] { ox + widthMm, oy }, new[] { ox + widthMm, oy + depthMm }, new[] { ox, oy + depthMm } };
        var wt = Find.Type<WallType>(doc, wallType); var ft = Find.Type<FloorType>(doc, floorType);
        var wallIds = new JsonArray(); var floorIds = new JsonArray();
        for (int i = 0; i < levels.Count; i++)
        {
            var top = i + 1 < levels.Count ? levels[i + 1] : roofLevel;
            var curves = Geo.Polyline(pts.Select(p => U.Pt(p, U.ToMm(levels[i].Elevation))).ToList(), true);
            foreach (var c in curves)
            {
                var w = Wall.Create(doc, c, wt.Id, levels[i].Id, U.Mm(floorHeightMm), 0, false, false);
                if (top.Id != levels[i].Id) w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(top.Id);
                wallIds.Add(w.Id.Value);
            }
            if (i > 0 || groundFloorSlab)
            {
                var loop = Geo.Loop(pts.Select(p => U.Pt(p)).ToList(), levels[i].Elevation);
                var f = Floor.Create(doc, new List<CurveLoop> { loop }, ft.Id, levels[i].Id);
                floorIds.Add(f.Id.Value);
            }
        }
        var rt = Find.Type<RoofType>(doc, roofType);
        double ov = overhangMm;
        var rp = new[] { new[] { ox - ov, oy - ov }, new[] { ox + widthMm + ov, oy - ov }, new[] { ox + widthMm + ov, oy + depthMm + ov }, new[] { ox - ov, oy + depthMm + ov } };
        ModelCurveArray mcs = new ModelCurveArray();
        var ca = Geo.CurveArray(rp.Select(p => U.Pt(p)).ToList(), roofLevel.Elevation);
        var roof = doc.Create.NewFootPrintRoof(ca, roofLevel, rt, out mcs);
        double tan = Math.Tan(U.Deg(roofSlopeDeg));
        // edges 0 and 2 run along X; for a gable roof slope the edges parallel to the longer direction
        int[] gableEdges = widthMm >= depthMm ? new[] { 0, 2 } : new[] { 1, 3 };
        int idx = 0;
        foreach (ModelCurve mc in mcs)
        {
            bool slope = roofKind != "flat" && (roofKind == "hip" || gableEdges.Contains(idx));
            roof.set_DefinesSlope(mc, slope);
            if (slope) roof.set_SlopeAngle(mc, tan);
            idx++;
        }
        doc.Regenerate();
        return new JsonObject
        {
            ["walls"] = wallIds.Count, ["floors"] = floorIds.Count, ["roof_id"] = roof.Id.Value,
            ["levels"] = new JsonArray(levels.Select(l => (JsonNode)l.Name).ToArray()),
        };
    }
}
