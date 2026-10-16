using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-3 structure depth: full reinforcement of beams/slabs/walls, covers, load combinations, pre-design and coordination checks.</summary>
public static class StructureDepthTools
{
    // ------------------------------------------------------------------ geometry helpers
    static List<XYZ> Vertices(Element e)
    {
        var res = new List<XYZ>();
        var ge = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        void Walk(GeometryElement g)
        {
            foreach (var go in g)
            {
                if (go is GeometryInstance gi) Walk(gi.GetInstanceGeometry());
                else if (go is Solid s && s.Volume > 1e-9)
                    foreach (Edge ed in s.Edges) { res.Add(ed.AsCurve().GetEndPoint(0)); res.Add(ed.AsCurve().GetEndPoint(1)); }
            }
        }
        if (ge != null) Walk(ge);
        if (res.Count == 0) throw new ToolException("Element has no solid geometry.");
        return res;
    }

    static void RequireHost(Element host)
    {
        bool ok = false;
        try { ok = RebarHostData.GetRebarHostData(host)?.IsValidHost() == true; } catch { }
        if (!ok) throw new ToolException("This element is not a valid rebar host. Rebar needs a STRUCTURAL element (create it with structural=true) made of a concrete material (a type whose structure layer uses a Concrete material - see create_compound_type).");
    }

    static Rebar Bar(Document doc, RebarStyle style, RebarBarType bt, Element host, XYZ normal, IList<Curve> curves)
        => Rebar.CreateFromCurves(doc, style, bt, host, normal, curves, new BarTerminationsData(doc), true, true);

    static void Layout(Rebar rb, int count, double spacing)
    {
        if (rb != null && count > 1) rb.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(count, spacing, true, true, true);
    }

    static void LayoutLen(Rebar rb, double maxSpacing, double length)
    {
        if (rb != null && length > maxSpacing) rb.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(maxSpacing, length, true, true, true);
    }

    static List<Curve> Loop(XYZ a, XYZ b, XYZ c, XYZ d) => new() { Line.CreateBound(a, b), Line.CreateBound(b, c), Line.CreateBound(c, d), Line.CreateBound(d, a) };

    // ------------------------------------------------------------------ beam
    [Tool("create_beam_reinforcement", """
        Reinforces a straight RECTANGULAR concrete beam: bottom and top longitudinal bars plus stirrups with denser spacing near both supports (confinement zones).
        @beam_id Structural framing element. @long_bar_type @stirrup_type Bar types (list_rebar_types). @cover_mm Clear cover (default 30).
        @bottom_bars @top_bars Number of bars per face (min 2). @stirrup_spacing_mm Mid-span max spacing. @dense_spacing_mm Spacing in the end zones (default = half of mid spacing). @dense_length_mm End-zone length (default 2 × beam depth).
        """, Category = "Structure")]
    public static object CreateBeamReinforcement(long beamId, string longBarType, string stirrupType, double coverMm = 30, int bottomBars = 3, int topBars = 2,
        double stirrupSpacingMm = 200, double? denseSpacingMm = null, double? denseLengthMm = null)
    {
        var doc = Ctx.RequireProject();
        var beam = Find.Element(doc, beamId);
        var lc = (beam.Location as LocationCurve)?.Curve as Line ?? throw new ToolException("Only straight beams are supported.");
        var lb = Find.Type<RebarBarType>(doc, longBarType); var sb = Find.Type<RebarBarType>(doc, stirrupType);
        var d = new XYZ(lc.Direction.X, lc.Direction.Y, 0).Normalize(); var s = d.CrossProduct(XYZ.BasisZ).Normalize();
        RequireHost(beam);
        var vs = Vertices(beam);
        double t0 = vs.Min(v => v.DotProduct(d)), t1 = vs.Max(v => v.DotProduct(d));
        double u0 = vs.Min(v => v.DotProduct(s)), u1 = vs.Max(v => v.DotProduct(s));
        double z0 = vs.Min(v => v.Z), z1 = vs.Max(v => v.Z);
        double cov = U.Mm(coverMm), dL = lb.BarModelDiameter, dS = sb.BarModelDiameter;
        double off = cov + dS + dL / 2;
        if (u1 - u0 <= 2 * off || z1 - z0 <= 2 * off) throw new ToolException("Beam section too small for this cover/bar size.");
        XYZ P(double t, double u, double z) => d * t + s * u + XYZ.BasisZ * z;
        var ids = new JsonArray();
        void Row(double z, int n)
        {
            n = Math.Max(2, n);
            for (int i = 0; i < n; i++)
            {
                double u = u0 + off + (u1 - u0 - 2 * off) * i / (n - 1);
                var rb = Bar(doc, RebarStyle.Standard, lb, beam, s, new List<Curve> { Line.CreateBound(P(t0 + cov, u, z), P(t1 - cov, u, z)) });
                if (rb != null) ids.Add(rb.Id.Value);
            }
        }
        Row(z0 + off, bottomBars); Row(z1 - off, topBars);
        // stirrups
        double a = dS / 2 + cov, ua = u0 + a, ub = u1 - a, za = z0 + a, zb = z1 - a;
        double depth = z1 - z0, lenEnd = U.Mm(denseLengthMm ?? U.ToMm(2 * depth));
        double dense = U.Mm(denseSpacingMm ?? stirrupSpacingMm / 2), mid = U.Mm(stirrupSpacingMm);
        double tStart = t0 + cov + dS / 2, tEnd = t1 - cov - dS / 2;
        void Stirrup(double t, double len, double spacing)
        {
            var st = Bar(doc, RebarStyle.StirrupTie, sb, beam, d, Loop(P(t, ua, za), P(t, ub, za), P(t, ub, zb), P(t, ua, zb)));
            if (st == null) return;
            LayoutLen(st, spacing, len); ids.Add(st.Id.Value);
        }
        int sets;
        if (tEnd - tStart > 2 * lenEnd + mid)
        {
            Stirrup(tStart, lenEnd, dense);
            Stirrup(tStart + lenEnd + dense, tEnd - tStart - 2 * lenEnd - dense, mid);
            Stirrup(tEnd - lenEnd, lenEnd, dense);
            sets = 3;
        }
        else { Stirrup(tStart, tEnd - tStart, mid); sets = 1; }
        return new JsonObject { ["rebar_ids"] = ids, ["bottom_bars"] = Math.Max(2, bottomBars), ["top_bars"] = Math.Max(2, topBars), ["stirrup_sets"] = sets };
    }

    // ------------------------------------------------------------------ slab / footing
    [Tool("create_slab_reinforcement", """
        Reinforces a rectangular (by bounding box) concrete floor, foundation slab or footing with an orthogonal bar mesh: X and Y bars in the bottom and/or top layer.
        @host_id Floor / foundation / footing. @bar_type Bar type. @spacing_mm Bar spacing (default 200). @cover_mm Clear cover (default 25). @layers bottom | top | both (default bottom). @top_bar_type Different type for the top layer (default = bar_type).
        """, Category = "Structure")]
    public static object CreateSlabReinforcement(long hostId, string barType, double spacingMm = 200, double coverMm = 25, string layers = "bottom", string topBarType = null)
    {
        var doc = Ctx.RequireProject();
        var host = Find.Element(doc, hostId);
        var bt = Find.Type<RebarBarType>(doc, barType); var tt = topBarType == null ? bt : Find.Type<RebarBarType>(doc, topBarType);
        RequireHost(host);
        var vs = Vertices(host);
        double x0 = vs.Min(v => v.X), x1 = vs.Max(v => v.X), y0 = vs.Min(v => v.Y), y1 = vs.Max(v => v.Y), z0 = vs.Min(v => v.Z), z1 = vs.Max(v => v.Z);
        double cov = U.Mm(coverMm), sp = U.Mm(spacingMm);
        var ids = new JsonArray(); int bars = 0;
        void Layer(RebarBarType t, bool top)
        {
            double d = t.BarModelDiameter;
            double zx = top ? z1 - cov - d / 2 : z0 + cov + d / 2;
            double zy = top ? zx - d : zx + d;
            int nx = (int)Math.Floor((y1 - y0 - 2 * cov) / sp) + 1, ny = (int)Math.Floor((x1 - x0 - 2 * cov) / sp) + 1;
            var rx = Bar(doc, RebarStyle.Standard, t, host, XYZ.BasisY, new List<Curve> { Line.CreateBound(new XYZ(x0 + cov, y0 + cov, zx), new XYZ(x1 - cov, y0 + cov, zx)) });
            Layout(rx, nx, sp); if (rx != null) { ids.Add(rx.Id.Value); bars += nx; }
            var ry = Bar(doc, RebarStyle.Standard, t, host, XYZ.BasisX, new List<Curve> { Line.CreateBound(new XYZ(x0 + cov, y0 + cov, zy), new XYZ(x0 + cov, y1 - cov, zy)) });
            Layout(ry, ny, sp); if (ry != null) { ids.Add(ry.Id.Value); bars += ny; }
        }
        string l = (layers ?? "bottom").ToLowerInvariant();
        if (l is "bottom" or "both") Layer(bt, false);
        if (l is "top" or "both") Layer(tt, true);
        return new JsonObject { ["rebar_ids"] = ids, ["bars"] = bars, ["layers"] = l };
    }

    // ------------------------------------------------------------------ wall
    [Tool("create_wall_reinforcement", """
        Reinforces a straight concrete wall (shear wall): horizontal and vertical bars in both faces (two curtains).
        @wall_id Wall. @bar_type Bar type. @vertical_spacing_mm Spacing of the vertical bars. @horizontal_spacing_mm Spacing of the horizontal bars. @cover_mm Clear cover. @both_faces false = a single (centre) curtain.
        """, Category = "Structure")]
    public static object CreateWallReinforcement(long wallId, string barType, double verticalSpacingMm = 200, double horizontalSpacingMm = 200, double coverMm = 25, bool bothFaces = true)
    {
        var doc = Ctx.RequireProject();
        var wall = Find.Element<Wall>(doc, wallId);
        var lc = (wall.Location as LocationCurve)?.Curve as Line ?? throw new ToolException("Only straight walls are supported.");
        var bt = Find.Type<RebarBarType>(doc, barType);
        var d = new XYZ(lc.Direction.X, lc.Direction.Y, 0).Normalize(); var n = XYZ.BasisZ.CrossProduct(d).Normalize();
        RequireHost(wall);
        var vs = Vertices(wall);
        double t0 = vs.Min(v => v.DotProduct(d)), t1 = vs.Max(v => v.DotProduct(d)), z0 = vs.Min(v => v.Z), z1 = vs.Max(v => v.Z);
        double mid = lc.GetEndPoint(0).DotProduct(n), w = wall.Width, cov = U.Mm(coverMm), db = bt.BarModelDiameter;
        XYZ P(double t, double u, double z) => d * t + n * u + XYZ.BasisZ * z;
        double vsp = U.Mm(verticalSpacingMm), hsp = U.Mm(horizontalSpacingMm);
        var ids = new JsonArray();
        var offsets = bothFaces ? new[] { -1, 1 } : new[] { 0 };
        foreach (int side in offsets)
        {
            double uh = mid + side * (w / 2 - cov - db / 2);          // horizontal bars outside
            double uv = uh - side * db;                                // vertical bars just inside them
            int nh = (int)Math.Floor((z1 - z0 - 2 * cov) / hsp) + 1, nv = (int)Math.Floor((t1 - t0 - 2 * cov) / vsp) + 1;
            var rh = Bar(doc, RebarStyle.Standard, bt, wall, XYZ.BasisZ, new List<Curve> { Line.CreateBound(P(t0 + cov, uh, z0 + cov), P(t1 - cov, uh, z0 + cov)) });
            Layout(rh, nh, hsp); if (rh != null) ids.Add(rh.Id.Value);
            var rv = Bar(doc, RebarStyle.Standard, bt, wall, d, new List<Curve> { Line.CreateBound(P(t0 + cov, uv, z0 + cov), P(t0 + cov, uv, z1 - cov)) });
            Layout(rv, nv, vsp); if (rv != null) ids.Add(rv.Id.Value);
        }
        return new JsonObject { ["rebar_ids"] = ids, ["curtains"] = offsets.Length };
    }

    // ------------------------------------------------------------------ cover
    [Tool("set_rebar_cover", "Sets the rebar clear cover of concrete hosts (creates a cover type 'Cover NN mm' if needed). @host_ids Beams/columns/floors/walls/footings. @cover_mm Cover distance. Existing rebar keeps its geometry until re-created.", Category = "Structure")]
    public static object SetRebarCover(long[] hostIds, double coverMm)
    {
        var doc = Ctx.RequireProject();
        string name = "Cover " + Math.Round(coverMm) + " mm";
        var ct = new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).Cast<RebarCoverType>().FirstOrDefault(c => Math.Abs(c.CoverDistance - U.Mm(coverMm)) < 1e-6)
            ?? RebarCoverType.Create(doc, name, U.Mm(coverMm));
        int changed = 0;
        foreach (var id in hostIds)
        {
            var e = Find.Element(doc, id); bool any = false;
            foreach (var bip in new[] { BuiltInParameter.CLEAR_COVER_TOP, BuiltInParameter.CLEAR_COVER_BOTTOM, BuiltInParameter.CLEAR_COVER_OTHER, BuiltInParameter.CLEAR_COVER_EXTERIOR, BuiltInParameter.CLEAR_COVER_INTERIOR })
            {
                var p = e.get_Parameter(bip);
                if (p != null && !p.IsReadOnly) { try { p.Set(ct.Id); any = true; } catch { } }
            }
            if (any) changed++;
        }
        return new JsonObject { ["cover_type"] = ct.Name, ["hosts_updated"] = changed };
    }

    // ------------------------------------------------------------------ analytical / loads
    [Tool("create_load_combination", """
        Creates a load combination from load cases, e.g. 1.2 D + 1.6 L.
        @name Combination name. @components Array of {case, factor} where case is a load case name (list_load_cases). @state ultimate | serviceability (default ultimate).
        """, Category = "Structure")]
    public static object CreateLoadCombination(string name, JsonArray components, string state = "ultimate")
    {
        var doc = Ctx.RequireProject();
        var cases = new FilteredElementCollector(doc).OfClass(typeof(LoadCase)).Cast<LoadCase>().ToList();
        var comps = new List<LoadComponent>();
        foreach (var c in components)
        {
            string cn = c?["case"]?.GetValue<string>() ?? throw new ToolException("Each component needs 'case'.");
            var lc = cases.FirstOrDefault(x => x.Name.Equals(cn, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException($"Load case '{cn}' not found. Available: {string.Join(", ", cases.Select(x => x.Name))}.");
            comps.Add(new LoadComponent(lc.Id, c["factor"]?.GetValue<double>() ?? 1.0));
        }
        var lcmb = LoadCombination.Create(doc, name, LoadCombinationType.Combination, state.StartsWith("serv", StringComparison.OrdinalIgnoreCase) ? LoadCombinationState.Serviceability : LoadCombinationState.Ultimate);
        lcmb.SetComponents(comps);
        return new JsonObject { ["id"] = lcmb.Id.Value, ["name"] = lcmb.Name, ["components"] = comps.Count };
    }

    // ------------------------------------------------------------------ pre-design
    [Tool("predesign_sections", """
        Rough pre-design (preliminary sizing, NOT a structural design) of concrete members from span and load, using code minimum-depth rules (ACI 318 / Iranian Mabhas 9 style): returns suggested beam, slab and column sizes in mm.
        @span_m Typical span (m). @load_kn_m2 Total service load incl. self-weight (default 10). @floors Number of floors above the column (default 1). @trib_area_m2 Column tributary area per floor (default span²). @continuity simple | one_end | both_ends | cantilever. @fc_mpa Concrete strength (default 25).
        """, Category = "Structure", ReadOnly = true)]
    public static object PredesignSections(double spanM, double loadKnM2 = 10, int floors = 1, double? tribAreaM2 = null, string continuity = "both_ends", double fcMpa = 25)
    {
        double div = continuity switch { "simple" => 16, "one_end" => 18.5, "cantilever" => 8, _ => 21 };
        double Round(double v, double step) => Math.Ceiling(v / step) * step;
        double beamH = Round(spanM * 1000 / div, 50), beamB = Math.Max(250, Round(beamH * 0.5, 50));
        double slabH = Math.Max(120, Round(spanM * 1000 / 28 * (continuity == "simple" ? 28.0 / 20 : 1), 10));
        double trib = tribAreaM2 ?? spanM * spanM;
        double nKn = loadKnM2 * trib * floors * 1.4;                    // factored axial load
        double ac = nKn * 1000 / (0.35 * fcMpa);                         // mm² (≈ 0.35 fc' practical limit incl. steel)
        double side = Math.Max(300, Round(Math.Sqrt(ac), 50));
        return new JsonObject
        {
            ["beam_mm"] = new JsonObject { ["width"] = beamB, ["depth"] = beamH, ["rule"] = $"h ≈ L/{div}" },
            ["slab_thickness_mm"] = slabH,
            ["column_mm"] = new JsonObject { ["square_side"] = side, ["factored_axial_kN"] = Math.Round(nKn) },
            ["note"] = "Preliminary only: confirm with analysis and the governing code (Mabhas 9 / ACI 318). Check deflection, punching shear and seismic provisions."
        };
    }

    // ------------------------------------------------------------------ coordination checks
    [Tool("check_beam_clear_height", "Reports beams whose soffit is lower than a minimum clear height above their level (default 2400 mm) — headroom check. @min_mm Minimum clear height.", Category = "Structure", ReadOnly = true)]
    public static object CheckBeamClearHeight(double minMm = 2400)
    {
        var doc = Ctx.RequireProject();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        var bad = new JsonArray(); int n = 0;
        foreach (var b in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType())
        {
            var bb = b.get_BoundingBox(null); if (bb == null) continue; n++;
            var lv = levels.LastOrDefault(l => l.Elevation <= bb.Min.Z + U.Mm(500) && l.Elevation <= bb.Min.Z + 1e-6) ?? levels.FirstOrDefault(); if (lv == null) continue;
            double clear = U.ToMm(bb.Min.Z - lv.Elevation);
            if (clear < minMm - 1) bad.Add(new JsonObject { ["id"] = b.Id.Value, ["level"] = lv.Name, ["clear_mm"] = Math.Round(clear) });
        }
        return new JsonObject { ["beams_checked"] = n, ["too_low"] = bad.Count, ["beams"] = bad };
    }

    [Tool("check_column_alignment", "Finds columns that are not stacked on a column below (within a tolerance) — possible load-path breaks / transfer situations. @tolerance_mm XY tolerance (default 100).", Category = "Structure", ReadOnly = true)]
    public static object CheckColumnAlignment(double toleranceMm = 100)
    {
        var doc = Ctx.RequireProject();
        var cols = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType()
            .Select(c => (c, bb: c.get_BoundingBox(null), p: (c.Location as LocationPoint)?.Point)).Where(x => x.bb != null && x.p != null).ToList();
        if (cols.Count == 0) return new JsonObject { ["columns"] = 0, ["unsupported"] = new JsonArray() };
        double lowest = cols.Min(x => x.bb.Min.Z), tol = U.Mm(toleranceMm);
        var bad = new JsonArray();
        foreach (var x in cols)
        {
            if (x.bb.Min.Z <= lowest + U.Mm(300)) continue;
            bool supported = cols.Any(y => y.c.Id != x.c.Id && Math.Abs(y.bb.Max.Z - x.bb.Min.Z) < U.Mm(300) && new XYZ(y.p.X - x.p.X, y.p.Y - x.p.Y, 0).GetLength() <= tol);
            if (!supported) bad.Add(new JsonObject { ["id"] = x.c.Id.Value, ["x_mm"] = Math.Round(U.ToMm(x.p.X)), ["y_mm"] = Math.Round(U.ToMm(x.p.Y)), ["base_mm"] = Math.Round(U.ToMm(x.bb.Min.Z)) });
        }
        return new JsonObject { ["columns"] = cols.Count, ["unsupported"] = bad.Count, ["items"] = bad };
    }
}
