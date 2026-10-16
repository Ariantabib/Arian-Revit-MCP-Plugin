using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Core;

/// <summary>Geometry construction helpers (all inputs/outputs in internal feet unless stated).</summary>
public static class Geo
{
    /// <summary>Shortest curve Revit accepts is ~0.8 mm (1/32 in); we use a safer 2 mm.</summary>
    public const double MinLen = 2.0 / 304.8;

    public static List<XYZ> Clean(IList<XYZ> pts, bool closed)
    {
        var res = new List<XYZ>();
        foreach (var p in pts)
            if (res.Count == 0 || res[^1].DistanceTo(p) > MinLen) res.Add(p);
        if (closed && res.Count > 1 && res[0].DistanceTo(res[^1]) <= MinLen) res.RemoveAt(res.Count - 1);
        return res;
    }

    /// <summary>Build a closed planar CurveLoop from points, flattening Z to the given elevation (feet) when z is provided.</summary>
    public static CurveLoop Loop(IList<XYZ> pts, double? z = null)
    {
        var p = Clean(pts, true);
        if (p.Count < 3) throw new ToolException("A boundary needs at least 3 distinct points.");
        if (z.HasValue) p = p.Select(q => new XYZ(q.X, q.Y, z.Value)).ToList();
        var loop = new CurveLoop();
        for (int i = 0; i < p.Count; i++)
            loop.Append(Line.CreateBound(p[i], p[(i + 1) % p.Count]));
        return loop;
    }

    public static CurveArray CurveArray(IList<XYZ> pts, double? z = null)
    {
        var p = Clean(pts, true);
        if (p.Count < 3) throw new ToolException("A boundary needs at least 3 distinct points.");
        if (z.HasValue) p = p.Select(q => new XYZ(q.X, q.Y, z.Value)).ToList();
        var ca = new CurveArray();
        for (int i = 0; i < p.Count; i++) ca.Append(Line.CreateBound(p[i], p[(i + 1) % p.Count]));
        return ca;
    }

    public static IList<Curve> Polyline(IList<XYZ> pts, bool closed)
    {
        var p = Clean(pts, closed);
        var list = new List<Curve>();
        for (int i = 0; i < p.Count - 1; i++) list.Add(Line.CreateBound(p[i], p[i + 1]));
        if (closed && p.Count > 2) list.Add(Line.CreateBound(p[^1], p[0]));
        return list;
    }

    public static double SignedArea(IList<XYZ> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; }
        return a / 2.0;
    }

    /// <summary>Find the planar face of a host element closest to a point; returns face with reference (requires ComputeReferences).</summary>
    public static PlanarFace NearestPlanarFace(Element host, XYZ point, XYZ preferNormal = null)
    {
        var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        PlanarFace best = null; double bestD = double.MaxValue;
        void Walk(GeometryElement ge)
        {
            foreach (var go in ge)
            {
                if (go is Solid s && s.Volume > 1e-9)
                {
                    foreach (Face f in s.Faces)
                    {
                        if (f is not PlanarFace pf || pf.Reference == null) continue;
                        double d = Math.Abs(pf.FaceNormal.DotProduct(point - pf.Origin));
                        if (preferNormal != null) d += (1 - pf.FaceNormal.DotProduct(preferNormal)) * 100;
                        if (d < bestD) { bestD = d; best = pf; }
                    }
                }
                else if (go is GeometryInstance gi) Walk(gi.GetSymbolGeometry());
            }
        }
        Walk(host.get_Geometry(opts));
        return best;
    }

    public static Line AxisZ(XYZ p) => Line.CreateBound(p, p + XYZ.BasisZ);
}
