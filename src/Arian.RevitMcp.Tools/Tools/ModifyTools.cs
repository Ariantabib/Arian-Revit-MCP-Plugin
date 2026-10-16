using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

public static class ModifyTools
{
    [Tool("delete_elements", """
        Deletes elements (and anything that depends on them, e.g. hosted doors with a wall). Returns the ids that were actually deleted. Undoable in Revit.
        @ids Element ids.
        """, Category = "Modify", Destructive = true)]
    public static object DeleteElements(long[] ids)
    {
        var doc = Ctx.RequireDoc();
        var deleted = doc.Delete(Find.Ids(ids));
        return new JsonObject { ["deleted_count"] = deleted.Count, ["deleted_ids"] = Dto.IdList(deleted.Take(500)) };
    }

    [Tool("move_elements", """
        Moves elements by a vector.
        @ids Element ids.
        @vector [dx,dy,dz] in mm.
        """, Category = "Modify")]
    public static object MoveElements(long[] ids, double[] vector)
    {
        var doc = Ctx.RequireDoc();
        ElementTransformUtils.MoveElements(doc, Find.Ids(ids), U.Pt(vector));
        return new JsonObject { ["moved"] = ids.Length };
    }

    [Tool("copy_elements", """
        Copies elements, optionally several times along a vector (linear array: copy i is offset by i*vector).
        @ids Element ids.
        @vector [dx,dy,dz] in mm between consecutive copies.
        @count Number of copies (default 1).
        """, Category = "Modify")]
    public static object CopyElements(long[] ids, double[] vector, int count = 1)
    {
        var doc = Ctx.RequireDoc();
        var all = new List<ElementId>();
        for (int i = 1; i <= Math.Max(1, count); i++)
        {
            var v = U.Pt(vector) * i;
            all.AddRange(ElementTransformUtils.CopyElements(doc, Find.Ids(ids), v));
        }
        return new JsonObject { ["new_count"] = all.Count, ["new_ids"] = Dto.IdList(all.Take(500)) };
    }

    [Tool("rotate_elements", """
        Rotates elements about a vertical (or custom) axis.
        @ids Element ids.
        @center_point [x,y] or [x,y,z] mm on the axis.
        @angle_deg Counter-clockwise angle in degrees.
        @axis_direction Axis direction vector (default [0,0,1]).
        """, Category = "Modify")]
    public static object RotateElements(long[] ids, double[] centerPoint, double angleDeg, double[] axisDirection = null)
    {
        var doc = Ctx.RequireDoc();
        var c = U.Pt(centerPoint);
        var dir = axisDirection == null ? XYZ.BasisZ : U.Vec(axisDirection).Normalize();
        ElementTransformUtils.RotateElements(doc, Find.Ids(ids), Line.CreateBound(c, c + dir), U.Deg(angleDeg));
        return new JsonObject { ["rotated"] = ids.Length };
    }

    [Tool("rotate_array", """
        Radial array: creates copies of elements rotated step by step around an axis (e.g. columns around a circle).
        @ids Element ids.
        @center_point [x,y] mm.
        @step_deg Angle between copies.
        @count Number of copies (not counting the original).
        """, Category = "Modify")]
    public static object RotateArray(long[] ids, double[] centerPoint, double stepDeg, int count)
    {
        var doc = Ctx.RequireDoc();
        var c = U.Pt(centerPoint);
        var axis = Line.CreateBound(c, c + XYZ.BasisZ);
        var all = new List<ElementId>();
        for (int i = 1; i <= count; i++)
        {
            var copies = ElementTransformUtils.CopyElements(doc, Find.Ids(ids), XYZ.Zero);
            ElementTransformUtils.RotateElements(doc, copies, axis, U.Deg(stepDeg * i));
            all.AddRange(copies);
        }
        return new JsonObject { ["new_count"] = all.Count, ["new_ids"] = Dto.IdList(all.Take(500)) };
    }

    [Tool("mirror_elements", """
        Mirrors elements about a vertical plane through a point.
        @ids Element ids.
        @plane_point [x,y] mm on the mirror plane.
        @plane_normal Normal of the mirror plane in XY, e.g. [1,0] mirrors left/right about x=plane_point.x.
        @copy Keep the originals (true) or move them (false).
        """, Category = "Modify")]
    public static object MirrorElements(long[] ids, double[] planePoint, double[] planeNormal, bool copy = true)
    {
        var doc = Ctx.RequireDoc();
        var plane = Plane.CreateByNormalAndOrigin(U.Vec(planeNormal).Normalize(), U.Pt(planePoint));
        var list = Find.Ids(ids);
        if (copy)
        {
            var before = new HashSet<ElementId>(new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds());
            ElementTransformUtils.MirrorElements(doc, list, plane, true);
            var fresh = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds().Where(i => !before.Contains(i)).ToList();
            return new JsonObject { ["new_count"] = fresh.Count, ["new_ids"] = Dto.IdList(fresh.Take(500)) };
        }
        ElementTransformUtils.MirrorElements(doc, list, plane, false);
        return new JsonObject { ["mirrored"] = ids.Length };
    }

    [Tool("change_element_type", """
        Changes the type of elements (e.g. swap a wall type, a door type, a beam size).
        @ids Element ids.
        @type_name New type: 'Family: Type', type name or type id.
        """, Category = "Modify")]
    public static object ChangeElementType(long[] ids, string typeName)
    {
        var doc = Ctx.RequireDoc();
        var first = Find.Element(doc, ids[0]);
        var cat = first.Category != null ? (BuiltInCategory?)(int)first.Category.Id.Value : null;
        var t = Find.Type(doc, typeName, cat);
        if (t is FamilySymbol fs && !fs.IsActive) { fs.Activate(); doc.Regenerate(); }
        int n = 0;
        foreach (var id in ids) { var e = doc.GetElement(new ElementId(id)); if (e != null) { e.ChangeTypeId(t.Id); n++; } }
        return new JsonObject { ["changed"] = n, ["type"] = t.FamilyName + ": " + t.Name };
    }

    [Tool("duplicate_type", """
        Duplicates an element type (wall type, floor type, family type, ...) under a new name. Then edit it with set_parameters / set_type_layers.
        @type_name Existing type ('Family: Type', name, or id).
        @new_name Name of the duplicate.
        @category Optional category to disambiguate.
        """, Category = "Modify")]
    public static object DuplicateType(string typeName, string newName, string category = null)
    {
        var doc = Ctx.RequireDoc();
        BuiltInCategory? cat = string.IsNullOrWhiteSpace(category) ? null : (BuiltInCategory?)(int)Find.Category(doc, category).Id.Value;
        var t = Find.Type(doc, typeName, cat);
        var existing = Find.Type(doc, newName, cat, null, true);
        if (existing != null && existing.FamilyName == t.FamilyName && existing.Name == newName)
            return new JsonObject { ["id"] = existing.Id.Value, ["name"] = existing.Name, ["note"] = "A type with this name already exists; returned it unchanged." };
        var n = t.Duplicate(newName);
        return new JsonObject { ["id"] = n.Id.Value, ["name"] = n.Name, ["family"] = (n as ElementType)?.FamilyName };
    }

    [Tool("rename_element", """
        Renames an element/type/level/view/grid/room name etc.
        @id Element id.
        @name New name.
        """, Category = "Modify")]
    public static object RenameElement(long id, string name)
    {
        var doc = Ctx.RequireDoc();
        var e = Find.Element(doc, id);
        e.Name = name;
        return new JsonObject { ["id"] = id, ["name"] = e.Name };
    }

    [Tool("pin_elements", "Pins or unpins elements.", Category = "Modify")]
    public static object PinElements(long[] ids, bool pinned = true)
    {
        var doc = Ctx.RequireDoc();
        foreach (var e in Find.Elements(doc, ids)) e.Pinned = pinned;
        return new JsonObject { ["count"] = ids.Length, ["pinned"] = pinned };
    }

    [Tool("group_elements", """
        Creates a model group from elements.
        @ids Element ids.
        @name Group type name.
        """, Category = "Modify")]
    public static object GroupElements(long[] ids, string name = null)
    {
        var doc = Ctx.RequireProject();
        var g = doc.Create.NewGroup(Find.Ids(ids));
        if (!string.IsNullOrWhiteSpace(name)) g.GroupType.Name = name;
        return new JsonObject { ["group_id"] = g.Id.Value, ["name"] = g.GroupType.Name };
    }

    [Tool("ungroup", "Ungroups a group instance.", Category = "Modify")]
    public static object Ungroup(long groupId)
    {
        var doc = Ctx.RequireProject();
        var g = Find.Element<Group>(doc, groupId);
        var members = g.UngroupMembers();
        return new JsonObject { ["members"] = Dto.IdList(members) };
    }

    [Tool("join_geometry", "Joins the geometry of two elements (e.g. wall to floor, beam to column). Set unjoin=true to unjoin.", Category = "Modify")]
    public static object JoinGeometry(long idA, long idB, bool unjoin = false)
    {
        var doc = Ctx.RequireProject();
        var a = Find.Element(doc, idA); var b = Find.Element(doc, idB);
        bool joined = JoinGeometryUtils.AreElementsJoined(doc, a, b);
        if (unjoin) { if (joined) JoinGeometryUtils.UnjoinGeometry(doc, a, b); }
        else if (!joined) JoinGeometryUtils.JoinGeometry(doc, a, b);
        return new JsonObject { ["joined"] = !unjoin };
    }

    [Tool("cut_geometry", "Cuts element A with element B (void cut), e.g. a void family cutting a wall. Set uncut=true to remove the cut.", Category = "Modify")]
    public static object CutGeometry(long cuttingId, long cutId, bool uncut = false)
    {
        var doc = Ctx.RequireProject();
        var cutting = Find.Element(doc, cuttingId); var cut = Find.Element(doc, cutId);
        if (uncut) SolidSolidCutUtils.RemoveCutBetweenSolids(doc, cutting, cut);
        else SolidSolidCutUtils.AddCutBetweenSolids(doc, cut, cutting);
        return new JsonObject { ["ok"] = true };
    }

    [Tool("set_element_workset", "Moves elements to a workset (worksharing).", Category = "Modify")]
    public static object SetElementWorkset(long[] ids, string workset)
    {
        var doc = Ctx.RequireProject();
        if (!doc.IsWorkshared) throw new ToolException("Document is not workshared.");
        var ws = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).FirstOrDefault(w => w.Name.Equals(workset, StringComparison.OrdinalIgnoreCase))
                 ?? throw new ToolException($"Workset '{workset}' not found.");
        foreach (var e in Find.Elements(doc, ids)) e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM)?.Set(ws.Id.IntegerValue);
        return new JsonObject { ["moved"] = ids.Length };
    }
}
