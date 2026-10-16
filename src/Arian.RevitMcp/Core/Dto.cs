using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Core;

/// <summary>Serialisation of Revit elements for the agent.</summary>
public static class Dto
{
    public static string SafeName(Element e)
    {
        try { return e.Name; } catch { return null; }
    }

    public static JsonObject Brief(Element e)
    {
        var o = new JsonObject
        {
            ["id"] = e.Id.Value,
            ["category"] = e.Category?.Name,
            ["name"] = SafeName(e),
        };
        try
        {
            if (e is FamilyInstance fi)
            {
                o["family"] = fi.Symbol?.FamilyName;
                o["type"] = fi.Symbol?.Name;
            }
            else if (e is ElementType et)
            {
                o["family"] = et.FamilyName;
                o["type"] = et.Name;
            }
            else
            {
                var t = e.Document.GetElement(e.GetTypeId()) as ElementType;
                if (t != null) { o["family"] = t.FamilyName; o["type"] = t.Name; }
            }
        }
        catch { }
        try
        {
            var lv = LevelOf(e);
            if (lv != null) o["level"] = lv.Name;
        }
        catch { }
        o["class"] = e.GetType().Name;
        return o;
    }

    public static Level LevelOf(Element e)
    {
        var doc = e.Document;
        try
        {
            if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId) return doc.GetElement(e.LevelId) as Level;
        }
        catch { }
        foreach (var bip in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.LEVEL_PARAM })
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() != ElementId.InvalidElementId)
                    return doc.GetElement(p.AsElementId()) as Level;
            }
            catch { }
        }
        return null;
    }

    public static JsonObject Detail(Element e, bool parameters = true)
    {
        var doc = e.Document;
        var o = Brief(e);
        try { o["unique_id"] = e.UniqueId; } catch { }

        // location
        try
        {
            switch (e.Location)
            {
                case LocationPoint lp:
                    o["location"] = new JsonObject { ["point"] = JsonArr(lp.Point), ["rotation_deg"] = U.R(U.ToDeg(lp.Rotation), 3) };
                    break;
                case LocationCurve lc:
                    var c = lc.Curve;
                    var loc = new JsonObject { ["curve_type"] = c.GetType().Name, ["length_mm"] = U.R(U.ToMm(c.Length), 2) };
                    if (c.IsBound) { loc["start"] = JsonArr(c.GetEndPoint(0)); loc["end"] = JsonArr(c.GetEndPoint(1)); }
                    if (c is Arc arc) { loc["center"] = JsonArr(arc.Center); loc["radius_mm"] = U.R(U.ToMm(arc.Radius), 2); }
                    o["location"] = loc;
                    break;
            }
        }
        catch { }

        // bounding box
        try
        {
            var bb = e.get_BoundingBox(null);
            if (bb != null) o["bbox_mm"] = new JsonObject { ["min"] = JsonArr(bb.Min), ["max"] = JsonArr(bb.Max) };
        }
        catch { }

        // family-instance specifics
        if (e is FamilyInstance fi)
        {
            try { if (fi.Host != null) o["host"] = new JsonObject { ["id"] = fi.Host.Id.Value, ["name"] = fi.Host.Name, ["category"] = fi.Host.Category?.Name }; } catch { }
            try { o["facing"] = JsonArr(fi.FacingOrientation, false); o["hand"] = JsonArr(fi.HandOrientation, false); } catch { }
            try { o["mirrored"] = fi.Mirrored; } catch { }
            try { o["structural_type"] = fi.StructuralType.ToString(); } catch { }
            try { if (fi.Room != null) o["room"] = fi.Room.Name; } catch { }
            try
            {
                var cm = fi.MEPModel?.ConnectorManager;
                if (cm != null) o["connector_count"] = cm.Connectors.Size;
            }
            catch { }
        }

        // quick numeric facts via built-in parameters
        AddNum(o, e, "length_mm", BuiltInParameter.CURVE_ELEM_LENGTH, SpecTypeId.Length);
        AddNum(o, e, "area_m2", BuiltInParameter.HOST_AREA_COMPUTED, SpecTypeId.Area);
        AddNum(o, e, "volume_m3", BuiltInParameter.HOST_VOLUME_COMPUTED, SpecTypeId.Volume);

        // misc
        try { o["workset"] = doc.IsWorkshared ? doc.GetWorksetTable().GetWorkset(e.WorksetId).Name : null; } catch { }
        try
        {
            if (e.CreatedPhaseId != ElementId.InvalidElementId) o["phase_created"] = doc.GetElement(e.CreatedPhaseId)?.Name;
            if (e.DemolishedPhaseId != ElementId.InvalidElementId) o["phase_demolished"] = doc.GetElement(e.DemolishedPhaseId)?.Name;
        }
        catch { }
        try { if (e.Pinned) o["pinned"] = true; } catch { }
        try { if (e.GroupId != ElementId.InvalidElementId) o["group_id"] = e.GroupId.Value; } catch { }
        try { if (e.DesignOption != null) o["design_option"] = e.DesignOption.Name; } catch { }

        if (parameters) o["parameters"] = Par.Values(e);
        return o;
    }

    static void AddNum(JsonObject o, Element e, string key, BuiltInParameter bip, ForgeTypeId spec)
    {
        try
        {
            var p = e.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                o[key] = U.R(U.ToApi(p.AsDouble(), spec), spec == SpecTypeId.Length ? 1 : 3);
        }
        catch { }
    }

    public static JsonArray JsonArr(XYZ p, bool mm = true)
    {
        if (p == null) return null;
        if (mm) return new JsonArray(U.R(U.ToMm(p.X)), U.R(U.ToMm(p.Y)), U.R(U.ToMm(p.Z)));
        return new JsonArray(U.R(p.X, 4), U.R(p.Y, 4), U.R(p.Z, 4));
    }

    public static JsonObject Pair(Element e) => e == null ? null : new JsonObject { ["id"] = e.Id.Value, ["name"] = SafeName(e) };

    public static JsonObject BBox(BoundingBoxXYZ bb) => bb == null ? null : new JsonObject { ["min"] = JsonArr(bb.Min), ["max"] = JsonArr(bb.Max) };

    public static JsonArray IdList(IEnumerable<ElementId> ids) => new JsonArray(ids.Select(i => (JsonNode)i.Value).ToArray());

    public static JsonObject Created(Element e, string note = null)
    {
        var o = Brief(e);
        if (note != null) o["note"] = note;
        return o;
    }
}
