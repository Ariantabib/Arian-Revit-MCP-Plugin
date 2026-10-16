using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Core;

/// <summary>
/// Unit helpers. The MCP API is ALWAYS: lengths in millimetres, angles in degrees,
/// areas in square metres, volumes in cubic metres. Revit internal units are decimal feet.
/// </summary>
public static class U
{
    public const double MmPerFt = 304.8;
    const double SqFtPerM2 = 10.763910416709722;
    const double CuFtPerM3 = 35.31466672148859;

    public static double Mm(double mm) => mm / MmPerFt;           // mm -> internal feet
    public static double ToMm(double ft) => ft * MmPerFt;          // internal feet -> mm
    public static double Deg(double deg) => deg * Math.PI / 180.0; // deg -> rad
    public static double ToDeg(double rad) => rad * 180.0 / Math.PI;
    public static double M2(double m2) => m2 * SqFtPerM2;          // m2 -> internal sqft
    public static double ToM2(double sqft) => sqft / SqFtPerM2;
    public static double M3(double m3) => m3 * CuFtPerM3;
    public static double ToM3(double cuft) => cuft / CuFtPerM3;

    public static double R(double v, int d = 3) => Math.Round(v, d);

    /// <summary>[x,y] or [x,y,z] in mm -> internal XYZ.</summary>
    public static XYZ Pt(double[] a)
    {
        if (a == null || a.Length < 2) throw new ArgumentException("A point must be [x,y] or [x,y,z] in millimetres.");
        return new XYZ(Mm(a[0]), Mm(a[1]), a.Length > 2 ? Mm(a[2]) : 0);
    }

    public static XYZ Pt(double[] a, double zMm) => new XYZ(Mm(a[0]), Mm(a[1]), Mm(zMm));

    public static double[] Arr(XYZ p) => p == null ? null : new[] { R(ToMm(p.X)), R(ToMm(p.Y)), R(ToMm(p.Z)) };

    public static List<XYZ> Pts(IEnumerable<double[]> pts) => pts.Select(a => Pt(a)).ToList();

    public static XYZ Vec(double[] a) => new XYZ(a[0], a[1], a.Length > 2 ? a[2] : 0);

    /// <summary>Converts an internal value to the API unit for the given spec (mm, m2, m3, deg), else raw.</summary>
    public static double ToApi(double v, ForgeTypeId spec)
    {
        if (spec == null) return v;
        if (spec == SpecTypeId.Length) return ToMm(v);
        if (spec == SpecTypeId.Angle) return ToDeg(v);
        if (spec == SpecTypeId.Area) return ToM2(v);
        if (spec == SpecTypeId.Volume) return ToM3(v);
        return v;
    }

    public static double FromApi(double v, ForgeTypeId spec)
    {
        if (spec == null) return v;
        if (spec == SpecTypeId.Length) return Mm(v);
        if (spec == SpecTypeId.Angle) return Deg(v);
        if (spec == SpecTypeId.Area) return M2(v);
        if (spec == SpecTypeId.Volume) return M3(v);
        return v;
    }
}
