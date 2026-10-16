using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace Arian.RevitMcp.Tools;

public static class MepTools
{
    // ----------------------------------------------------------------- helpers
    static ConnectorSet Connectors(Element e)
    {
        if (e is MEPCurve mc) return mc.ConnectorManager?.Connectors;
        if (e is FamilyInstance fi) return fi.MEPModel?.ConnectorManager?.Connectors;
        return null;
    }

    static List<Connector> ConnList(Element e)
    {
        var res = new List<Connector>();
        var cs = Connectors(e);
        if (cs == null) return res;
        foreach (Connector c in cs) res.Add(c);
        return res.OrderBy(c => c.Id).ToList();
    }

    static JsonObject ConnInfo(Connector c, int idx)
    {
        var o = new JsonObject
        {
            ["index"] = idx,
            ["id"] = c.Id,
            ["origin"] = Dto.JsonArr(c.Origin),
            ["domain"] = c.Domain.ToString(),
            ["connected"] = c.IsConnected,
            ["shape"] = c.Shape.ToString(),
        };
        try { o["direction"] = Dto.JsonArr(c.CoordinateSystem.BasisZ, false); } catch { }
        try
        {
            if (c.Shape == ConnectorProfileType.Round) o["diameter_mm"] = U.R(U.ToMm(c.Radius * 2), 1);
            else if (c.Shape == ConnectorProfileType.Rectangular) { o["width_mm"] = U.R(U.ToMm(c.Width), 1); o["height_mm"] = U.R(U.ToMm(c.Height), 1); }
        }
        catch { }
        try
        {
            if (c.Domain == Domain.DomainHvac) { o["system_type"] = c.DuctSystemType.ToString(); o["flow_lps"] = U.R(UnitUtils.ConvertFromInternalUnits(c.Flow, UnitTypeId.LitersPerSecond), 2); }
            else if (c.Domain == Domain.DomainPiping) { o["system_type"] = c.PipeSystemType.ToString(); o["flow_lps"] = U.R(UnitUtils.ConvertFromInternalUnits(c.Flow, UnitTypeId.LitersPerSecond), 3); }
            else if (c.Domain == Domain.DomainElectrical) o["system_type"] = c.ElectricalSystemType.ToString();
        }
        catch { }
        if (c.IsConnected)
        {
            var refs = new JsonArray();
            foreach (Connector r in c.AllRefs) if (r.Owner.Id != c.Owner.Id && r.ConnectorType != ConnectorType.Logical) refs.Add(r.Owner.Id.Value);
            o["connected_to"] = refs;
        }
        return o;
    }

    [Tool("get_connectors", "Lists the MEP connectors of an element (duct/pipe/conduit/equipment/fitting/terminal): index, position, direction, shape, size, system type, flow and what each is connected to. Use the index with connect_connectors / route_from_connector.",
        Category = "MEP", ReadOnly = true)]
    public static object GetConnectors(long id)
    {
        var doc = Ctx.RequireProject();
        var e = Find.Element(doc, id);
        var arr = new JsonArray();
        var list = ConnList(e);
        for (int i = 0; i < list.Count; i++) arr.Add(ConnInfo(list[i], i));
        return new JsonObject { ["id"] = id, ["element"] = Dto.Brief(e), ["connectors"] = arr };
    }

    static ElementId SystemTypeId<T>(Document doc, string name) where T : ElementType
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<ElementType>().ToList();
        if (string.IsNullOrWhiteSpace(name)) return all.FirstOrDefault()?.Id ?? throw new ToolException($"No {typeof(T).Name} found.");
        return (all.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ToolException($"System type '{name}' not found. Available: {string.Join(", ", all.Select(t => t.Name))}")).Id;
    }

    // ----------------------------------------------------------------- listing
    [Tool("list_mep_types", """
        Lists MEP types: duct_type, pipe_type, conduit_type, cable_tray_type, flex_duct_type, flex_pipe_type, mechanical_system_type, piping_system_type, wire_type, pipe_segment, duct_insulation, pipe_insulation...
        @kind One of the kinds above.
        """, Category = "MEP", ReadOnly = true)]
    public static object ListMepTypes(string kind)
    {
        var doc = Ctx.RequireProject();
        Type cls = kind.ToLowerInvariant() switch
        {
            "duct_type" => typeof(DuctType),
            "flex_duct_type" => typeof(FlexDuctType),
            "pipe_type" => typeof(PipeType),
            "flex_pipe_type" => typeof(FlexPipeType),
            "conduit_type" => typeof(ConduitType),
            "cable_tray_type" => typeof(CableTrayType),
            "mechanical_system_type" => typeof(MechanicalSystemType),
            "piping_system_type" => typeof(PipingSystemType),
            "wire_type" => typeof(WireType),
            "pipe_segment" => typeof(PipeSegment),
            "duct_insulation" => typeof(DuctInsulationType),
            "pipe_insulation" => typeof(PipeInsulationType),
            _ => throw new ToolException("Unknown kind. Use duct_type, pipe_type, conduit_type, cable_tray_type, flex_duct_type, flex_pipe_type, mechanical_system_type, piping_system_type, wire_type, pipe_segment, duct_insulation, pipe_insulation."),
        };
        var arr = new JsonArray(new FilteredElementCollector(doc).OfClass(cls).Cast<Element>().OrderBy(e => e.Name)
            .Select(e => (JsonNode)new JsonObject { ["id"] = e.Id.Value, ["name"] = e.Name }).ToArray());
        return new JsonObject { ["kind"] = kind, ["types"] = arr };
    }

    [Tool("list_mep_systems", "Lists MEP systems (mechanical, piping, electrical circuits) with type, base equipment and element count.", Category = "MEP", ReadOnly = true)]
    public static object ListMepSystems(string domain = "all")
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(MEPSystem)).Cast<MEPSystem>())
        {
            string d = s is MechanicalSystem ? "duct" : s is PipingSystem ? "pipe" : s is ElectricalSystem ? "electrical" : "other";
            if (domain != "all" && domain != d) continue;
            var o = new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["domain"] = d, ["type"] = doc.GetElement(s.GetTypeId())?.Name };
            try { o["base_equipment"] = s.BaseEquipment?.Name; } catch { }
            try { o["elements"] = s.Elements?.Size; } catch { }
            if (s is ElectricalSystem es) { try { o["panel"] = es.PanelName; o["circuit_number"] = es.CircuitNumber; o["voltage_v"] = U.R(UnitUtils.ConvertFromInternalUnits(es.Voltage, UnitTypeId.Volts), 1); } catch { } }
            arr.Add(o);
        }
        return new JsonObject { ["count"] = arr.Count, ["systems"] = arr };
    }

    // ----------------------------------------------------------------- creation: ducts / pipes / conduits / trays
    static void SetSize(Element e, double? widthMm, double? heightMm, double? diameterMm)
    {
        if (diameterMm.HasValue)
        {
            foreach (var bip in new[] { BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM })
            { var p = e.get_Parameter(bip); if (p != null && !p.IsReadOnly) { p.Set(U.Mm(diameterMm.Value)); break; } }
        }
        if (widthMm.HasValue)
        {
            foreach (var bip in new[] { BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM })
            { var p = e.get_Parameter(bip); if (p != null && !p.IsReadOnly) { p.Set(U.Mm(widthMm.Value)); break; } }
        }
        if (heightMm.HasValue)
        {
            foreach (var bip in new[] { BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM })
            { var p = e.get_Parameter(bip); if (p != null && !p.IsReadOnly) { p.Set(U.Mm(heightMm.Value)); break; } }
        }
    }

    enum Kind { Duct, Pipe, Conduit, Tray }

    static MEPCurve MakeSegment(Document doc, Kind kind, XYZ a, XYZ b, string type, string systemType, Level lv, double? w, double? h, double? d)
    {
        MEPCurve seg;
        switch (kind)
        {
            case Kind.Duct:
                seg = Duct.Create(doc, SystemTypeId<MechanicalSystemType>(doc, systemType), Find.Type<DuctType>(doc, type).Id, lv.Id, a, b); break;
            case Kind.Pipe:
                seg = Pipe.Create(doc, SystemTypeId<PipingSystemType>(doc, systemType), Find.Type<PipeType>(doc, type).Id, lv.Id, a, b); break;
            case Kind.Conduit:
                seg = Conduit.Create(doc, Find.Type<ConduitType>(doc, type).Id, a, b, lv.Id); break;
            default:
                seg = CableTray.Create(doc, Find.Type<CableTrayType>(doc, type).Id, a, b, lv.Id); break;
        }
        SetSize(seg, w, h, d);
        return seg;
    }

    static object Run(Kind kind, double[][] points, string type, string systemType, string level, double? widthMm, double? heightMm, double? diameterMm, double elevationMm, bool fittings)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var pts = points.Select(p => new XYZ(U.Mm(p[0]), U.Mm(p[1]), p.Length > 2 ? U.Mm(p[2]) : lv.Elevation + U.Mm(elevationMm))).ToList();
        pts = Geo.Clean(pts, false);
        if (pts.Count < 2) throw new ToolException("Need at least 2 distinct points.");
        var segs = new List<MEPCurve>();
        for (int i = 0; i + 1 < pts.Count; i++) segs.Add(MakeSegment(doc, kind, pts[i], pts[i + 1], type, systemType, lv, widthMm, heightMm, diameterMm));
        doc.Regenerate();
        var notes = new JsonArray();
        if (fittings)
        {
            for (int i = 0; i + 1 < segs.Count; i++)
            {
                try
                {
                    var (c1, c2) = ClosestPair(segs[i], segs[i + 1], unconnectedOnly: true);
                    if (c1 == null) continue;
                    var d1 = c1.CoordinateSystem.BasisZ; var d2 = c2.CoordinateSystem.BasisZ;
                    if (Math.Abs(d1.DotProduct(d2)) > 0.9999 && d1.DotProduct(d2) < 0) { c1.ConnectTo(c2); continue; } // collinear: join directly
                    doc.Create.NewElbowFitting(c1, c2);
                }
                catch (Exception ex) { notes.Add($"fitting at point {i + 1}: {ex.Message}"); }
            }
        }
        var arr = new JsonArray(segs.Select(s => (JsonNode)new JsonObject { ["id"] = s.Id.Value, ["length_mm"] = U.R(U.ToMm((s.Location as LocationCurve).Curve.Length), 1) }).ToArray());
        var res = new JsonObject { ["segments"] = arr };
        if (notes.Count > 0) res["notes"] = notes;
        return res;
    }

    static (Connector, Connector) ClosestPair(Element a, Element b, bool unconnectedOnly)
    {
        Connector best1 = null, best2 = null; double bd = double.MaxValue;
        foreach (var c1 in ConnList(a))
            foreach (var c2 in ConnList(b))
            {
                if (unconnectedOnly && (c1.IsConnected || c2.IsConnected)) continue;
                if (c1.ConnectorType == ConnectorType.Logical || c2.ConnectorType == ConnectorType.Logical) continue;
                double d = c1.Origin.DistanceTo(c2.Origin);
                if (d < bd) { bd = d; best1 = c1; best2 = c2; }
            }
        return (best1, best2);
    }

    [Tool("create_duct_run", """
        Creates a duct run through a polyline of points with elbow fittings at the bends. Points are [x,y,z] mm (z absolute) or [x,y] with elevation_mm above the level.
        @points Route points. @duct_type Duct type name (list_mep_types duct_type). @system_type Mechanical system type e.g. 'Supply Air', 'Return Air', 'Exhaust Air'.
        @level Level. @width_mm/@height_mm for rectangular, @diameter_mm for round ducts. @elevation_mm Height above the level when points have no z.
        @fittings Add elbows at bends (default true).
        """, Category = "MEP")]
    public static object CreateDuctRun(double[][] points, string ductType, string systemType, string level, double? widthMm = null, double? heightMm = null,
        double? diameterMm = null, double elevationMm = 2700, bool fittings = true)
        => Run(Kind.Duct, points, ductType, systemType, level, widthMm, heightMm, diameterMm, elevationMm, fittings);

    [Tool("create_pipe_run", """
        Creates a pipe run through a polyline of points with elbows. Points [x,y,z] mm (z absolute) or [x,y] + elevation_mm.
        @pipe_type Pipe type (list_mep_types pipe_type). @system_type Piping system type e.g. 'Domestic Cold Water', 'Sanitary', 'Hydronic Supply'.
        @level @diameter_mm Nominal diameter. @fittings Add elbows.
        """, Category = "MEP")]
    public static object CreatePipeRun(double[][] points, string pipeType, string systemType, string level, double diameterMm = 25, double elevationMm = 2700, bool fittings = true)
        => Run(Kind.Pipe, points, pipeType, systemType, level, null, null, diameterMm, elevationMm, fittings);

    [Tool("create_conduit_run", "Creates a conduit run through points with elbows. @conduit_type from list_mep_types conduit_type; @diameter_mm trade size.", Category = "MEP")]
    public static object CreateConduitRun(double[][] points, string conduitType, string level, double diameterMm = 20, double elevationMm = 2700, bool fittings = true)
        => Run(Kind.Conduit, points, conduitType, null, level, null, null, diameterMm, elevationMm, fittings);

    [Tool("create_cable_tray_run", "Creates a cable tray run through points with elbows. @tray_type from list_mep_types cable_tray_type; width/height in mm.", Category = "MEP")]
    public static object CreateCableTrayRun(double[][] points, string trayType, string level, double widthMm = 300, double heightMm = 60, double elevationMm = 2700, bool fittings = true)
        => Run(Kind.Tray, points, trayType, null, level, widthMm, heightMm, null, elevationMm, fittings);

    [Tool("create_flex_duct", """
        Creates a flexible duct through points (min 2) — typically the last connection from a duct to a diffuser. Tangents at both ends follow the first/last segment so the ends can be connected (connect_connectors, fitting 'none') or tapped into a main (tap_into).
        @points [[x,y,z],...] mm absolute. @flex_duct_type @system_type @level @diameter_mm.
        """, Category = "MEP")]
    public static object CreateFlexDuct(double[][] points, string flexDuctType, string systemType, string level, double diameterMm = 200)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var pts = points.Select(p => U.Pt(p)).ToList();
        var fd = FlexDuct.Create(doc, SystemTypeId<MechanicalSystemType>(doc, systemType), Find.Type<FlexDuctType>(doc, flexDuctType).Id, lv.Id, pts);
        SetSize(fd, null, null, diameterMm);
        // end connectors face along the end tangents; without this they all point along +X and cannot be connected / tapped
        if (pts.Count >= 2)
        {
            try { fd.StartTangent = (pts[1] - pts[0]).Normalize(); fd.EndTangent = (pts[^1] - pts[^2]).Normalize(); } catch { }
        }
        return Dto.Brief(fd);
    }

    [Tool("create_flex_pipe", "Creates a flexible pipe through points. @flex_pipe_type @system_type @level @diameter_mm.", Category = "MEP")]
    public static object CreateFlexPipe(double[][] points, string flexPipeType, string systemType, string level, double diameterMm = 20)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var pts = points.Select(p => U.Pt(p)).ToList();
        var fp = FlexPipe.Create(doc, SystemTypeId<PipingSystemType>(doc, systemType), Find.Type<FlexPipeType>(doc, flexPipeType).Id, lv.Id, pts);
        SetSize(fp, null, null, diameterMm);
        if (pts.Count >= 2) { try { fp.StartTangent = (pts[1] - pts[0]).Normalize(); fp.EndTangent = (pts[^1] - pts[^2]).Normalize(); } catch { } }
        return Dto.Brief(fp);
    }

    [Tool("route_from_connector", """
        Starts a duct/pipe directly from an equipment/terminal/fitting connector to an end point; size and system are inherited from the connector. The easiest way to connect an air terminal, fan coil, AHU, sink, etc.
        @element_id Equipment/terminal. @connector_index From get_connectors. @end_point [x,y,z] mm. @kind duct | pipe. @type Duct/pipe type name.
        """, Category = "MEP")]
    public static object RouteFromConnector(long elementId, int connectorIndex, double[] endPoint, string kind = "duct", string type = null)
    {
        var doc = Ctx.RequireProject();
        var e = Find.Element(doc, elementId);
        var cs = ConnList(e);
        if (connectorIndex < 0 || connectorIndex >= cs.Count) throw new ToolException($"connector_index out of range (0..{cs.Count - 1}).");
        var c = cs[connectorIndex];
        var lv = Find.NearestLevel(doc, c.Origin.Z);
        MEPCurve seg;
        if (kind.Equals("pipe", StringComparison.OrdinalIgnoreCase))
            seg = Pipe.Create(doc, (type != null ? Find.Type<PipeType>(doc, type) : new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().First()).Id, lv.Id, c, U.Pt(endPoint));
        else
            seg = Duct.Create(doc, (type != null ? Find.Type<DuctType>(doc, type) : new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().First()).Id, lv.Id, c, U.Pt(endPoint));
        return Dto.Brief(seg);
    }

    [Tool("connect_connectors", """
        Connects two elements. If connector indices are omitted the closest unconnected pair is used. Between straight ducts/pipes it adds the right fitting (elbow, transition or union) automatically; with equipment it connects directly.
        @element_a @element_b Ids. @connector_a @connector_b Optional connector indices (get_connectors).
        @fitting auto | elbow | transition | union | none (direct connect).
        """, Category = "MEP")]
    public static object ConnectConnectors(long elementA, long elementB, int? connectorA = null, int? connectorB = null, string fitting = "auto")
    {
        var doc = Ctx.RequireProject();
        var a = Find.Element(doc, elementA); var b = Find.Element(doc, elementB);
        Connector c1, c2;
        if (connectorA.HasValue && connectorB.HasValue) { c1 = ConnList(a)[connectorA.Value]; c2 = ConnList(b)[connectorB.Value]; }
        else (c1, c2) = ClosestPair(a, b, true);
        if (c1 == null || c2 == null) throw new ToolException("No free connector pair found.");
        string used = "direct";
        bool bothCurves = a is MEPCurve && b is MEPCurve && a is not FlexDuct && b is not FlexDuct;
        if (bothCurves && fitting != "none")
        {
            var d1 = c1.CoordinateSystem.BasisZ; var d2 = c2.CoordinateSystem.BasisZ;
            bool collinear = Math.Abs(d1.DotProduct(d2)) > 0.9999;
            if (fitting == "elbow" || (fitting == "auto" && !collinear)) { doc.Create.NewElbowFitting(c1, c2); used = "elbow"; }
            else if (fitting == "transition" || (fitting == "auto" && collinear && !SameSize(c1, c2))) { doc.Create.NewTransitionFitting(c1, c2); used = "transition"; }
            else if (fitting == "union" || fitting == "auto") { doc.Create.NewUnionFitting(c1, c2); used = "union"; }
        }
        else c1.ConnectTo(c2);
        return new JsonObject { ["connected"] = true, ["fitting"] = used };
    }

    static bool SameSize(Connector a, Connector b)
    {
        try
        {
            if (a.Shape == ConnectorProfileType.Round && b.Shape == ConnectorProfileType.Round) return Math.Abs(a.Radius - b.Radius) < 1e-4;
            if (a.Shape == ConnectorProfileType.Rectangular && b.Shape == ConnectorProfileType.Rectangular) return Math.Abs(a.Width - b.Width) < 1e-4 && Math.Abs(a.Height - b.Height) < 1e-4;
        }
        catch { }
        return false;
    }

    [Tool("tap_into", "Connects a branch duct/pipe INTO the side of a main duct/pipe with a tap/tee (takeoff) fitting. @branch_id: the branch whose open end is near the main; @main_id: the main run.", Category = "MEP")]
    public static object TapInto(long branchId, long mainId)
    {
        var doc = Ctx.RequireProject();
        var branch = Find.Element<MEPCurve>(doc, branchId); var main = Find.Element<MEPCurve>(doc, mainId);
        var mainCurve = ((LocationCurve)main.Location).Curve;
        Connector best = null; double bd = double.MaxValue;
        foreach (var c in ConnList(branch))
        {
            if (c.IsConnected) continue;
            var pr = mainCurve.Project(c.Origin);
            if (pr != null && pr.Distance < bd) { bd = pr.Distance; best = c; }
        }
        if (best == null) throw new ToolException("The branch has no free end connector.");
        var f = doc.Create.NewTakeoffFitting(best, main);
        return Dto.Brief(f);
    }

    [Tool("create_tee_fitting", "Creates a tee between three connectors: ids of three elements (main end A, main end B, branch) near the same point. Prefer tap_into when branching off a continuous run.", Category = "MEP")]
    public static object CreateTeeFitting(long mainA, long mainB, long branch)
    {
        var doc = Ctx.RequireProject();
        var a = Find.Element(doc, mainA); var b = Find.Element(doc, mainB); var c = Find.Element(doc, branch);
        var pa = ConnList(a).Where(x => !x.IsConnected).ToList(); var pb = ConnList(b).Where(x => !x.IsConnected).ToList(); var pc = ConnList(c).Where(x => !x.IsConnected).ToList();
        Connector ba = null, bb = null, bc = null; double bd = double.MaxValue;
        foreach (var x in pa) foreach (var y in pb) foreach (var z in pc)
        {
            double d = x.Origin.DistanceTo(y.Origin) + y.Origin.DistanceTo(z.Origin) + x.Origin.DistanceTo(z.Origin);
            if (d < bd) { bd = d; ba = x; bb = y; bc = z; }
        }
        if (ba == null) throw new ToolException("No free connectors found near each other.");
        return Dto.Brief(doc.Create.NewTeeFitting(ba, bb, bc));
    }

    [Tool("find_open_connectors", """
        QA: lists duct/pipe/conduit/tray elements that have unconnected ends (dangling runs) — typical modelling errors to fix before coordination. @category e.g. Ducts, Pipes, Conduits, Cable Trays, Duct Fittings.
        """, Category = "MEP", ReadOnly = true)]
    public static object FindOpenConnectors(string category = "Ducts", int limit = 100)
    {
        var doc = Ctx.RequireProject();
        var cat = Find.Category(doc, category);
        var arr = new JsonArray();
        int total = 0;
        foreach (var e in new FilteredElementCollector(doc).OfCategoryId(cat.Id).WhereElementIsNotElementType())
        {
            var cs = ConnList(e).Where(c => c.ConnectorType != ConnectorType.Logical && !c.IsConnected).ToList();
            if (cs.Count == 0) continue;
            total++;
            if (arr.Count < limit) arr.Add(new JsonObject { ["element"] = Dto.Brief(e), ["open_ends"] = new JsonArray(cs.Select(c => (JsonNode)Dto.JsonArr(c.Origin)).ToArray()) });
        }
        return new JsonObject { ["elements_with_open_ends"] = total, ["items"] = arr };
    }

    // ----------------------------------------------------------------- systems
    [Tool("create_mep_system", """
        Creates a named mechanical (duct) or piping system of a given system type and adds the open matching connectors of the given equipment/terminals to it (2027 API).
        @domain duct | pipe. @system_type Mechanical/Piping system type name. @name System name. @element_ids Equipment + terminals (their free connectors of the right domain are added).
        """, Category = "MEP")]
    public static object CreateMepSystem(string domain, string systemType, string name, long[] elementIds)
    {
        var doc = Ctx.RequireProject();
        var cset = new ConnectorSet();
        var set = new ConnectorSet();
        var conns = new List<Connector>();
        // only connectors whose own classification (e.g. SupplyAir, SupplyHydronic) equals the system type's classification can join the system
        var sysTypeId = domain == "duct" ? SystemTypeId<MechanicalSystemType>(doc, systemType) : SystemTypeId<PipingSystemType>(doc, systemType);
        string cls = (doc.GetElement(sysTypeId) as MEPSystemType)?.SystemClassification.ToString();
        foreach (var e in Find.Elements(doc, elementIds))
            foreach (var c in ConnList(e))
            {
                if (c.IsConnected) continue;
                if (domain == "duct" && c.Domain == Domain.DomainHvac && (cls == null || c.DuctSystemType.ToString() == cls || c.DuctSystemType == DuctSystemType.UndefinedSystemType)) conns.Add(c);
                else if (domain == "pipe" && c.Domain == Domain.DomainPiping && (cls == null || c.PipeSystemType.ToString() == cls || c.PipeSystemType == PipeSystemType.UndefinedSystemType || c.PipeSystemType == PipeSystemType.Global)) conns.Add(c);
            }
        if (conns.Count == 0) throw new ToolException($"None of the elements has a free {domain} connector matching the system classification '{cls}' (see get_connectors system_type).");
        MEPSystem sys;
        var cs = new ConnectorSet();
        // ConnectorSet has no public ctor for items: create via ConnectorSet.Insert
        foreach (var c in conns) cs.Insert(c);
        if (domain == "duct") { var ms = MechanicalSystem.Create(doc, sysTypeId, name); ms.Add(cs); sys = ms; }
        else { var ps = PipingSystem.Create(doc, sysTypeId, name); ps.Add(cs); sys = ps; }
        return new JsonObject { ["system_id"] = sys.Id.Value, ["name"] = sys.Name, ["connectors_added"] = conns.Count };
    }

    [Tool("create_electrical_circuit", """
        Creates an electrical circuit from devices (receptacles, lighting fixtures, equipment) and optionally assigns it to a panel.
        @element_ids Electrical elements to put on the circuit. @system_type power | data | telephone | security | fire_alarm | nurse_call | controls | communication.
        @panel_id Panelboard family instance id to connect to (optional).
        @load_name Optional circuit load name.
        """, Category = "MEP")]
    public static object CreateElectricalCircuit(long[] elementIds, string systemType = "power", long? panelId = null, string loadName = null)
    {
        var doc = Ctx.RequireProject();
        var st = systemType.ToLowerInvariant() switch
        {
            "data" => ElectricalSystemType.Data, "telephone" => ElectricalSystemType.Telephone, "security" => ElectricalSystemType.Security,
            "fire_alarm" => ElectricalSystemType.FireAlarm, "nurse_call" => ElectricalSystemType.NurseCall, "controls" => ElectricalSystemType.Controls,
            "communication" => ElectricalSystemType.Communication, _ => ElectricalSystemType.PowerCircuit,
        };
        var es = ElectricalSystem.Create(doc, elementIds.Select(i => new ElementId(i)).ToList(), st);
        string distUsed = null;
        if (panelId.HasValue)
        {
            var panel = Find.Element<FamilyInstance>(doc, panelId.Value);
            try { es.SelectPanel(panel); }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // A panel placed from a family has no Distribution System until one is chosen: try the project's systems until one matches the circuit.
                var dp = panel.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_DISTRIBUTION_SYSTEM);
                bool ok = false;
                if (dp != null && !dp.IsReadOnly)
                    foreach (var d in new FilteredElementCollector(doc).OfClass(typeof(DistributionSysType)).Cast<DistributionSysType>())
                    {
                        dp.Set(d.Id);
                        try { es.SelectPanel(panel); ok = true; distUsed = d.Name; break; } catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
                    }
                if (!ok) throw new ToolException("The panel's distribution system does not match this circuit (voltage/phases); set the panel's 'Distribution System' parameter to a matching system.");
            }
        }
        if (!string.IsNullOrWhiteSpace(loadName)) { try { es.LoadName = loadName; } catch { } }
        doc.Regenerate();
        var o = new JsonObject { ["circuit_id"] = es.Id.Value, ["circuit_number"] = es.CircuitNumber, ["panel"] = es.PanelName, ["load_name"] = es.LoadName };
        if (distUsed != null) o["panel_distribution_system_set"] = distUsed;
        try { o["voltage_v"] = U.R(UnitUtils.ConvertFromInternalUnits(es.Voltage, UnitTypeId.Volts), 1); o["apparent_load_va"] = U.R(UnitUtils.ConvertFromInternalUnits(es.ApparentLoad, UnitTypeId.VoltAmperes), 1); } catch { }
        return o;
    }

    [Tool("list_electrical_panels", "Lists electrical panelboards/switchboards with their circuits.", Category = "MEP", ReadOnly = true)]
    public static object ListElectricalPanels()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (FamilyInstance p in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_ElectricalEquipment).WhereElementIsNotElementType().OfType<FamilyInstance>())
        {
            var o = Dto.Brief(p);
            try
            {
                var circuits = p.MEPModel?.GetAssignedElectricalSystems();
                o["circuits"] = circuits == null ? 0 : circuits.Count;
                o["panel_name"] = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString() ?? p.Name; o["circuit_numbers"] = new JsonArray((circuits ?? new HashSet<ElectricalSystem>()).Select(c => (JsonNode)c.CircuitNumber).ToArray());
            }
            catch { }
            arr.Add(o);
        }
        return new JsonObject { ["panels"] = arr };
    }

    // ----------------------------------------------------------------- spaces
    [Tool("create_spaces_automatically", "Places MEP Spaces in every enclosed room-like area of a level (for HVAC / lighting analysis).", Category = "MEP")]
    public static object CreateSpacesAutomatically(string level)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var phase = doc.Phases.Cast<Phase>().Last();
        // NewSpaces2 needs a plan view of the level (null view throws)
        var plan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.GenLevel?.Id == lv.Id && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.EngineeringPlan))
            .OrderBy(v => v.ViewType == ViewType.FloorPlan ? 0 : 1).FirstOrDefault()
            ?? throw new ToolException($"Level '{lv.Name}' has no floor plan view; create one with create_floor_plan.");
        var ids = doc.Create.NewSpaces2(lv, phase, plan);
        return new JsonObject { ["created"] = ids.Count, ["ids"] = Dto.IdList(ids) };
    }

    [Tool("create_space", "Creates one MEP Space at a point. Pass 'parameters' for design values (e.g. {'Design Supply Airflow': 100}).", Category = "MEP")]
    public static object CreateSpace(double[] point, string level, string name = null, string number = null, Dictionary<string, JsonNode> parameters = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var sp = doc.Create.NewSpace(lv, new UV(U.Mm(point[0]), U.Mm(point[1])));
        if (!string.IsNullOrWhiteSpace(name)) sp.get_Parameter(BuiltInParameter.ROOM_NAME)?.Set(name);
        if (!string.IsNullOrWhiteSpace(number)) sp.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.Set(number);
        if (parameters != null) Par.SetMany(sp, parameters);
        doc.Regenerate();
        return new JsonObject { ["id"] = sp.Id.Value, ["name"] = sp.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(), ["area_m2"] = U.R(U.ToM2(sp.Area), 2) };
    }

    // ----------------------------------------------------------------- fixture layout
    [Tool("place_fixtures_in_room", """
        Lays out a regular grid of fixtures (light fittings, air terminals, sprinklers, smoke detectors, speakers...) inside a room/space — the quick way to populate ceilings.
        @family_type Fixture family type. @room_id Room or space id.
        @spacing_x_mm / @spacing_y_mm Grid spacing. @margin_mm Minimum distance from the room edge (default half spacing).
        @elevation_mm Mounting height above the room level (default 2700) for level-based families.
        @host_id Ceiling element id for ceiling-hosted (face-based) families.
        """, Category = "MEP")]
    public static object PlaceFixturesInRoom(string familyType, long roomId, double spacingXMm = 3000, double spacingYMm = 3000, double? marginMm = null,
        double elevationMm = 2700, long? hostId = null)
    {
        var doc = Ctx.RequireProject();
        var room = Find.Element<SpatialElement>(doc, roomId);
        var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
        if (loops == null || loops.Count == 0) throw new ToolException("Room has no boundary (not enclosed/placed).");
        var poly = loops[0].Select(s => s.GetCurve().GetEndPoint(0)).ToList();
        var bb = room.get_BoundingBox(null);
        double sx = U.Mm(spacingXMm), sy = U.Mm(spacingYMm), margin = U.Mm(marginMm ?? Math.Min(spacingXMm, spacingYMm) / 2);
        // centre the grid in the room bbox
        int nx = Math.Max(1, (int)Math.Floor((bb.Max.X - bb.Min.X - 2 * margin) / sx) + 1);
        int ny = Math.Max(1, (int)Math.Floor((bb.Max.Y - bb.Min.Y - 2 * margin) / sy) + 1);
        double x0 = (bb.Min.X + bb.Max.X) / 2 - (nx - 1) * sx / 2, y0 = (bb.Min.Y + bb.Max.Y) / 2 - (ny - 1) * sy / 2;
        var lv = doc.GetElement(room.LevelId) as Level;
        var ids = new JsonArray(); var errs = new JsonArray();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                Activity.Check();
                var p = new XYZ(x0 + i * sx, y0 + j * sy, 0);
                if (!Inside(poly, p) || DistToEdges(poly, p) < margin * 0.5) continue;
                try
                {
                    var fi = Place.One(doc, new PlaceSpec { Type = familyType, Point = new[] { U.ToMm(p.X), U.ToMm(p.Y) }, Level = lv?.Name, ElevationMm = elevationMm, HostId = hostId });
                    ids.Add(fi.Id.Value);
                }
                catch (Exception ex) { if (errs.Count < 3) errs.Add(ex.Message); }
            }
        return new JsonObject { ["placed"] = ids.Count, ["ids"] = ids, ["errors"] = errs };
    }

    static bool Inside(List<XYZ> poly, XYZ p)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) && (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)) c = !c;
        return c;
    }

    static double DistToEdges(List<XYZ> poly, XYZ p)
    {
        double best = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            var ab = new XYZ(b.X - a.X, b.Y - a.Y, 0); var ap = new XYZ(p.X - a.X, p.Y - a.Y, 0);
            double t = ab.GetLength() < 1e-9 ? 0 : Math.Clamp(ap.DotProduct(ab) / ab.DotProduct(ab), 0, 1);
            var q = new XYZ(a.X + ab.X * t, a.Y + ab.Y * t, 0);
            best = Math.Min(best, new XYZ(p.X, p.Y, 0).DistanceTo(q));
        }
        return best;
    }

    [Tool("get_mep_system_info", "Detailed info of a duct/pipe/electrical system: type, equipment, element count, flow and key parameters.", Category = "MEP", ReadOnly = true)]
    public static object GetMepSystemInfo(long systemId)
    {
        var doc = Ctx.RequireProject();
        var s = Find.Element<MEPSystem>(doc, systemId);
        var o = Dto.Detail(s);
        try
        {
            var els = new JsonArray();
            foreach (Element e in s.Elements) if (els.Count < 200) els.Add(Dto.Brief(e));
            o["elements"] = els;
        }
        catch { }
        return o;
    }
}
