using System.Text.Json.Nodes;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace Arian.RevitMcp.Core;

public sealed class PlaceSpec
{
    [Desc("Family type: 'Family: Type', type name, or type id. Use list_families / list_element_types.")] public string Type;
    [Desc("[x,y,z] mm insertion point (z optional: defaults to the level elevation).")] public double[] Point;
    [Desc("[x,y,z] mm end point for line-based families (beams, braces, line-based lights...).")] public double[] End;
    [Desc("Level name/id (default: nearest level below the point).")] public string Level;
    [Desc("Rotation about the vertical axis, degrees CCW.")] public double RotationDeg;
    [Desc("Host element id for hosted families (wall for doors/windows, floor/ceiling/face host for face-based).")] public long? HostId;
    [Desc("Elevation offset from the level in mm (e.g. sill height, mounting height).")] public double ElevationMm;
    [Desc("Distance along the host wall from its start, mm (alternative to point for doors/windows).")] public double? DistanceFromStartMm;
    [Desc("Top level for two-level families (columns).")] public string TopLevel;
    [Desc("Base offset (columns), mm.")] public double? BaseOffsetMm;
    [Desc("Top offset (columns), mm.")] public double? TopOffsetMm;
    [Desc("Instance parameters to set after placement: {name: value}.")] public Dictionary<string, JsonNode> Parameters;
    [Desc("Flip hand orientation (doors/windows).")] public bool FlipHand;
    [Desc("Flip facing orientation (doors/windows).")] public bool FlipFacing;
    [Desc("Override: column | beam | brace | footing | nonstructural. Normally auto-detected from the category.")] public string StructuralType;
    [Desc("View name for view-based (detail/annotation) families.")] public string View;
}

public static class Place
{
    static StructuralType StructuralFor(FamilySymbol fs, string over)
    {
        if (!string.IsNullOrWhiteSpace(over))
            return over.ToLowerInvariant() switch
            {
                "column" => StructuralType.Column,
                "beam" => StructuralType.Beam,
                "brace" => StructuralType.Brace,
                "footing" => StructuralType.Footing,
                _ => StructuralType.NonStructural,
            };
        var bic = fs.Category != null ? (BuiltInCategory)(int)fs.Category.Id.Value : BuiltInCategory.INVALID;
        return bic switch
        {
            BuiltInCategory.OST_StructuralColumns => StructuralType.Column,
            BuiltInCategory.OST_StructuralFraming => StructuralType.Beam,
            BuiltInCategory.OST_StructuralFoundation => StructuralType.Footing,
            _ => StructuralType.NonStructural,
        };
    }

    public static Wall FindNearestWall(Document doc, XYZ p, double tolFeet, Level lv = null)
    {
        Wall best = null; double bd = double.MaxValue;
        foreach (Wall w in new FilteredElementCollector(doc).OfClass(typeof(Wall)))
        {
            if (w.Location is not LocationCurve lc) continue;
            var bb = w.get_BoundingBox(null);
            if (bb != null && (p.Z < bb.Min.Z - 1 || p.Z > bb.Max.Z + 1)) continue;
            var c = lc.Curve;
            var q = new XYZ(p.X, p.Y, c.GetEndPoint(0).Z);
            var proj = c.Project(q);
            if (proj == null) continue;
            double d = proj.Distance;
            if (d < bd) { bd = d; best = w; }
        }
        if (best != null && bd <= tolFeet + best.Width / 2) return best;
        return null;
    }

    public static FamilyInstance One(Document doc, PlaceSpec s)
    {
        var fs = Find.Symbol(doc, s.Type);
        if (!fs.IsActive) { fs.Activate(); doc.Regenerate(); }
        var fam = fs.Family;
        var ptype = fam.FamilyPlacementType;
        var stype = StructuralFor(fs, s.StructuralType);

        XYZ pt = s.Point != null ? U.Pt(s.Point) : null;
        Level lv = Find.Level(doc, s.Level, true);
        Element host = s.HostId.HasValue ? Find.Element(doc, s.HostId.Value) : null;

        // doors/windows positioned by distance along host wall
        if (host is Wall hw && s.DistanceFromStartMm.HasValue && hw.Location is LocationCurve hlc)
        {
            var c = hlc.Curve;
            double t = Math.Clamp(U.Mm(s.DistanceFromStartMm.Value) / c.Length, 0, 1);
            var q = c.Evaluate(t, true);
            pt = new XYZ(q.X, q.Y, q.Z);
        }
        if (pt == null && ptype != FamilyPlacementType.CurveBased && ptype != FamilyPlacementType.CurveDrivenStructural)
            throw new ToolException("'point' is required for this family (or distance_from_start_mm with host_id).");

        if (lv == null)
        {
            if (host is Wall w0) lv = doc.GetElement(w0.LevelId) as Level;
            else if (pt != null) lv = Find.NearestLevel(doc, pt.Z);
            else if (s.End != null) lv = Find.NearestLevel(doc, U.Pt(s.End).Z);
        }
        bool zGiven = s.Point != null && s.Point.Length > 2;
        if (pt != null && !zGiven && lv != null) pt = new XYZ(pt.X, pt.Y, lv.Elevation);
        if (pt != null && s.ElevationMm != 0 && !zGiven) pt = new XYZ(pt.X, pt.Y, pt.Z + U.Mm(s.ElevationMm));

        FamilyInstance fi;
        switch (ptype)
        {
            case FamilyPlacementType.OneLevelBased:
                fi = doc.Create.NewFamilyInstance(pt, fs, lv, stype);
                break;
            case FamilyPlacementType.TwoLevelsBased:
                fi = doc.Create.NewFamilyInstance(pt, fs, lv, stype);
                if (!string.IsNullOrWhiteSpace(s.TopLevel))
                    fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.Set(Find.Level(doc, s.TopLevel).Id);
                if (s.BaseOffsetMm.HasValue) fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.Set(U.Mm(s.BaseOffsetMm.Value));
                if (s.TopOffsetMm.HasValue) fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.Set(U.Mm(s.TopOffsetMm.Value));
                break;
            case FamilyPlacementType.OneLevelBasedHosted:
                if (host == null && pt != null) host = FindNearestWall(doc, pt, U.Mm(600), lv);
                if (host == null) throw new ToolException("This family needs a host (wall). Pass host_id, or place the point on/near a wall.");
                fi = doc.Create.NewFamilyInstance(pt, fs, host, lv, stype);
                break;
            case FamilyPlacementType.WorkPlaneBased:
                {
                    if (host == null && pt != null)
                    {
                        // try to find any element at the point to host on (walls/floors/ceilings)
                        host = FindNearestWall(doc, pt, U.Mm(400), lv);
                    }
                    if (host == null) throw new ToolException("This is a work-plane/face-based family: pass host_id (wall, floor, ceiling...) and a point on its face.");
                    var face = Geo.NearestPlanarFace(host, pt);
                    if (face == null) throw new ToolException("Could not find a planar face on the host element near the point.");
                    var refDir = Math.Abs(face.FaceNormal.Z) > 0.99 ? XYZ.BasisX : XYZ.BasisZ.CrossProduct(face.FaceNormal).Normalize();
                    // project point onto face plane
                    var proj = pt - face.FaceNormal * face.FaceNormal.DotProduct(pt - face.Origin);
                    fi = doc.Create.NewFamilyInstance(face.Reference, proj, refDir, fs);
                    break;
                }
            case FamilyPlacementType.CurveBased:
            case FamilyPlacementType.CurveDrivenStructural:
                {
                    if (s.End == null) throw new ToolException("This is a line-based family (beam/brace/line-based): pass both 'point' (start) and 'end'.");
                    var a = pt ?? throw new ToolException("'point' (start) is required.");
                    var b = U.Pt(s.End);
                    if (s.End.Length <= 2) b = new XYZ(b.X, b.Y, a.Z);
                    if (a.DistanceTo(b) < Geo.MinLen) throw new ToolException("Start and end points are too close.");
                    fi = doc.Create.NewFamilyInstance(Line.CreateBound(a, b), fs, lv, stype);
                    break;
                }
            case FamilyPlacementType.ViewBased:
            case FamilyPlacementType.CurveBasedDetail:
                {
                    var v = Find.View(doc, s.View ?? doc.ActiveView?.Id.Value.ToString());
                    if (ptype == FamilyPlacementType.ViewBased) fi = doc.Create.NewFamilyInstance(pt, fs, v);
                    else fi = doc.Create.NewFamilyInstance(Line.CreateBound(pt, U.Pt(s.End)), fs, v);
                    break;
                }
            default:
                throw new ToolException($"Placement type {ptype} is not supported by this tool; use execute_csharp for adaptive/pattern-based families.");
        }

        // free-standing level-based families: make the absolute Z of the insertion point authoritative (Revit may add the level elevation again)
        if (pt != null && (ptype == FamilyPlacementType.OneLevelBased) && fi.Location is LocationPoint lp0)
        {
            doc.Regenerate();
            double dz = pt.Z - ((LocationPoint)fi.Location).Point.Z;
            if (Math.Abs(dz) > 1e-4)
            {
                var off = fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                if (off != null && !off.IsReadOnly) off.Set(off.AsDouble() + dz);
                else ElementTransformUtils.MoveElement(doc, fi.Id, new XYZ(0, 0, dz));
            }
        }
        if (Math.Abs(s.RotationDeg) > 1e-9 && fi.Location is LocationPoint && pt != null)
        {
            doc.Regenerate();
            var pivot = ((LocationPoint)fi.Location).Point;
            // rotate about the vertical axis through the insertion point (XY of the requested point is authoritative)
            ElementTransformUtils.RotateElement(doc, fi.Id, Geo.AxisZ(new XYZ(pt.X, pt.Y, pivot.Z)), U.Deg(s.RotationDeg));
        }
        if (s.FlipHand && fi.CanFlipHand) fi.flipHand();
        if (s.FlipFacing && fi.CanFlipFacing) fi.flipFacing();
        if (s.ElevationMm != 0 && zGiven == false && (ptype == FamilyPlacementType.OneLevelBasedHosted))
            fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.Set(U.Mm(s.ElevationMm));
        if (s.Parameters != null) Par.SetMany(fi, s.Parameters);
        return fi;
    }
}
