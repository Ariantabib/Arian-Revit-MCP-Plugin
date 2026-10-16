using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-4 MEP depth: sizing calculations, loads, sloped and auto-routed runs, lighting/sprinkler/socket layout, panel loads, QA.</summary>
public static class MepDepthTools
{
    static double R(double v, int d = 2) => Math.Round(v, d);

    // ------------------------------------------------------------------ pure calculations
    static (double dpPaPerM, double v) AirDrop(double dEqM, double q) // q m3/s; round-duct equivalent diameter
    {
        double a = Math.PI * dEqM * dEqM / 4, v = q / a;
        double re = v * dEqM / 1.5e-5, eps = 0.09e-3;
        double f = 0.25 / Math.Pow(Math.Log10(eps / (3.7 * dEqM) + 5.74 / Math.Pow(Math.Max(re, 1000), 0.9)), 2); // Swamee-Jain
        return (f / dEqM * 1.2 * v * v / 2, v);
    }

    [Tool("calc_duct_size", """
        Sizes an air duct from airflow: round diameter, or rectangular width for a given height, with velocity and friction loss (Darcy-Weisbach, galvanised steel).
        @airflow_lps Airflow (l/s). @max_velocity_mps Design velocity limit (default 6; main 8, branch 4-5, terminals 2-3). @height_mm Fixed height for a rectangular size (optional).
        """, Category = "MEP", ReadOnly = true)]
    public static object CalcDuctSize(double airflowLps, double maxVelocityMps = 6, double? heightMm = null)
    {
        if (airflowLps <= 0) throw new ToolException("airflow_l_s must be positive.");
        double q = airflowLps / 1000, area = q / maxVelocityMps;
        double dRaw = Math.Sqrt(4 * area / Math.PI) * 1000;
        double d = Math.Ceiling(dRaw / 25) * 25;
        var (dp, v) = AirDrop(d / 1000, q);
        var res = new JsonObject { ["round"] = new JsonObject { ["diameter_mm"] = d, ["velocity_m_s"] = R(v), ["friction_pa_per_m"] = R(dp, 3) } };
        double h = heightMm ?? Math.Max(200, Math.Ceiling(dRaw * 0.6 / 50) * 50);
        double w = Math.Ceiling(area * 1e6 / h / 50) * 50;
        double de = 1.3 * Math.Pow(w * h, 0.625) / Math.Pow(w + h, 0.25);   // equivalent round diameter (Huebscher)
        double vr = q / (w * h / 1e6);
        res["rectangular"] = new JsonObject { ["width_mm"] = w, ["height_mm"] = h, ["velocity_m_s"] = R(vr), ["equivalent_diameter_mm"] = R(de, 0), ["friction_pa_per_m"] = R(AirDrop(de / 1000, q).dpPaPerM, 3) };
        return res;
    }

    static readonly double[] NominalMm = { 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300 };

    [Tool("calc_pipe_size", """
        Sizes a water pipe from flow (or fixture units): nominal diameter, velocity and pressure drop per metre (Darcy-Weisbach).
        @flow_lps Flow (l/s) — or give @fixture_units (Hunter curve, flush-tank systems; converted to a probable demand). @max_velocity_mps Default 1.5 (supply), use 0.8-1.0 for quiet zones.
        """, Category = "MEP", ReadOnly = true)]
    public static object CalcPipeSize(double? flowLps = null, double? fixtureUnits = null, double maxVelocityMps = 1.5)
    {
        double q = flowLps ?? 0; string basis = "flow";
        if (fixtureUnits.HasValue)
        {
            double[] fu = { 0, 6, 10, 20, 40, 60, 80, 100, 200, 400, 600, 1000 }, gpm = { 0, 5, 8, 11, 16, 19, 23, 26, 38, 58, 75, 100 };
            double x = fixtureUnits.Value; int i = 1; while (i < fu.Length - 1 && x > fu[i]) i++;
            double g = x >= fu[^1] ? gpm[^1] * x / fu[^1] : gpm[i - 1] + (gpm[i] - gpm[i - 1]) * (x - fu[i - 1]) / (fu[i] - fu[i - 1]);
            q = g * 0.0630902; basis = $"{x} fixture units (Hunter)";
        }
        if (q <= 0) throw new ToolException("Give flow_l_s or fixture_units.");
        double qm = q / 1000;
        double dMin = Math.Sqrt(4 * qm / (Math.PI * maxVelocityMps)) * 1000;
        double dn = NominalMm.FirstOrDefault(n => n >= dMin); if (dn == 0) dn = Math.Ceiling(dMin / 50) * 50;
        double dm = dn / 1000, v = qm / (Math.PI * dm * dm / 4), re = v * dm / 1e-6;
        double f = 0.25 / Math.Pow(Math.Log10(0.045e-3 / (3.7 * dm) + 5.74 / Math.Pow(Math.Max(re, 1000), 0.9)), 2);
        double dp = f / dm * 1000 * v * v / 2;
        return new JsonObject { ["basis"] = basis, ["flow_l_s"] = R(q, 3), ["nominal_diameter_mm"] = dn, ["velocity_m_s"] = R(v), ["pressure_drop_pa_per_m"] = R(dp, 1), ["pressure_drop_kpa_per_100m"] = R(dp / 10, 2) };
    }

    static (double cool, double heat, double people) Profile(string p) => (p ?? "office").ToLowerInvariant() switch
    {
        "residential" or "bedroom" or "living" => (70, 60, 25),
        "retail" or "shop" => (120, 70, 5),
        "classroom" or "school" => (100, 65, 3),
        "restaurant" => (150, 75, 1.5),
        "server" or "datacenter" => (500, 30, 50),
        "corridor" => (40, 40, 40),
        _ => (90, 65, 10),
    };

    [Tool("calc_space_loads", """
        Estimates cooling/heating load and supply airflow of Spaces (rules of thumb W/m² by occupancy + people), writes them to the Space parameters when available and returns the diffuser count.
        @space_ids Spaces (default: all spaces). @profile office | residential | retail | classroom | restaurant | server | corridor. @delta_t_kelvin Supply air ΔT (default 10 K). @max_lps_per_diffuser Default 60.
        Preliminary sizing only — use a real load calculation for design.
        """, Category = "MEP")]
    public static object CalcSpaceLoads(long[] spaceIds = null, string profile = "office", double deltaTKelvin = 10, double maxLpsPerDiffuser = 60)
    {
        var doc = Ctx.RequireProject();
        var spaces = (spaceIds != null && spaceIds.Length > 0 ? spaceIds.Select(i => Find.Element(doc, i)) : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MEPSpaces).WhereElementIsNotElementType()).OfType<SpatialElement>().ToList();
        var (cool, heat, ppl) = Profile(profile);
        var items = new JsonArray(); double totalCool = 0;
        foreach (var sp in spaces)
        {
            double area = U.ToM2(sp.Area); if (area <= 0) continue;
            double qc = area * cool + (area / ppl) * 115, qh = area * heat;      // 115 W sensible+latent per person
            double l_s = qc / (1.2 * 1.006 * deltaTKelvin);                           // l/s = W / (rho*cp*dT)  (rho 1.2 kg/m3, cp 1.006 kJ/kgK)
            int diff = (int)Math.Ceiling(l_s / maxLpsPerDiffuser);
            var set = new List<string>();
            bool Put(string n, double v, ForgeTypeId unit) { var p = sp.LookupParameter(n); if (p == null || p.IsReadOnly) return false; try { return p.Set(UnitUtils.ConvertToInternalUnits(v, unit)); } catch { return false; } }
            if (Put("Design Cooling Load", qc, UnitTypeId.Watts)) set.Add("cooling");
            if (Put("Design Heating Load", qh, UnitTypeId.Watts)) set.Add("heating");
            if (Put("Design Supply Airflow", l_s, UnitTypeId.LitersPerSecond)) set.Add("airflow");
            totalCool += qc;
            items.Add(new JsonObject { ["id"] = sp.Id.Value, ["name"] = sp.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(), ["area_m2"] = R(area), ["cooling_w"] = R(qc, 0), ["heating_w"] = R(qh, 0), ["supply_l_s"] = R(l_s, 1), ["diffusers"] = diff, ["written"] = new JsonArray(set.Select(s => (JsonNode)s).ToArray()) });
        }
        return new JsonObject { ["spaces"] = items.Count, ["total_cooling_kw"] = R(totalCool / 1000, 1), ["items"] = items };
    }

    // ------------------------------------------------------------------ resizing
    [Tool("auto_size_ducts", """
        Resizes ducts to a design velocity from their calculated flow (Revit computes the flow once the system has terminals with airflow). Round ducts get a new diameter, rectangular ducts a new width (height kept).
        @duct_ids Ducts (default: all ducts). @max_velocity_mps Default 6. @flow_lps Optional flow override applied to every selected duct.
        """, Category = "MEP")]
    public static object AutoSizeDucts(long[] ductIds = null, double maxVelocityMps = 6, double? flowLps = null)
    {
        var doc = Ctx.RequireProject();
        var ducts = (ductIds != null && ductIds.Length > 0 ? ductIds.Select(i => Find.Element(doc, i)) : new FilteredElementCollector(doc).OfClass(typeof(Duct))).OfType<Duct>().ToList();
        int changed = 0, skipped = 0; var items = new JsonArray();
        foreach (var d in ducts)
        {
            double q = flowLps.HasValue ? flowLps.Value / 1000 : UnitUtils.ConvertFromInternalUnits(d.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)?.AsDouble() ?? 0, UnitTypeId.CubicMetersPerSecond);
            if (q <= 1e-9) { skipped++; continue; }
            double area = q / maxVelocityMps;
            var dia = d.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            try
            {
                if (dia != null && !dia.IsReadOnly && dia.HasValue)
                {
                    double nd = Math.Ceiling(Math.Sqrt(4 * area / Math.PI) * 1000 / 25) * 25; dia.Set(U.Mm(nd));
                    items.Add(new JsonObject { ["id"] = d.Id.Value, ["diameter_mm"] = nd });
                }
                else
                {
                    var w = d.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM); var h = d.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                    double hh = U.ToMm(h.AsDouble()); double nw = Math.Max(100, Math.Ceiling(area * 1e6 / hh / 50) * 50); w.Set(U.Mm(nw));
                    items.Add(new JsonObject { ["id"] = d.Id.Value, ["width_mm"] = nw, ["height_mm"] = R(hh, 0) });
                }
                changed++;
            }
            catch { skipped++; }
        }
        return new JsonObject { ["resized"] = changed, ["skipped"] = skipped, ["items"] = new JsonArray(items.Take(100).Select(x => x.DeepClone()).ToArray()) };
    }

    // ------------------------------------------------------------------ sloped drainage
    [Tool("create_sloped_pipe", """
        Creates a pipe that falls at a given slope — drainage/sanitary lines. The end elevation is computed from the horizontal length.
        @start [x,y,z] mm (z absolute, the HIGH end). @end [x,y] mm. @slope_percent Fall in percent (default 2 = 1:50). @pipe_type @system_type (e.g. 'Sanitary') @level @diameter_mm.
        """, Category = "MEP")]
    public static object CreateSlopedPipe(double[] start, double[] end, string pipeType, string systemType, string level, double slopePercent = 2, double diameterMm = 100)
    {
        if (start.Length < 3) throw new ToolException("start must be [x,y,z].");
        double len = Math.Sqrt(Math.Pow(end[0] - start[0], 2) + Math.Pow(end[1] - start[1], 2));
        double z1 = start[2] - len * slopePercent / 100;
        var r = MepTools.CreatePipeRun(new[] { start, new[] { end[0], end[1], z1 } }, pipeType, systemType, level, diameterMm, 0, false);
        if (r is JsonObject jo) { jo["start_z_mm"] = start[2]; jo["end_z_mm"] = R(z1, 1); jo["slope_percent"] = slopePercent; }
        return r;
    }

    // ------------------------------------------------------------------ auto routing
    static bool Hits(Document doc, XYZ a, XYZ b, double clearFt, ICollection<ElementId> ignore, ICollection<BuiltInCategory> cats, out ElementId hit)
    {
        var min = new XYZ(Math.Min(a.X, b.X) - clearFt, Math.Min(a.Y, b.Y) - clearFt, Math.Min(a.Z, b.Z) - clearFt);
        var max = new XYZ(Math.Max(a.X, b.X) + clearFt, Math.Max(a.Y, b.Y) + clearFt, Math.Max(a.Z, b.Z) + clearFt);
        var f = new BoundingBoxIntersectsFilter(new Outline(min, max));
        var catFilter = new ElementMulticategoryFilter(cats.ToList());
        foreach (var e in new FilteredElementCollector(doc).WherePasses(catFilter).WherePasses(f).WhereElementIsNotElementType())
            if (!ignore.Contains(e.Id)) { hit = e.Id; return true; }
        hit = ElementId.InvalidElementId; return false;
    }

    [Tool("route_mep_auto", """
        Routes a duct / pipe / conduit / cable tray between two points avoiding structure and existing MEP: tries orthogonal routes at several elevations (fewest bends first) and creates the first collision-free one.
        @kind duct | pipe | conduit | tray. @start @end [x,y,z] mm absolute. @type Type name. @system_type For duct/pipe. @level. @width_mm/@height_mm (duct, tray) or @diameter_mm. @clearance_mm Gap to obstacles (default 50).
        @elevations_mm Optional list of candidate cruising elevations (absolute z); default: start/end z and ±300/600/900 around them. @avoid_walls true to also avoid walls (default false: runs may cross walls).
        @dry_run true = only report the chosen route.
        """, Category = "MEP")]
    public static object RouteMepAuto(string kind, double[] start, double[] end, string type, string systemType, string level, double? widthMm = null, double? heightMm = null, double? diameterMm = null,
        double clearanceMm = 50, double[] elevationsMm = null, bool avoidWalls = false, bool dryRun = false)
    {
        var doc = Ctx.RequireProject();
        double sizeMm = Math.Max(widthMm ?? 0, Math.Max(heightMm ?? 0, diameterMm ?? 100));
        double clear = U.Mm(clearanceMm + sizeMm / 2);
        var cats = new List<BuiltInCategory> { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit };
        if (avoidWalls) cats.Add(BuiltInCategory.OST_Walls);
        var ignore = new HashSet<ElementId>();
        XYZ S = U.Pt(start), E = U.Pt(end);
        var zs = new List<double>();
        if (elevationsMm != null) zs.AddRange(elevationsMm); else { foreach (var b in new[] { start[2], end[2] }) foreach (var o in new double[] { 0, 300, -300, 600, -600, 900, -900 }) zs.Add(b + o); }
        zs = zs.Distinct().ToList();
        var cands = new List<(List<XYZ> pts, int bends)>();
        foreach (var zc in zs)
            foreach (bool xFirst in new[] { true, false })
            {
                var c = U.Mm(zc);
                var pts = new List<XYZ> { S, new XYZ(S.X, S.Y, c), xFirst ? new XYZ(E.X, S.Y, c) : new XYZ(S.X, E.Y, c), new XYZ(E.X, E.Y, c), E };
                // remove coincident / collinear points
                var clean = new List<XYZ>();
                foreach (var p in pts) if (clean.Count == 0 || clean[^1].DistanceTo(p) > U.Mm(1)) clean.Add(p);
                for (int i = clean.Count - 2; i >= 1; i--)
                {
                    var d1 = (clean[i] - clean[i - 1]).Normalize(); var d2 = (clean[i + 1] - clean[i]).Normalize();
                    if (d1.DotProduct(d2) > 0.9999) clean.RemoveAt(i);
                }
                cands.Add((clean, clean.Count - 2));
            }
        var ordered = cands.OrderBy(c => c.bends).ThenBy(c => c.pts.Zip(c.pts.Skip(1), (a, b) => a.DistanceTo(b)).Sum()).ToList();
        List<XYZ> chosen = null; var blockers = new HashSet<long>();
        foreach (var (pts, _) in ordered)
        {
            bool ok = true;
            for (int i = 0; i + 1 < pts.Count && ok; i++)
                if (Hits(doc, pts[i], pts[i + 1], clear, ignore, cats, out var hit)) { ok = false; blockers.Add(hit.Value); }
            if (ok) { chosen = pts; break; }
        }
        if (chosen == null) throw new ToolException($"No collision-free orthogonal route found among {ordered.Count} candidates (blocked by element ids: {string.Join(", ", blockers.Take(10))}). Try other elevations_mm, a smaller clearance or avoid_walls=false.");
        var arr = chosen.Select(p => new[] { U.ToMm(p.X), U.ToMm(p.Y), U.ToMm(p.Z) }).ToArray();
        var route = new JsonObject { ["points_mm"] = new JsonArray(arr.Select(a => (JsonNode)new JsonArray(a.Select(v => (JsonNode)R(v, 1)).ToArray())).ToArray()), ["bends"] = chosen.Count - 2 };
        if (dryRun) { route["dry_run"] = true; return route; }
        object res = kind.ToLowerInvariant() switch
        {
            "duct" => MepTools.CreateDuctRun(arr, type, systemType, level, widthMm, heightMm, diameterMm, 0, true),
            "pipe" => MepTools.CreatePipeRun(arr, type, systemType, level, diameterMm ?? 25, 0, true),
            "conduit" => MepTools.CreateConduitRun(arr, type, level, diameterMm ?? 20, 0, true),
            "tray" or "cable_tray" => MepTools.CreateCableTrayRun(arr, type, level, widthMm ?? 300, heightMm ?? 60, 0, true),
            _ => throw new ToolException("kind must be duct | pipe | conduit | tray."),
        };
        route["created"] = res is JsonNode n ? n.DeepClone() : null;
        return route;
    }

    // ------------------------------------------------------------------ layouts
    [Tool("place_lights_by_lux", """
        Places ceiling lights in a room to reach a target illuminance (lumen method): N = E·A / (lumens · utilisation · maintenance), laid out on a regular grid.
        @room_id Room or space. @family_type Light fixture type. @target_lux e.g. office 500, corridor 100, residential 150-300. @lumens_per_fixture Luminous flux of one fixture (default 3000).
        @utilization (default 0.6) @maintenance (default 0.8) @elevation_mm @host_id Ceiling id for face-based fixtures.
        """, Category = "MEP")]
    public static object PlaceLightsByLux(long roomId, string familyType, double targetLux = 500, double lumensPerFixture = 3000, double utilization = 0.6, double maintenance = 0.8, double elevationMm = 2700, long? hostId = null)
    {
        var doc = Ctx.RequireProject();
        var room = Find.Element<SpatialElement>(doc, roomId);
        double area = U.ToM2(room.Area); if (area <= 0) throw new ToolException("Room has no area (not enclosed/placed).");
        int need = (int)Math.Ceiling(targetLux * area / (lumensPerFixture * utilization * maintenance));
        var bb = room.get_BoundingBox(null);
        double W = U.ToMm(bb.Max.X - bb.Min.X), L = U.ToMm(bb.Max.Y - bb.Min.Y);
        int nx = Math.Max(1, (int)Math.Round(Math.Sqrt(need * W / L))), ny = Math.Max(1, (int)Math.Ceiling(need / (double)nx));
        double sx = W / nx, sy = L / ny;
        var r = (JsonObject)MepTools.PlaceFixturesInRoom(familyType, roomId, sx, sy, Math.Min(sx, sy) / 2, elevationMm, hostId);
        int placed = r["placed"]?.GetValue<int>() ?? 0;
        r["required_by_calculation"] = need; r["achieved_lux"] = R(placed * lumensPerFixture * utilization * maintenance / area, 0); r["room_area_m2"] = R(area);
        return r;
    }

    [Tool("place_sprinklers", """
        Places sprinkler heads in a room on a square grid sized from the coverage area per head (light hazard ≈ 20 m², ordinary ≈ 12 m²) and a maximum spacing.
        @room_id @family_type Sprinkler family type. @coverage_m2 Default 12. @max_spacing_mm Default 4600. @elevation_mm Default 2700. @host_id Ceiling (face-based).
        """, Category = "MEP")]
    public static object PlaceSprinklers(long roomId, string familyType, double coverageM2 = 12, double maxSpacingMm = 4600, double elevationMm = 2700, long? hostId = null)
    {
        double s = Math.Min(maxSpacingMm, Math.Sqrt(coverageM2) * 1000);
        return MepTools.PlaceFixturesInRoom(familyType, roomId, s, s, s / 2, elevationMm, hostId);
    }

    [Tool("place_sockets_along_walls", """
        Places wall sockets / switches / data outlets along the walls bounding a room at a regular spacing (hosted on the walls).
        @room_id @family_type Wall-hosted device type. @spacing_mm Distance between devices (default 3000). @elevation_mm Height above the level (default 300 sockets, 1200 switches). @end_offset_mm Distance from wall ends (default 500). @max_count Safety limit.
        """, Category = "MEP")]
    public static object PlaceSocketsAlongWalls(long roomId, string familyType, double spacingMm = 3000, double elevationMm = 300, double endOffsetMm = 500, int maxCount = 200)
    {
        var doc = Ctx.RequireProject();
        var room = Find.Element<SpatialElement>(doc, roomId);
        var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
        if (loops == null || loops.Count == 0) throw new ToolException("Room has no boundary.");
        var lv = doc.GetElement(room.LevelId) as Level;
        var ids = new JsonArray(); var errs = new JsonArray();
        foreach (var seg in loops[0])
        {
            if (ids.Count >= maxCount) break;
            var wall = doc.GetElement(seg.ElementId) as Wall; if (wall == null) continue;
            var c = seg.GetCurve(); double len = U.ToMm(c.Length); double off = endOffsetMm;
            if (len < 2 * off + 100) continue;
            int n = (int)Math.Floor((len - 2 * off) / spacingMm) + 1;
            for (int i = 0; i < n; i++)
            {
                double t = (off + i * spacingMm) / len;
                var p = c.Evaluate(Math.Min(t, 1), true);
                try { ids.Add(Place.One(doc, new PlaceSpec { Type = familyType, Point = new[] { U.ToMm(p.X), U.ToMm(p.Y) }, Level = lv?.Name, ElevationMm = elevationMm, HostId = wall.Id.Value }).Id.Value); }
                catch (Exception ex) { if (errs.Count < 3) errs.Add(ex.Message); }
            }
        }
        return new JsonObject { ["placed"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    // ------------------------------------------------------------------ electrical
    [Tool("get_panel_loads", "Sums the connected load of the circuits fed by an electrical panel (apparent load, VA/kVA, circuit list) for a quick capacity check. @panel_id Panel equipment id.", Category = "MEP", ReadOnly = true)]
    public static object GetPanelLoads(long panelId)
    {
        var doc = Ctx.RequireProject();
        var panel = Find.Element(doc, panelId);
        var circuits = new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>().Where(s => s.BaseEquipment?.Id == panel.Id).ToList();
        var items = new JsonArray(); double total = 0;
        foreach (var c in circuits)
        {
            double va = UnitUtils.ConvertFromInternalUnits(c.ApparentLoad, UnitTypeId.VoltAmperes); total += va;
            items.Add(new JsonObject { ["id"] = c.Id.Value, ["name"] = c.LoadName, ["number"] = c.CircuitNumber, ["load_va"] = R(va, 0), ["elements"] = c.Elements.Size });
        }
        return new JsonObject { ["panel"] = Dto.Brief(panel), ["circuits"] = circuits.Count, ["total_va"] = R(total, 0), ["total_kva"] = R(total / 1000, 2), ["amps_at_400v_3ph"] = R(total / (Math.Sqrt(3) * 400), 1), ["amps_at_230v_1ph"] = R(total / 230, 1), ["items"] = items };
    }

    [Tool("create_panel_schedule", "Creates the panel schedule view of an electrical panel (circuit table) so it can be placed on a sheet. @panel_id Panel equipment id.", Category = "MEP")]
    public static object CreatePanelSchedule(long panelId)
    {
        var doc = Ctx.RequireProject();
        var v = PanelScheduleView.CreateInstanceView(doc, new ElementId(panelId)) ?? throw new ToolException("Revit could not create a panel schedule for this element (is it an electrical panel with circuits?).");
        return new JsonObject { ["view_id"] = v.Id.Value, ["name"] = v.Name };
    }

    // ------------------------------------------------------------------ QA
    static IEnumerable<Connector> Conns(Element e)
    {
        ConnectorManager cm = e is MEPCurve mc ? mc.ConnectorManager : (e as FamilyInstance)?.MEPModel?.ConnectorManager;
        if (cm == null) yield break;
        foreach (Connector c in cm.Connectors) yield return c;
    }

    [Tool("check_mep_systems", """
        QA report of the MEP model: for ducts/pipes/conduits/trays the number of elements with open ends and elements not assigned to any system; per system the element count and open connectors.
        """, Category = "MEP", ReadOnly = true)]
    public static object CheckMepSystems()
    {
        var doc = Ctx.RequireProject();
        var rep = new JsonObject();
        foreach (var (name, cat) in new[] { ("ducts", BuiltInCategory.OST_DuctCurves), ("pipes", BuiltInCategory.OST_PipeCurves), ("conduits", BuiltInCategory.OST_Conduit), ("cable_trays", BuiltInCategory.OST_CableTray) })
        {
            int n = 0, open = 0, nosys = 0;
            foreach (var e in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
            {
                n++;
                var cs = Conns(e).Where(c => c.ConnectorType != ConnectorType.Logical).ToList();
                if (cs.Any(c => !c.IsConnected)) open++;
                if (cat is BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_PipeCurves && cs.All(c => c.MEPSystem == null)) nosys++;
            }
            rep[name] = new JsonObject { ["count"] = n, ["with_open_ends"] = open, ["not_in_system"] = nosys };
        }
        var systems = new JsonArray();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(MEPSystem)).Cast<MEPSystem>().Where(s => s is MechanicalSystem or PipingSystem).Take(200))
        {
            int cnt = 0, openC = 0;
            try { foreach (Element e in s.Elements) { cnt++; openC += Conns(e).Count(c => c.ConnectorType != ConnectorType.Logical && !c.IsConnected); } } catch { }
            systems.Add(new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["elements"] = cnt, ["open_connectors"] = openC, ["has_equipment"] = s.BaseEquipment != null });
        }
        rep["systems"] = systems;
        return rep;
    }

    [Tool("check_mep_clashes", """
        Finds geometric clashes between two categories (e.g. Ducts vs Structural Framing, Pipes vs Ducts). Uses Revit's solid intersection filter.
        @category_a @category_b Category names. @limit Max pairs (default 100). For a category against itself, each pair is reported once.
        """, Category = "MEP", ReadOnly = true)]
    public static object CheckMepClashes(string categoryA, string categoryB, int limit = 100)
    {
        var doc = Ctx.RequireProject();
        var ca = Find.Category(doc, categoryA); var cb = Find.Category(doc, categoryB);
        var pairs = new JsonArray(); var seen = new HashSet<(long, long)>(); int total = 0;
        foreach (var a in new FilteredElementCollector(doc).OfCategoryId(ca.Id).WhereElementIsNotElementType().ToList())
        {
            var hits = new FilteredElementCollector(doc).OfCategoryId(cb.Id).WhereElementIsNotElementType().WherePasses(new ElementIntersectsElementFilter(a));
            foreach (var b in hits)
            {
                if (a.Id == b.Id) continue;
                var key = a.Id.Value < b.Id.Value ? (a.Id.Value, b.Id.Value) : (b.Id.Value, a.Id.Value);
                if (!seen.Add(key)) continue;
                total++;
                if (pairs.Count < limit) pairs.Add(new JsonObject { ["a"] = Dto.Brief(a), ["b"] = Dto.Brief(b) });
            }
        }
        return new JsonObject { ["clashes"] = total, ["pairs"] = pairs };
    }
}
