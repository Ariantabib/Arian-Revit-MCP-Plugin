using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Arian.RevitMcp.Tools;

public static class ArchExtraTools
{
    // ---------------------------------------------------------------- curtain walls
    [Tool("create_curtain_wall", """
        Creates a curtain wall (glazed facade) with a regular grid and optional mullions, along a line.
        The grid is defined on a DUPLICATED curtain-wall type so shared types are not changed.
        @start @end [x,y] mm. @base_level @top_level Levels (or height_mm). @curtain_wall_type Wall type of kind curtain (default: first curtain wall type).
        @vertical_spacing_mm Spacing of the VERTICAL mullion lines along the wall (e.g. 1500); omit for no vertical lines.
        @horizontal_spacing_mm Spacing of the HORIZONTAL lines going up (e.g. 1200).
        @mullion_type Mullion type name (list_element_types category='Curtain Wall Mullions') applied to interior and border mullions.
        @panel_type Panel type name (category 'Curtain Panels'), default glazed.
        """, Category = "Architecture")]
    public static object CreateCurtainWall(double[] start, double[] end, string baseLevel, string topLevel = null, double? heightMm = null, string curtainWallType = null,
        double? verticalSpacingMm = null, double? horizontalSpacingMm = null, string mullionType = null, string panelType = null)
    {
        var doc = Ctx.RequireProject();
        var bl = Find.Level(doc, baseLevel); var tl = Find.Level(doc, topLevel, true);
        var baseType = curtainWallType != null ? Find.Type<WallType>(doc, curtainWallType)
            : new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(w => w.Kind == WallKind.Curtain)
              ?? throw new ToolException("No curtain wall type in this project.");
        var wt = baseType;
        if (verticalSpacingMm.HasValue || horizontalSpacingMm.HasValue || mullionType != null || panelType != null)
        {
            string nm = $"Arian CW {baseType.Name} {verticalSpacingMm?.ToString("0") ?? "x"}x{horizontalSpacingMm?.ToString("0") ?? "x"}{(mullionType != null ? " " + mullionType : "")}{(panelType != null ? " " + panelType : "")}";
            foreach (char bad in new[] { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' }) nm = nm.Replace(bad, '-'); // Revit forbids these in type names
            wt = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(w => w.Name == nm) ?? (WallType)baseType.Duplicate(nm);
            void SetLayout(BuiltInParameter layout, BuiltInParameter length, double? spacing)
            {
                if (!spacing.HasValue) { wt.get_Parameter(layout)?.Set(0); return; }
                wt.get_Parameter(layout)?.Set(1); // 1 = Fixed Distance
                wt.get_Parameter(length)?.Set(U.Mm(spacing.Value));
            }
            SetLayout(BuiltInParameter.SPACING_LAYOUT_VERT, BuiltInParameter.SPACING_LENGTH_VERT, verticalSpacingMm);
            SetLayout(BuiltInParameter.SPACING_LAYOUT_HORIZ, BuiltInParameter.SPACING_LENGTH_HORIZ, horizontalSpacingMm);
            if (!string.IsNullOrWhiteSpace(mullionType))
            {
                var mt = Find.Type(doc, mullionType, BuiltInCategory.OST_CurtainWallMullions);
                foreach (var bip in new[] { BuiltInParameter.AUTO_MULLION_INTERIOR_VERT, BuiltInParameter.AUTO_MULLION_INTERIOR_HORIZ,
                             BuiltInParameter.AUTO_MULLION_BORDER1_VERT, BuiltInParameter.AUTO_MULLION_BORDER2_VERT,
                             BuiltInParameter.AUTO_MULLION_BORDER1_HORIZ, BuiltInParameter.AUTO_MULLION_BORDER2_HORIZ })
                    wt.get_Parameter(bip)?.Set(mt.Id);
            }
            if (!string.IsNullOrWhiteSpace(panelType))
                wt.get_Parameter(BuiltInParameter.AUTO_PANEL_WALL)?.Set(Find.Type(doc, panelType, BuiltInCategory.OST_CurtainWallPanels).Id);
        }
        double h = heightMm ?? (tl != null ? U.ToMm(tl.Elevation - bl.Elevation) : 3000);
        var curve = Line.CreateBound(U.Pt(start, U.ToMm(bl.Elevation)), U.Pt(end, U.ToMm(bl.Elevation)));
        var w = Wall.Create(doc, curve, wt.Id, bl.Id, U.Mm(h), 0, false, false);
        if (tl != null && !heightMm.HasValue) w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(tl.Id);
        doc.Regenerate();
        var o = Dto.Brief(w);
        try { var g = w.CurtainGrid; o["panels"] = g.GetPanelIds().Count; o["mullions"] = g.GetMullionIds().Count; } catch { }
        return o;
    }

    // ---------------------------------------------------------------- floors / ceilings from rooms
    static List<CurveLoop> RoomLoops(SpatialElement r)
    {
        var res = new List<CurveLoop>();
        var segs = r.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish });
        if (segs == null) return res;
        foreach (var loop in segs)
        {
            var cl = new CurveLoop();
            foreach (var s in loop) cl.Append(s.GetCurve());
            res.Add(cl);
        }
        return res;
    }

    [Tool("create_floors_from_rooms", """
        Creates a floor slab inside every room (finish boundary) of a level — the fast way to get finished floors/slabs that follow room shapes.
        @level Level. @floor_type Floor type. @room_ids Optional subset of rooms. @offset_mm Offset from level (e.g. -50 to sit finish under the slab top).
        """, Category = "Architecture")]
    public static object CreateFloorsFromRooms(string level, string floorType, long[] roomIds = null, double offsetMm = 0)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level); var ft = Find.Type<FloorType>(doc, floorType);
        var rooms = roomIds != null && roomIds.Length > 0 ? Find.Elements(doc, roomIds).OfType<SpatialElement>()
            : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<SpatialElement>().Where(r => r.LevelId == lv.Id && r.Area > 0);
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var r in rooms)
        {
            try
            {
                var loops = RoomLoops(r);
                if (loops.Count == 0) continue;
                var f = Floor.Create(doc, loops, ft.Id, lv.Id);
                if (offsetMm != 0) f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(U.Mm(offsetMm));
                ids.Add(f.Id.Value);
            }
            catch (Exception ex) { errs.Add($"room {r.Id.Value}: {ex.Message}"); }
        }
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    [Tool("create_ceilings_from_rooms", "Creates a ceiling in every room (finish boundary) of a level. @height_offset_mm Height above the level (default 2700).", Category = "Architecture")]
    public static object CreateCeilingsFromRooms(string level, string ceilingType, long[] roomIds = null, double heightOffsetMm = 2700)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level); var ct = Find.Type<CeilingType>(doc, ceilingType);
        var rooms = roomIds != null && roomIds.Length > 0 ? Find.Elements(doc, roomIds).OfType<SpatialElement>()
            : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<SpatialElement>().Where(r => r.LevelId == lv.Id && r.Area > 0);
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var r in rooms)
        {
            try
            {
                var loops = RoomLoops(r);
                if (loops.Count == 0) continue;
                var c = Ceiling.Create(doc, loops, ct.Id, lv.Id);
                c.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM)?.Set(U.Mm(heightOffsetMm));
                ids.Add(c.Id.Value);
            }
            catch (Exception ex) { errs.Add($"room {r.Id.Value}: {ex.Message}"); }
        }
        return new JsonObject { ["created"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    // ---------------------------------------------------------------- repeat floors
    [Tool("copy_to_levels", """
        Copies elements (e.g. a typical floor: walls, doors, floors, columns...) to other levels, offsetting vertically by the level elevation difference — the quick way to build multi-storey buildings.
        @ids Source elements (on source_level). @source_level Level they belong to. @target_levels Levels to copy to.
        """, Category = "Modify")]
    public static object CopyToLevels(long[] ids, string sourceLevel, string[] targetLevels)
    {
        var doc = Ctx.RequireProject();
        if (ids == null || ids.Length == 0) throw new ToolException("Pass the ids of the elements to copy (use query_elements, e.g. category='Walls', level=...).");
        var src = Find.Level(doc, sourceLevel);
        var all = new JsonArray();
        foreach (var tn in targetLevels)
        {
            var tl = Find.Level(doc, tn);
            var v = new XYZ(0, 0, tl.Elevation - src.Elevation);
            var copies = ElementTransformUtils.CopyElements(doc, Find.Ids(ids), v);
            all.Add(new JsonObject { ["level"] = tl.Name, ["copied"] = copies.Count });
        }
        return new JsonObject { ["results"] = all };
    }

    // ---------------------------------------------------------------- numbering
    [Tool("number_elements", """
        Assigns sequential numbers to a text parameter (default 'Mark') of all elements of a category — e.g. door marks D01, D02..., window marks, column marks. Ordered by position (left-to-right then bottom-to-top) or by type.
        @category e.g. Doors. @prefix e.g. 'D' @start first number @digits zero padding @parameter parameter name (default Mark).
        @order position_x | position_y | level_then_x | name. @level Restrict to a level.
        """, Category = "Modify")]
    public static object NumberElements(string category, string prefix = "", int start = 1, int digits = 2, string parameter = "Mark", string order = "level_then_x", string level = null)
    {
        var doc = Ctx.RequireProject();
        var cat = Find.Category(doc, category);
        Level lv = level != null ? Find.Level(doc, level) : null;
        var els = new FilteredElementCollector(doc).OfCategoryId(cat.Id).WhereElementIsNotElementType().ToList();
        if (lv != null) els = els.Where(e => Dto.LevelOf(e)?.Id == lv.Id).ToList();
        XYZ P(Element e) { var bb = e.get_BoundingBox(null); return bb == null ? XYZ.Zero : (bb.Min + bb.Max) / 2; }
        IEnumerable<Element> sorted = order switch
        {
            "position_y" => els.OrderBy(e => P(e).Y).ThenBy(e => P(e).X),
            "position_x" => els.OrderBy(e => P(e).X).ThenBy(e => P(e).Y),
            "name" => els.OrderBy(e => e.Name),
            _ => els.OrderBy(e => Dto.LevelOf(e)?.Elevation ?? 0).ThenBy(e => Math.Round(P(e).X, 1)).ThenBy(e => P(e).Y),
        };
        int n = start, ok = 0;
        foreach (var e in sorted)
        {
            var p = Par.Get(e, parameter);
            if (p == null || p.IsReadOnly) continue;
            p.Set(prefix + n.ToString().PadLeft(digits, '0'));
            n++; ok++;
        }
        return new JsonObject { ["numbered"] = ok, ["first"] = prefix + start.ToString().PadLeft(digits, '0'), ["last"] = prefix + (n - 1).ToString().PadLeft(digits, '0') };
    }

    // ---------------------------------------------------------------- sheets in bulk
    public sealed class SheetSpec
    {
        [Desc("Sheet number e.g. A-101")] public string Number;
        [Desc("Sheet name")] public string Name;
        [Desc("Views/schedules to place on it (names or ids)")] public string[] Views;
    }

    [Tool("create_sheets", """
        Creates several sheets at once with their views placed. Views are placed in a simple row layout (left to right) at the sheet centre line; adjust later with place_view_on_sheet if needed.
        @sheets [{number, name, views:[...]}]. @titleblock Title block type name (default: first).
        """, Category = "View")]
    public static object CreateSheets(SheetSpec[] sheets, string titleblock = null)
    {
        var doc = Ctx.RequireProject();
        var res = new JsonArray();
        foreach (var s in sheets)
        {
            ViewTools.CreateSheet(s.Number, s.Name, titleblock);
            int i = 0; int n = Math.Max(1, s.Views?.Length ?? 0);
            if (s.Views != null)
                foreach (var v in s.Views)
                {
                    double x = 420.0 * (i + 0.5) / n * 2 * 0.8 + 60;
                    try { ViewTools.PlaceViewOnSheet(s.Number, v, new[] { x, 300.0 }); }
                    catch (Exception ex) { res.Add($"{s.Number}/{v}: {ex.Message}"); }
                    i++;
                }
            res.Add(new JsonObject { ["sheet"] = s.Number, ["views"] = s.Views?.Length ?? 0 });
        }
        return new JsonObject { ["results"] = res };
    }
}
