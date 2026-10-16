using System.Text;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-5: ready-made parametric family library, family QA, type catalogs, CSV type import, nested families.</summary>
public static class FamilyLibraryTools
{
    // ------------------------------------------------------------------ spec helpers
    static FamParamSpec L(string n, double v, string group = null) => new() { Name = n, Type = "length", Value = JsonValue.Create(v), Group = group };
    static FamParamSpec Mat(string n, string defaultMaterial = null) => new() { Name = n, Type = "material", Value = defaultMaterial == null ? null : JsonValue.Create(defaultMaterial) };
    static FamShapeSpec Box(string name, string[] x, string[] y, string[] z, string mat = null, string sub = null, bool solid = true)
        => new() { Kind = "box", Name = name, X = x, Y = y, Z = z, MaterialParameter = mat, Subcategory = sub, Solid = solid };
    static FamShapeSpec Cyl(string name, string[] c, string r, string[] z, string mat = null)
        => new() { Kind = "cylinder", Name = name, Center = c, Radius = r, Z = z, MaterialParameter = mat };
    static string[] A(params string[] s) => s;
    static FamTypeSpec T(string name, params (string k, double v)[] vals)
        => new() { Name = name, Values = vals.ToDictionary(x => x.k, x => (JsonNode)JsonValue.Create(x.v)) };

    sealed record Kind(string Id, string Description, Func<FamilySpec> Build);

    static readonly Kind[] Kinds =
    {
        new("table", "Rectangular table: top + 4 legs (Length, Width, Height, TopThickness, LegSize).", () => new FamilySpec
        {
            Template = "furniture", Category = "Furniture",
            Parameters = { L("Length", 1600), L("Width", 800), L("Height", 750), L("TopThickness", 30), L("LegSize", 60), Mat("Top Material"), Mat("Leg Material") },
            Types = { T("1600x800", ("Length", 1600), ("Width", 800)), T("1200x800", ("Length", 1200), ("Width", 800)), T("1800x900", ("Length", 1800), ("Width", 900)), T("Round-ish 900x900", ("Length", 900), ("Width", 900)) },
            Shapes =
            {
                Box("Top", A("-Length/2", "Length/2"), A("-Width/2", "Width/2"), A("Height-TopThickness", "Height"), "Top Material"),
                Box("Leg1", A("-Length/2+50", "-Length/2+50+LegSize"), A("-Width/2+50", "-Width/2+50+LegSize"), A("0", "Height-TopThickness"), "Leg Material"),
                Box("Leg2", A("Length/2-50-LegSize", "Length/2-50"), A("-Width/2+50", "-Width/2+50+LegSize"), A("0", "Height-TopThickness"), "Leg Material"),
                Box("Leg3", A("-Length/2+50", "-Length/2+50+LegSize"), A("Width/2-50-LegSize", "Width/2-50"), A("0", "Height-TopThickness"), "Leg Material"),
                Box("Leg4", A("Length/2-50-LegSize", "Length/2-50"), A("Width/2-50-LegSize", "Width/2-50"), A("0", "Height-TopThickness"), "Leg Material"),
            }
        }),
        new("chair", "Dining chair: seat, back, 4 legs (SeatHeight, SeatSize, BackHeight).", () => new FamilySpec
        {
            Template = "furniture", Category = "Furniture",
            Parameters = { L("SeatSize", 450), L("SeatHeight", 450), L("BackHeight", 450), L("SeatThickness", 40), L("LegSize", 40), Mat("Chair Material") },
            Types = { T("Standard", ("SeatSize", 450)), T("Large", ("SeatSize", 500), ("SeatHeight", 480)) },
            Shapes =
            {
                Box("Seat", A("-SeatSize/2", "SeatSize/2"), A("-SeatSize/2", "SeatSize/2"), A("SeatHeight-SeatThickness", "SeatHeight"), "Chair Material"),
                Box("Back", A("-SeatSize/2", "SeatSize/2"), A("SeatSize/2-30", "SeatSize/2"), A("SeatHeight", "SeatHeight+BackHeight"), "Chair Material"),
                Box("Leg1", A("-SeatSize/2", "-SeatSize/2+LegSize"), A("-SeatSize/2", "-SeatSize/2+LegSize"), A("0", "SeatHeight-SeatThickness"), "Chair Material"),
                Box("Leg2", A("SeatSize/2-LegSize", "SeatSize/2"), A("-SeatSize/2", "-SeatSize/2+LegSize"), A("0", "SeatHeight-SeatThickness"), "Chair Material"),
                Box("Leg3", A("-SeatSize/2", "-SeatSize/2+LegSize"), A("SeatSize/2-LegSize", "SeatSize/2"), A("0", "SeatHeight-SeatThickness"), "Chair Material"),
                Box("Leg4", A("SeatSize/2-LegSize", "SeatSize/2"), A("SeatSize/2-LegSize", "SeatSize/2"), A("0", "SeatHeight-SeatThickness"), "Chair Material"),
            }
        }),
        new("bed", "Bed: frame, mattress, headboard (Width, Length, HeadboardHeight).", () => new FamilySpec
        {
            Template = "furniture", Category = "Furniture",
            Parameters = { L("Width", 1600), L("Length", 2000), L("FrameHeight", 300), L("MattressHeight", 250), L("HeadboardHeight", 1000), Mat("Frame Material"), Mat("Mattress Material") },
            Types = { T("Single 900", ("Width", 900)), T("Double 1400", ("Width", 1400)), T("Queen 1600", ("Width", 1600)), T("King 1800", ("Width", 1800)) },
            Shapes =
            {
                Box("Frame", A("-Width/2", "Width/2"), A("-Length/2", "Length/2"), A("100", "FrameHeight"), "Frame Material"),
                Box("Mattress", A("-Width/2+30", "Width/2-30"), A("-Length/2+60", "Length/2-30"), A("FrameHeight", "FrameHeight+MattressHeight"), "Mattress Material"),
                Box("Headboard", A("-Width/2", "Width/2"), A("-Length/2", "-Length/2+60"), A("100", "HeadboardHeight"), "Frame Material"),
            }
        }),
        new("sofa", "Three-seat sofa: base, back, two arms.", () => new FamilySpec
        {
            Template = "furniture", Category = "Furniture",
            Parameters = { L("Length", 2000), L("Depth", 900), L("SeatHeight", 420), L("BackHeight", 850), L("ArmWidth", 150), Mat("Sofa Material") },
            Types = { T("2-seat 1500", ("Length", 1500)), T("3-seat 2000", ("Length", 2000)), T("3-seat 2300", ("Length", 2300)) },
            Shapes =
            {
                Box("Base", A("-Length/2", "Length/2"), A("-Depth/2", "Depth/2"), A("100", "SeatHeight"), "Sofa Material"),
                Box("Back", A("-Length/2", "Length/2"), A("Depth/2-200", "Depth/2"), A("SeatHeight", "BackHeight"), "Sofa Material"),
                Box("ArmL", A("-Length/2", "-Length/2+ArmWidth"), A("-Depth/2", "Depth/2-200"), A("SeatHeight", "SeatHeight+200"), "Sofa Material"),
                Box("ArmR", A("Length/2-ArmWidth", "Length/2"), A("-Depth/2", "Depth/2-200"), A("SeatHeight", "SeatHeight+200"), "Sofa Material"),
            }
        }),
        new("wardrobe", "Wardrobe / tall cabinet with plinth and door panels.", () => new FamilySpec
        {
            Template = "casework", Category = "Casework",
            Parameters = { L("Width", 1200), L("Depth", 600), L("Height", 2100), L("Plinth", 100), L("PanelGap", 3), Mat("Body Material"), Mat("Door Material") },
            Types = { T("1000", ("Width", 1000)), T("1200", ("Width", 1200)), T("1800", ("Width", 1800)), T("2400", ("Width", 2400)) },
            Shapes =
            {
                Box("Body", A("-Width/2", "Width/2"), A("-Depth/2+20", "Depth/2"), A("Plinth", "Height"), "Body Material"),
                Box("PlinthBlock", A("-Width/2+40", "Width/2-40"), A("-Depth/2+60", "Depth/2-20"), A("0", "Plinth"), "Body Material"),
                Box("DoorLeft", A("-Width/2", "-PanelGap"), A("-Depth/2", "-Depth/2+20"), A("Plinth", "Height"), "Door Material"),
                Box("DoorRight", A("PanelGap", "Width/2"), A("-Depth/2", "-Depth/2+20"), A("Plinth", "Height"), "Door Material"),
            }
        }),
        new("kitchen_base_cabinet", "Kitchen base cabinet with countertop and toe kick.", () => new FamilySpec
        {
            Template = "casework", Category = "Casework",
            Parameters = { L("Width", 600), L("Depth", 600), L("CabinetHeight", 720), L("CounterThickness", 40), L("ToeKick", 100), Mat("Cabinet Material"), Mat("Counter Material") },
            Types = { T("400", ("Width", 400)), T("600", ("Width", 600)), T("800", ("Width", 800)), T("1000", ("Width", 1000)) },
            Shapes =
            {
                Box("Body", A("-Width/2", "Width/2"), A("-Depth/2+50", "Depth/2"), A("ToeKick", "CabinetHeight"), "Cabinet Material"),
                Box("Front", A("-Width/2", "Width/2"), A("-Depth/2", "-Depth/2+50"), A("ToeKick", "CabinetHeight"), "Cabinet Material"),
                Box("Counter", A("-Width/2", "Width/2"), A("-Depth/2-20", "Depth/2"), A("CabinetHeight", "CabinetHeight+CounterThickness"), "Counter Material"),
                Box("Toe", A("-Width/2+20", "Width/2-20"), A("-Depth/2+80", "Depth/2-20"), A("0", "ToeKick"), "Cabinet Material"),
            }
        }),
        new("wc", "Toilet (WC) with cistern and drain/water pipe connectors.", () => new FamilySpec
        {
            Template = "plumbing_fixture", Category = "Plumbing Fixtures",
            Parameters = { L("Width", 380), L("Length", 700), L("SeatHeight", 420), L("CisternHeight", 800), Mat("Ceramic") },
            Types = { T("Standard"), T("Compact", ("Length", 600)) },
            Shapes =
            {
                Box("Bowl", A("-Width/2", "Width/2"), A("-Length/2", "Length*0.2"), A("0", "SeatHeight"), "Ceramic"),
                Box("Cistern", A("-Width/2", "Width/2"), A("Length*0.2", "Length/2"), A("200", "CisternHeight"), "Ceramic"),
            },
            Connectors =
            {
                new() { Kind = "pipe", Shape = "Bowl", Point = new[] { 0, -105.0, 0 }, SystemType = "sanitary", DiameterMm = 100, FlowDirection = "out" },
                new() { Kind = "pipe", Shape = "Cistern", Point = new[] { 0, 350.0, 500 }, SystemType = "domestic_cold_water", DiameterMm = 15, FlowDirection = "in" },
            }
        }),
        new("washbasin", "Wash basin with counter-height body and drain/supply connectors.", () => new FamilySpec
        {
            Template = "plumbing_fixture", Category = "Plumbing Fixtures",
            Parameters = { L("Width", 550), L("Depth", 450), L("RimHeight", 850), L("BasinThickness", 150), Mat("Ceramic") },
            Types = { T("550", ("Width", 550)), T("650", ("Width", 650)), T("800", ("Width", 800)) },
            Shapes =
            {
                Box("Basin", A("-Width/2", "Width/2"), A("-Depth/2", "Depth/2"), A("RimHeight-BasinThickness", "RimHeight"), "Ceramic"),
                Box("Pedestal", A("-60", "60"), A("Depth*0.15", "Depth*0.4"), A("0", "RimHeight-BasinThickness"), "Ceramic"),
            },
            Connectors =
            {
                new() { Kind = "pipe", Shape = "Pedestal", Point = new[] { 0, 124.0, 0 }, SystemType = "sanitary", DiameterMm = 40, FlowDirection = "out" },
                new() { Kind = "pipe", Shape = "Basin", Point = new[] { -100, 225.0, 775 }, SystemType = "domestic_cold_water", DiameterMm = 15, FlowDirection = "in" },
            }
        }),
        new("bathtub", "Bathtub (rim, interior void).", () => new FamilySpec
        {
            Template = "plumbing_fixture", Category = "Plumbing Fixtures",
            Parameters = { L("Length", 1700), L("Width", 750), L("Height", 560), L("Wall", 60), Mat("Acrylic") },
            Types = { T("1500", ("Length", 1500)), T("1700", ("Length", 1700)) },
            Shapes =
            {
                Box("Shell", A("-Width/2", "Width/2"), A("-Length/2", "Length/2"), A("0", "Height"), "Acrylic"),
                Box("Cavity", A("-Width/2+Wall", "Width/2-Wall"), A("-Length/2+Wall", "Length/2-Wall"), A("100", "Height+10"), null, null, false),
            },
            Connectors = { new() { Kind = "pipe", Shape = "Shell", Point = new[] { 0, 0.0, 0 }, SystemType = "sanitary", DiameterMm = 40, FlowDirection = "out" } }
        }),
        new("ceiling_light", "Recessed ceiling luminaire (square) with electrical connector.", () => new FamilySpec
        {
            Template = "lighting_fixture_ceiling", Category = "Lighting Fixtures",
            Parameters = { L("Length", 600), L("Width", 600), L("Thickness", 50), Mat("Housing") },
            Types = { T("600x600"), T("600x1200", ("Length", 1200)), T("300x1200", ("Length", 1200), ("Width", 300)) },
            Shapes = { Box("Housing", A("-Length/2", "Length/2"), A("-Width/2", "Width/2"), A("-Thickness", "0"), "Housing") },
            Connectors = { new() { Kind = "electrical", Shape = "Housing", Point = new[] { 0, 0, 0.0 }, SystemType = "power_circuit" } }
        }),
        new("air_diffuser", "Supply/return diffuser with a rectangular duct connector.", () => new FamilySpec
        {
            Template = "mechanical_equipment_ceiling", Category = "Air Terminals",
            Parameters = { L("FaceSize", 600), L("NeckSize", 300), L("FaceThickness", 20), L("NeckHeight", 150), Mat("Diffuser Material") },
            Types = { T("600 face / 300 neck"), T("300 face / 200 neck", ("FaceSize", 300), ("NeckSize", 200)), T("900 face / 400 neck", ("FaceSize", 900), ("NeckSize", 400)) },
            Shapes =
            {
                Box("Face", A("-FaceSize/2", "FaceSize/2"), A("-FaceSize/2", "FaceSize/2"), A("-FaceThickness", "0"), "Diffuser Material"),
                Box("Neck", A("-NeckSize/2", "NeckSize/2"), A("-NeckSize/2", "NeckSize/2"), A("0", "NeckHeight"), "Diffuser Material"),
            },
            Connectors = { new() { Kind = "duct", Shape = "Neck", Point = new[] { 0, 0, 150.0 }, SystemType = "supply_air", ShapeType = "rectangular", WidthMm = 300, HeightMm = 300, FlowDirection = "in" } }
        }),
        new("fan_coil", "Fan-coil unit: casing, supply/return hydronic connectors, condensate drain, power.", () => new FamilySpec
        {
            Template = "mechanical_equipment", Category = "Mechanical Equipment",
            Parameters = { L("Length", 900), L("Width", 500), L("Height", 300), Mat("Casing Material") },
            Types = { T("FCU-small"), T("FCU-medium", ("Length", 1100), ("Width", 550)), T("FCU-large", ("Length", 1400), ("Width", 600), ("Height", 350)) },
            Shapes = { Box("Casing", A("-Length/2", "Length/2"), A("-Width/2", "Width/2"), A("0", "Height"), "Casing Material") },
            Connectors =
            {
                new() { Kind = "pipe", Shape = "Casing", Point = new[] { -450.0, 0, 150 }, SystemType = "hydronic_supply", DiameterMm = 20, FlowDirection = "in" },
                new() { Kind = "pipe", Shape = "Casing", Point = new[] { 450.0, 0, 150 }, SystemType = "hydronic_return", DiameterMm = 20, FlowDirection = "out" },
                new() { Kind = "electrical", Shape = "Casing", Point = new[] { 0, 250.0, 150 }, SystemType = "power_circuit" },
            }
        }),
        new("ahu", "Air handling unit box with supply/return duct connectors and chilled-water coil connections.", () => new FamilySpec
        {
            Template = "mechanical_equipment", Category = "Mechanical Equipment",
            Parameters = { L("Length", 3000), L("Width", 1200), L("Height", 1500), L("DuctWidth", 800), L("DuctHeight", 500), Mat("Casing Material") },
            Types = { T("AHU-5000"), T("AHU-10000", ("Length", 4000), ("Width", 1600), ("Height", 1900), ("DuctWidth", 1100), ("DuctHeight", 700)) },
            Shapes = { Box("Casing", A("-Length/2", "Length/2"), A("-Width/2", "Width/2"), A("0", "Height"), "Casing Material") },
            Connectors =
            {
                new() { Kind = "duct", Shape = "Casing", Point = new[] { -1500.0, 0, 750 }, SystemType = "return_air", ShapeType = "rectangular", WidthMm = 800, HeightMm = 500, FlowDirection = "in" },
                new() { Kind = "duct", Shape = "Casing", Point = new[] { 1500.0, 0, 750 }, SystemType = "supply_air", ShapeType = "rectangular", WidthMm = 800, HeightMm = 500, FlowDirection = "out" },
                new() { Kind = "pipe", Shape = "Casing", Point = new[] { 0, -600.0, 400 }, SystemType = "hydronic_supply", DiameterMm = 50, FlowDirection = "in" },
                new() { Kind = "pipe", Shape = "Casing", Point = new[] { 200, -600.0, 400 }, SystemType = "hydronic_return", DiameterMm = 50, FlowDirection = "out" },
                new() { Kind = "electrical", Shape = "Casing", Point = new[] { 0, 600.0, 750 }, SystemType = "power_circuit" },
            }
        }),
        new("pump", "Centrifugal pump: motor cylinder, base, suction/discharge connectors.", () => new FamilySpec
        {
            Template = "mechanical_equipment", Category = "Mechanical Equipment",
            Parameters = { L("BaseLength", 800), L("BaseWidth", 350), L("MotorDiameter", 250), L("PipeDia", 50), Mat("Pump Material") },
            Types = { T("Pump-small"), T("Pump-large", ("BaseLength", 1100), ("BaseWidth", 450), ("MotorDiameter", 330), ("PipeDia", 80)) },
            Shapes =
            {
                Box("Base", A("-BaseLength/2", "BaseLength/2"), A("-BaseWidth/2", "BaseWidth/2"), A("0", "80"), "Pump Material"),
                Cyl("Motor", A("BaseLength*0.25", "0"), "MotorDiameter/2", A("80", "80+MotorDiameter"), "Pump Material"),
                Box("Volute", A("-BaseLength*0.4", "-BaseLength*0.15"), A("-120", "120"), A("80", "380"), "Pump Material"),
            },
            Connectors =
            {
                new() { Kind = "pipe", Shape = "Volute", Point = new[] { -220.0, 0, 380 }, SystemType = "hydronic_supply", DiameterMm = 50, FlowDirection = "out" },
                new() { Kind = "pipe", Shape = "Volute", Point = new[] { -220.0, -120, 230 }, SystemType = "hydronic_return", DiameterMm = 50, FlowDirection = "in" },
                new() { Kind = "electrical", Shape = "Base", Point = new[] { 0, 175.0, 40 }, SystemType = "power_circuit" },
            }
        }),
        new("electrical_panel", "Electrical distribution board (wall mounted box) with power connector.", () => new FamilySpec
        {
            Template = "electrical_equipment", Category = "Electrical Equipment",
            Parameters = { L("Width", 600), L("Height", 800), L("Depth", 200), Mat("Panel Material") },
            Types = { T("DB-12 way", ("Width", 400), ("Height", 500)), T("DB-24 way"), T("DB-48 way", ("Width", 800), ("Height", 1100), ("Depth", 250)) },
            Shapes = { Box("Enclosure", A("-Width/2", "Width/2"), A("0", "Depth"), A("0", "Height"), "Panel Material") },
            Connectors = { new() { Kind = "electrical", Shape = "Enclosure", Point = new[] { 0, 0.0, 400 }, SystemType = "power_circuit" } }
        }),
        new("round_column", "Round concrete column driven by level constraints.", () => new FamilySpec
        {
            Template = "structural_column", Category = "Structural Columns",
            Parameters = { L("Diameter", 400) },
            Types = { T("D300", ("Diameter", 300)), T("D400", ("Diameter", 400)), T("D500", ("Diameter", 500)), T("D600", ("Diameter", 600)) },
            Shapes = { new FamShapeSpec { Kind = "cylinder", Name = "Column", Center = A("0", "0"), Radius = "Diameter/2", Z = A("level:Lower Ref. Level", "level:Upper Ref Level"), MaterialParameter = "Structural Material" } }
        }),
        new("door_single", "Single-leaf door types (template geometry, type sizes 800-1000 x 2100-2400).", () => new FamilySpec
        {
            Template = "door", Category = "Doors", ClearGeometry = false,
            Types = { T("800 x 2100", ("Width", 800), ("Height", 2100)), T("900 x 2100", ("Width", 900), ("Height", 2100)), T("1000 x 2100", ("Width", 1000), ("Height", 2100)), T("900 x 2400", ("Width", 900), ("Height", 2400)) },
        }),
        new("door_double", "Double-leaf door types (1200-1800 wide).", () => new FamilySpec
        {
            Template = "door", Category = "Doors", ClearGeometry = false,
            Types = { T("1200 x 2100", ("Width", 1200), ("Height", 2100)), T("1500 x 2100", ("Width", 1500), ("Height", 2100)), T("1800 x 2400", ("Width", 1800), ("Height", 2400)) },
        }),
        new("window_standard", "Window types (template geometry) 600-2400 wide.", () => new FamilySpec
        {
            Template = "window", Category = "Windows", ClearGeometry = false,
            Types = { T("600 x 600", ("Width", 600), ("Height", 600)), T("1200 x 1200", ("Width", 1200), ("Height", 1200)), T("1500 x 1500", ("Width", 1500), ("Height", 1500)), T("2400 x 1500", ("Width", 2400), ("Height", 1500)) },
        }),
    };

    [Tool("list_standard_families", "Lists the ready-made parametric families that create_standard_family can build (furniture, casework, plumbing, lighting, HVAC equipment, electrical, columns, doors, windows).", Category = "Family", ReadOnly = true, NoDocument = true)]
    public static object ListStandardFamilies()
        => new JsonObject { ["families"] = new JsonArray(Kinds.Select(k => (JsonNode)new JsonObject { ["kind"] = k.Id, ["description"] = k.Description }).ToArray()) };

    [Tool("create_standard_family", """
        Builds a professional parametric family from the built-in library (see list_standard_families) and loads it into the project: named reference planes, labelled dimensions, formulas, material parameters, several types, connectors for MEP kinds, flex-tested.
        @kind Library kind e.g. table, chair, bed, sofa, wardrobe, kitchen_base_cabinet, wc, washbasin, bathtub, ceiling_light, air_diffuser, fan_coil, ahu, pump, electrical_panel, round_column, door_single, door_double, window_standard.
        @name Family name (default 'Arian <kind>'). @load Load into the project (default true). @extra_types Optional extra types [{name, values:{Param: mm}}].
        """, Category = "Family", NoDocument = true, NoTransaction = true)]
    public static object CreateStandardFamily(string kind, string name = null, bool load = true, FamTypeSpec[] extraTypes = null)
    {
        var k = Kinds.FirstOrDefault(x => x.Id.Equals(kind, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException("Unknown kind. Available: " + string.Join(", ", Kinds.Select(x => x.Id)));
        var spec = k.Build();
        spec.Name = name ?? "Arian " + k.Id.Replace('_', ' ');
        spec.Load = load;
        if (extraTypes != null) spec.Types.AddRange(extraTypes);
        return FamilySpecTools.FamilyBuildFromSpec(spec);
    }

    // ------------------------------------------------------------------ QA
    [Tool("family_qa_report", """
        Quality report of the open family document: category, parameters/types, unnamed reference planes, geometry without material binding, missing subcategories, import instances (should be avoided), nested families, connectors, file size, and a flex test. Returns issues + a 0-100 score.
        """, Category = "Family", NoTransaction = true)]
    public static object FamilyQaReport()
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        var issues = new JsonArray(); int penalty = 0;
        void Issue(string sev, string msg, int p) { issues.Add(new JsonObject { ["severity"] = sev, ["message"] = msg }); penalty += p; }
        var cat = fd.OwnerFamily?.FamilyCategory?.Name;
        var pars = fm.Parameters.Cast<FamilyParameter>().ToList();
        int types = fm.Types.Size;
        if (types == 0) Issue("warning", "The family has no named types.", 5);
        var planes = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
        int unnamed = planes.Count(p => string.IsNullOrWhiteSpace(p.Name) || p.Name == "Reference Plane");
        if (unnamed > 0) Issue("info", $"{unnamed} reference plane(s) have the default name — name them so users can align and so dimensions are readable.", 3);
        var forms = new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).Cast<GenericForm>().Where(f => f.IsSolid).ToList();
        if (forms.Count == 0) Issue("error", "The family contains no solid geometry.", 30);
        int noMat = forms.Count(f => { var mp = f.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM); return mp != null && mp.AsElementId() == ElementId.InvalidElementId && fm.GetAssociatedFamilyParameter(mp) == null; });
        if (noMat > 0) Issue("info", $"{noMat} solid(s) have no material or material parameter (they will render as 'By Category').", 4);
        int noSub = forms.Count(f => f.Subcategory == null);
        if (noSub > 0 && cat != null) Issue("info", $"{noSub} solid(s) have no subcategory (needed for graphic control of frames, glass, panels).", 2);
        int imports = new FilteredElementCollector(fd).OfClass(typeof(ImportInstance)).GetElementCount();
        if (imports > 0) Issue("warning", $"{imports} imported CAD/geometry instance(s) — avoid imports in families (slow and uncontrolled).", 10);
        int nested = new FilteredElementCollector(fd).OfClass(typeof(FamilyInstance)).GetElementCount();
        var dims = new FilteredElementCollector(fd).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
        int labelled = dims.Count(d => { try { return d.FamilyLabel != null; } catch { return false; } });
        if (forms.Count > 0 && labelled == 0 && pars.Any(p => p.Definition.GetDataType() == SpecTypeId.Length)) Issue("warning", "No dimension is labelled with a parameter — geometry will not follow length parameters.", 15);
        foreach (var n in new[] { "Description", "Manufacturer", "Model" })
            if (!pars.Any(p => p.Definition.Name == n) && fd.OwnerFamily != null) { /* built-in identity params are not always listed; skip */ }
        string path = fd.PathName; long size = 0; if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) size = new System.IO.FileInfo(path).Length;
        if (size > 3_000_000) Issue("warning", $"File size {size / 1048576.0:0.0} MB is large for a family.", 5);
        var conn = new FilteredElementCollector(fd).OfCategory(BuiltInCategory.OST_ConnectorElem).GetElementCount();
        if (cat != null && (cat.Contains("Equipment") || cat.Contains("Terminals")) && conn == 0) Issue("warning", "MEP equipment/terminal family has no connectors.", 10);
        JsonNode flex = null;
        try { flex = FamilyDocTools.FamilyFlexTest(null, null) as JsonNode; } catch (Exception ex) { Issue("warning", "Flex test could not run: " + ex.Message, 5); }
        if (flex is JsonObject fo && fo["failures"] is JsonArray fa && fa.Count > 0) Issue("error", $"Flex test failed for {fa.Count} case(s).", 20);
        return new JsonObject
        {
            ["category"] = cat, ["parameters"] = pars.Count, ["types"] = types, ["solids"] = forms.Count, ["reference_planes"] = planes.Count, ["labelled_dimensions"] = labelled,
            ["nested_families"] = nested, ["connectors"] = conn, ["file_size_kb"] = size / 1024, ["score"] = Math.Max(0, 100 - penalty), ["issues"] = issues, ["flex"] = flex?.DeepClone()
        };
    }

    // ------------------------------------------------------------------ type catalogs / CSV
    static string CatalogHeader(FamilyParameter p)
    {
        var dt = p.Definition.GetDataType();
        string n = p.Definition.Name;
        if (dt == SpecTypeId.Length) return $"{n}##length##millimeters";
        if (dt == SpecTypeId.Angle) return $"{n}##angle##degrees";
        if (dt == SpecTypeId.Number) return $"{n}##number##general";
        if (dt == SpecTypeId.Int.Integer) return $"{n}##integer##general";
        if (dt == SpecTypeId.String.Text) return $"{n}##other##";
        return null;
    }

    [Tool("family_export_type_catalog", "Writes a Revit type catalog (.txt next to the .rfa, same base name) for the open family so users can load only the types they need. @path Output path (default: next to the family file). Length/angle/number/integer/text type parameters are exported.", Category = "Family", NoTransaction = true)]
    public static object FamilyExportTypeCatalog(string path = null)
    {
        var fd = Ctx.RequireFamilyDoc(); var fm = fd.FamilyManager;
        path ??= System.IO.Path.ChangeExtension(fd.PathName, ".txt");
        if (string.IsNullOrEmpty(path)) throw new ToolException("Save the family first or pass @path.");
        var cols = fm.Parameters.Cast<FamilyParameter>().Where(p => !p.IsInstance && !p.IsReporting && p.Formula == null && CatalogHeader(p) != null).OrderBy(p => p.Definition.Name).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("," + string.Join(",", cols.Select(CatalogHeader)));
        var original = fm.CurrentType;
        foreach (FamilyType t in fm.Types)
        {
            var cells = new List<string> { t.Name };
            foreach (var p in cols)
            {
                var dt = p.Definition.GetDataType();
                string v;
                if (dt == SpecTypeId.Length) v = Math.Round(UnitUtils.ConvertFromInternalUnits(t.AsDouble(p) ?? 0, UnitTypeId.Millimeters), 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (dt == SpecTypeId.Angle) v = Math.Round(UnitUtils.ConvertFromInternalUnits(t.AsDouble(p) ?? 0, UnitTypeId.Degrees), 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (dt == SpecTypeId.Number) v = (t.AsDouble(p) ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (dt == SpecTypeId.Int.Integer) v = (t.AsInteger(p) ?? 0).ToString();
                else v = t.AsString(p) ?? "";
                cells.Add(v.Replace(",", " "));
            }
            sb.AppendLine(string.Join(",", cells));
        }
        System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return new JsonObject { ["path"] = path, ["types"] = fm.Types.Size, ["columns"] = cols.Count };
    }

    [Tool("family_import_types_csv", "Creates family types from a CSV file (Family Types from Excel): the first row holds parameter names (first column = type name), following rows the values (lengths in mm). Existing types are updated. @path CSV path (UTF-8, comma or semicolon separated).", Category = "Family")]
    public static object FamilyImportTypesCsv(string path)
    {
        if (!System.IO.File.Exists(path)) throw new ToolException("File not found: " + path);
        var lines = System.IO.File.ReadAllLines(path, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count < 2) throw new ToolException("CSV needs a header row and at least one data row.");
        char sep = lines[0].Contains(';') ? ';' : ',';
        var head = lines[0].Split(sep).Select(s => s.Trim()).ToArray();
        int made = 0; var errors = new JsonArray();
        foreach (var line in lines.Skip(1))
        {
            var c = line.Split(sep).Select(s => s.Trim()).ToArray();
            if (c.Length == 0 || c[0] == "") continue;
            var vals = new Dictionary<string, JsonNode>();
            for (int i = 1; i < head.Length && i < c.Length; i++)
            {
                if (c[i] == "") continue;
                if (double.TryParse(c[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) vals[head[i]] = JsonValue.Create(d);
                else vals[head[i]] = JsonValue.Create(c[i]);
            }
            try { FamilyDocTools.FamilyAddType(c[0], vals, false); made++; }
            catch (Exception ex) { if (errors.Count < 5) errors.Add(c[0] + ": " + ex.Message); }
        }
        return new JsonObject { ["types_created_or_updated"] = made, ["errors"] = errors };
    }

    // ------------------------------------------------------------------ nested
    [Tool("family_add_nested", """
        Loads another family (.rfa) into the open family document and places an instance of it (a nested family) — e.g. a handle inside a door or a leg inside a table.
        @family_path .rfa path. @type_name Type to place (default: first). @point [x,y,z] mm. @shared Also mark the nested family as shared (so it can be scheduled/tagged separately).
        """, Category = "Family")]
    public static object FamilyAddNested(string familyPath, string typeName = null, double[] point = null, bool shared = false)
    {
        var fd = Ctx.RequireFamilyDoc();
        if (!System.IO.File.Exists(familyPath)) throw new ToolException("File not found: " + familyPath);
        if (!fd.LoadFamily(familyPath, out Family fam) && fam == null)
        {
            fam = new FilteredElementCollector(fd).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == System.IO.Path.GetFileNameWithoutExtension(familyPath))
                ?? throw new ToolException("Revit could not load the family.");
        }
        var sym = fam.GetFamilySymbolIds().Select(i => (FamilySymbol)fd.GetElement(i)).FirstOrDefault(s => typeName == null || s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolException("Type not found in the nested family.");
        if (!sym.IsActive) sym.Activate();
        var p = point == null ? XYZ.Zero : U.Pt(point);
        var inst = fd.FamilyCreate.NewFamilyInstance(p, sym, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        if (shared) { try { fam.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.Set(1); } catch { } }
        return new JsonObject { ["id"] = inst.Id.Value, ["family"] = fam.Name, ["type"] = sym.Name };
    }
}
