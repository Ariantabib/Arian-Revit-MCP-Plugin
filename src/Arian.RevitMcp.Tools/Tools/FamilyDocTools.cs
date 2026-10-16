using System.Reflection;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace Arian.RevitMcp.Tools;

/// <summary>Helpers for working inside family documents.</summary>
static class FamUtil
{
    public const string TemplateDir = @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English";

    public static readonly Dictionary<string, string> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["generic_model"] = "Metric Generic Model.rft",
        ["generic_model_wall_based"] = "Metric Generic Model wall based.rft",
        ["generic_model_face_based"] = "Metric Generic Model face based.rft",
        ["generic_model_ceiling_based"] = "Metric Generic Model ceiling based.rft",
        ["generic_model_floor_based"] = "Metric Generic Model floor based.rft",
        ["generic_model_line_based"] = "Metric Generic Model line based.rft",
        ["door"] = "Metric Door.rft",
        ["window"] = "Metric Window.rft",
        ["furniture"] = "Metric Furniture.rft",
        ["furniture_system"] = "Metric Furniture System.rft",
        ["casework"] = "Metric Casework.rft",
        ["casework_wall_based"] = "Metric Casework wall based.rft",
        ["column"] = "Metric Column.rft",
        ["structural_column"] = "Metric Structural Column.rft",
        ["structural_framing"] = "Metric Structural Framing - Beams and Braces.rft",
        ["structural_foundation"] = "Metric Structural Foundation.rft",
        ["structural_truss"] = "Metric Structural Trusses.rft",
        ["lighting_fixture"] = "Metric Lighting Fixture.rft",
        ["lighting_fixture_ceiling"] = "Metric Lighting Fixture ceiling based.rft",
        ["lighting_fixture_wall"] = "Metric Lighting Fixture wall based.rft",
        ["linear_lighting"] = "Metric Linear Lighting Fixture.rft",
        ["electrical_equipment"] = "Metric Electrical Equipment.rft",
        ["electrical_fixture"] = "Metric Electrical Fixture.rft",
        ["electrical_fixture_wall"] = "Metric Electrical Fixture wall based.rft",
        ["mechanical_equipment"] = "Metric Mechanical Equipment.rft",
        ["mechanical_equipment_ceiling"] = "Metric Mechanical Equipment ceiling based.rft",
        ["mechanical_equipment_wall"] = "Metric Mechanical Equipment wall based.rft",
        ["plumbing_fixture"] = "Metric Plumbing Fixture.rft",
        ["plumbing_fixture_wall"] = "Metric Plumbing Fixture wall based.rft",
        ["specialty_equipment"] = "Metric Specialty Equipment.rft",
        ["fire_alarm_device"] = "Metric Fire Alarm Device.rft",
        ["data_device"] = "Metric Data Device.rft",
        ["telephone_device"] = "Metric Telephone Device.rft",
        ["profile"] = "Metric Profile.rft",
        ["detail_item"] = "Metric Detail Item.rft",
        ["planting"] = "Metric Planting.rft",
        ["site"] = "Metric Site.rft",
        ["entourage"] = "Metric Entourage.rft",
        ["parking"] = "Metric Parking.rft",
        ["railing_baluster"] = "Metric Baluster.rft",
        ["curtain_panel"] = "Metric Curtain Wall Panel.rft",
        ["duct_elbow"] = "Metric Duct Elbow.rft",
        ["duct_tee"] = "Metric Duct Tee.rft",
        ["duct_transition"] = "Metric Duct Transition.rft",
    };

    public static string ResolveTemplate(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) t = "generic_model";
        if (Templates.TryGetValue(t, out var f)) t = Path.Combine(TemplateDir, f);
        else if (!File.Exists(t) && File.Exists(Path.Combine(TemplateDir, t))) t = Path.Combine(TemplateDir, t);
        else if (!File.Exists(t) && File.Exists(Path.Combine(TemplateDir, t + ".rft"))) t = Path.Combine(TemplateDir, t + ".rft");
        if (!File.Exists(t)) throw new ToolException($"Family template '{t}' not found. Use list_family_templates.");
        return t;
    }

    public static ForgeTypeId Group(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return GroupTypeId.General;
        string key = s.Replace("_", "").Replace(" ", "").ToLowerInvariant();
        foreach (var p in typeof(GroupTypeId).GetProperties(BindingFlags.Public | BindingFlags.Static))
            if (p.PropertyType == typeof(ForgeTypeId) && p.Name.ToLowerInvariant() == key) return (ForgeTypeId)p.GetValue(null);
        // friendly aliases
        return key switch
        {
            "dimensions" => GroupTypeId.Geometry,
            "identity" => GroupTypeId.IdentityData,
            "constraints" => GroupTypeId.Constraints,
            "materialsandfinishes" => GroupTypeId.Materials,
            _ => GroupTypeId.General,
        };
    }

    public static ForgeTypeId Spec(string s)
    {
        string k = (s ?? "length").Trim().ToLowerInvariant().Replace(" ", "_");
        switch (k)
        {
            case "length": return SpecTypeId.Length;
            case "angle": return SpecTypeId.Angle;
            case "area": return SpecTypeId.Area;
            case "volume": return SpecTypeId.Volume;
            case "number": return SpecTypeId.Number;
            case "integer": case "int": return SpecTypeId.Int.Integer;
            case "text": case "string": return SpecTypeId.String.Text;
            case "multiline_text": return SpecTypeId.String.MultilineText;
            case "url": return SpecTypeId.String.Url;
            case "yesno": case "yes_no": case "boolean": case "bool": return SpecTypeId.Boolean.YesNo;
            case "material": return SpecTypeId.Reference.Material;
            case "image": return SpecTypeId.Reference.Image;
            case "currency": return SpecTypeId.Currency;
            case "mass": return SpecTypeId.Mass;
            case "force": return SpecTypeId.Force;
            case "electrical_power": case "power": return SpecTypeId.ElectricalPower;
            case "voltage": case "electrical_potential": return SpecTypeId.ElectricalPotential;
            case "current": case "electrical_current": return SpecTypeId.Current;
            case "airflow": return SpecTypeId.AirFlow;
            case "flow": case "pipe_flow": return SpecTypeId.Flow;
            case "pressure": return SpecTypeId.HvacPressure;
            case "temperature": return SpecTypeId.HvacTemperature;
            case "luminous_flux": return SpecTypeId.LuminousFlux;
            case "wattage": case "heat_flow": return SpecTypeId.HeatingLoad;
        }
        if (k.Contains(":")) return new ForgeTypeId(s.Trim());
        throw new ToolException($"Unknown data_type '{s}'. Use length, angle, area, volume, number, integer, text, yesno, material, url, currency, mass, force, electrical_power, voltage, current, airflow, flow, pressure, temperature, luminous_flux — or a raw spec id like 'autodesk.spec.aec:length-2.0.0'.");
    }

    public static ViewPlan PlanView(Document d)
        => new FilteredElementCollector(d).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
           ?? new FilteredElementCollector(d).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate)
           ?? throw new ToolException("The family has no plan view.");

    public static View Elevation(Document d, params string[] names)
    {
        var secs = new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && (v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Section)).ToList();
        foreach (var n in names)
        {
            var hit = secs.FirstOrDefault(v => v.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return secs.FirstOrDefault();
    }

    public static ReferencePlane RefPlane(Document d, string name, bool nullable = false)
    {
        var rp = new FilteredElementCollector(d).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (rp == null && !nullable)
        {
            var names = new FilteredElementCollector(d).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().Select(r => r.Name);
            throw new ToolException($"Reference plane '{name}' not found. Existing: {string.Join(", ", names)}");
        }
        return rp;
    }

    public static FamilyParameter Param(Document d, string name, bool nullable = false)
    {
        var p = d.FamilyManager.get_Parameter(name);
        if (p == null && !nullable)
            throw new ToolException($"Family parameter '{name}' not found. Existing: {string.Join(", ", d.FamilyManager.Parameters.Cast<FamilyParameter>().Select(x => x.Definition.Name))}");
        return p;
    }

    public static void EnsureType(FamilyManager fm)
    {
        if (fm.Types.Size == 0 || fm.CurrentType == null) fm.NewType(fm.Types.Size == 0 ? "Default" : fm.Types.Cast<FamilyType>().First().Name);
    }

    public static XYZ Point3(string plane, double u, double v, double offsetMm) => plane switch
    {
        "xy" => new XYZ(U.Mm(u), U.Mm(v), U.Mm(offsetMm)),
        "xz" => new XYZ(U.Mm(u), U.Mm(offsetMm), U.Mm(v)),
        "yz" => new XYZ(U.Mm(offsetMm), U.Mm(u), U.Mm(v)),
        _ => throw new ToolException("plane must be 'xy' (plan), 'xz' (front elevation) or 'yz' (side elevation)"),
    };

    public static XYZ Normal(string plane) => plane switch { "xy" => XYZ.BasisZ, "xz" => XYZ.BasisY, "yz" => XYZ.BasisX, _ => throw new ToolException("bad plane") };

    public static SketchPlane Sketch(Document d, string plane, double offsetMm)
    {
        var origin = Point3(plane, 0, 0, offsetMm);
        return SketchPlane.Create(d, Plane.CreateByNormalAndOrigin(Normal(plane), origin));
    }

    /// <summary>A material parameter must hold a valid material before geometry can be bound to it (otherwise Revit fails to regenerate).</summary>
    public static void EnsureMaterialValue(Document fd, FamilyParameter fp)
    {
        if (fp == null || fp.StorageType != StorageType.ElementId) return;
        var fm = fd.FamilyManager;
        EnsureType(fm);
        bool hasValid = false;
        foreach (FamilyType t in fm.Types)
        {
            var id = t.AsElementId(fp);
            if (id != null && id != ElementId.InvalidElementId && fd.GetElement(id) is Material) { hasValid = true; break; }
        }
        if (hasValid) return;
        var mat = new FilteredElementCollector(fd).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name.StartsWith("Arian Default"));
        if (mat == null)
        {
            mat = (Material)fd.GetElement(Material.Create(fd, "Arian Default Material"));
            mat.Color = new Color(170, 170, 170);
        }
        foreach (FamilyType t in fm.Types) { fm.CurrentType = t; fm.Set(fp, mat.Id); }
    }

    /// <summary>Revit leaves 'Name.0001.rfa' backups next to every saved family; remove them to keep the library folder clean.</summary>
    public static void CleanBackups(string rfaPath)
    {
        try
        {
            if (string.IsNullOrEmpty(rfaPath)) return;
            var dir = Path.GetDirectoryName(rfaPath);
            var stem = Path.GetFileNameWithoutExtension(rfaPath);
            if (dir == null || !Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, stem + ".0*.rfa"))
            {
                var mid = Path.GetFileNameWithoutExtension(f).Substring(stem.Length);
                if (mid.Length == 5 && mid[0] == '.' && mid.Skip(1).All(char.IsDigit)) File.Delete(f);
            }
        }
        catch { }
    }

    public static string FormatValue(FamilyType t, FamilyParameter p)
    {
        try
        {
            if (!t.HasValue(p)) return null;
            switch (p.StorageType)
            {
                case StorageType.Double: return U.ToApi(t.AsDouble(p) ?? 0, p.Definition.GetDataType()).ToString("0.###");
                case StorageType.Integer: return (t.AsInteger(p) ?? 0).ToString();
                case StorageType.String: return t.AsString(p);
                case StorageType.ElementId: return (t.AsElementId(p)?.Value ?? -1).ToString();
            }
        }
        catch { }
        return null;
    }
}

public static class FamilyDocTools
{
    // =============================================================== lifecycle
    [Tool("list_family_templates", "Lists family templates (.rft): aliases usable with family_create plus all template files.", Category = "Family", NoDocument = true, ReadOnly = true)]
    public static object ListFamilyTemplates()
    {
        var files = Directory.Exists(FamUtil.TemplateDir) ? Directory.GetFiles(FamUtil.TemplateDir, "*.rft").Select(Path.GetFileName).OrderBy(x => x).ToArray() : Array.Empty<string>();
        return new JsonObject
        {
            ["aliases"] = new JsonObject(FamUtil.Templates.Select(kv => new KeyValuePair<string, JsonNode>(kv.Key, kv.Value))),
            ["files"] = new JsonArray(files.Select(f => (JsonNode)f).ToArray()),
        };
    }

    [Tool("family_create", """
        Creates a NEW family document from a template and saves it. Returns the 'document' title to pass to every other family_* tool. Start every from-scratch family here.
        Typical flow: family_create -> family_add_parameter / family_setup_dimensions -> family_add_extrusion/box/cylinder... -> family_add_type -> family_save -> family_load_into_project.
        @template Alias (generic_model, door, window, furniture, casework, structural_column, structural_framing, structural_foundation, lighting_fixture, mechanical_equipment, electrical_equipment, plumbing_fixture, profile, detail_item, ...) or a template path. See list_family_templates.
        @name Family name (file name).
        @folder Folder to save into (default Documents\ArianFamilies).
        @category Optionally change the family category (e.g. 'Furniture', 'Specialty Equipment', 'Casework').
        """, Category = "Family", NoDocument = true, NoTransaction = true)]
    public static object FamilyCreate(string name, string template = "generic_model", string folder = null, string category = null)
    {
        var tpl = FamUtil.ResolveTemplate(template);
        var fd = Ctx.App.NewFamilyDocument(tpl);
        if (!string.IsNullOrWhiteSpace(category))
        {
            using var t = new Transaction(fd, "category");
            t.Start();
            fd.OwnerFamily.FamilyCategory = Find.Category(fd, category);
            t.Commit();
        }
        var dir = string.IsNullOrWhiteSpace(folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianFamilies") : folder;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".rfa");
        fd.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        return Info(fd);
    }

    [Tool("family_open", "Opens an existing .rfa file for editing (without loading it into a project). Returns the document title.", Category = "Family", NoDocument = true, NoTransaction = true)]
    public static object FamilyOpen(string path)
    {
        if (!File.Exists(path)) throw new ToolException("File not found: " + path);
        var fd = Ctx.App.OpenDocumentFile(path);
        if (!fd.IsFamilyDocument) throw new ToolException("That file is not a family.");
        return Info(fd);
    }

    [Tool("family_save", "Saves the family document (optionally to a new path).", Category = "Family", NoTransaction = true)]
    public static object FamilySave(string path = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        if (!string.IsNullOrWhiteSpace(path)) { Directory.CreateDirectory(Path.GetDirectoryName(path)); fd.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true }); }
        else fd.Save();
        FamUtil.CleanBackups(fd.PathName);
        return new JsonObject { ["saved"] = fd.PathName, ["title"] = fd.Title };
    }

    [Tool("family_close", "Closes a family document.", Category = "Family", NoTransaction = true)]
    public static object FamilyClose(bool save = false)
    {
        var fd = Ctx.RequireFamilyDoc();
        string t = fd.Title;
        fd.Close(save);
        return new JsonObject { ["closed"] = t };
    }

    [Tool("family_load_into_project", """
        Loads the family document into a project so it can be placed. Saves first if it has a path.
        @project Title of the target project (default: the active project).
        @overwrite Overwrite the existing family + parameter values if already loaded.
        @close Close the family document afterwards.
        """, Category = "Family", NoTransaction = true)]
    public static object FamilyLoadIntoProject(string project = null, bool overwrite = true, bool close = false)
    {
        var fd = Ctx.RequireFamilyDoc();
        Document proj = null;
        if (!string.IsNullOrWhiteSpace(project)) proj = Ctx.FindDocument(project);
        else
        {
            foreach (Document d in Ctx.App.Documents) if (!d.IsFamilyDocument && !d.IsLinked && Ctx.UiDoc != null && d.Equals(Ctx.UiDoc.Document)) proj = d;
            proj ??= Ctx.App.Documents.Cast<Document>().FirstOrDefault(d => !d.IsFamilyDocument && !d.IsLinked);
        }
        if (proj == null || proj.IsFamilyDocument) throw new ToolException("No project document to load into. Open or create a project first.");
        if (!string.IsNullOrEmpty(fd.PathName) && fd.IsModified) { try { fd.Save(); } catch { } }
        var fam = fd.LoadFamily(proj, new FamilyLoadOptions(overwrite));
        if (fam == null) throw new ToolException("Revit could not load the family into the project (family category invalid for loading, or open errors).");
        var types = new JsonArray();
        foreach (var id in fam.GetFamilySymbolIds()) { var s = proj.GetElement(id) as FamilySymbol; if (s != null) types.Add(new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name }); }
        var res = new JsonObject { ["family"] = fam.Name, ["family_id"] = fam.Id.Value, ["category"] = fam.FamilyCategory?.Name, ["loaded_into"] = proj.Title, ["types"] = types };
        string famPath = fd.PathName;
        if (close) fd.Close(false);
        FamUtil.CleanBackups(famPath);
        return res;
    }

    static JsonObject Info(Document fd) => new()
    {
        ["document"] = fd.Title,
        ["path"] = fd.PathName,
        ["category"] = fd.OwnerFamily.FamilyCategory?.Name,
        ["placement_type"] = fd.OwnerFamily.FamilyPlacementType.ToString(),
        ["reference_planes"] = new JsonArray(new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().Select(r => (JsonNode)r.Name).ToArray()),
        ["views"] = new JsonArray(new FilteredElementCollector(fd).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser).Select(v => (JsonNode)(v.Name + " [" + v.ViewType + "]")).ToArray()),
        ["hint"] = "Pass document='" + fd.Title + "' to the other family_* tools.",
    };

    [Tool("family_get_info", "Full state of a family document: category, parameters (with formulas), types with their values, reference planes, forms (extrusions, sweeps...), connectors.", Category = "Family", ReadOnly = true)]
    public static object FamilyGetInfo()
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        var o = Info(fd);
        o.Remove("hint");
        var ps = new JsonArray();
        var plist = fm.Parameters.Cast<FamilyParameter>().OrderBy(p => p.Definition.Name).ToList();
        foreach (var p in plist)
        {
            var po = new JsonObject
            {
                ["name"] = p.Definition.Name,
                ["kind"] = p.IsInstance ? "instance" : "type",
                ["data_type"] = SafeSpec(p),
                ["storage"] = p.StorageType.ToString().ToLowerInvariant(),
                ["read_only"] = p.IsReadOnly,
            };
            if (p.IsDeterminedByFormula) po["formula"] = p.Formula;
            try { po["group"] = LabelUtils.GetLabelForGroup(p.Definition.GetGroupTypeId()); } catch { }
            ps.Add(po);
        }
        o["parameters"] = ps;
        var types = new JsonArray();
        foreach (FamilyType t in fm.Types)
        {
            var vals = new JsonObject();
            foreach (var p in plist) { var v = FamUtil.FormatValue(t, p); if (v != null) vals[p.Definition.Name] = v; }
            types.Add(new JsonObject { ["name"] = t.Name, ["values"] = vals });
        }
        o["types"] = types;
        var rps = new JsonArray();
        foreach (ReferencePlane r in new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)))
        {
            var pl = r.GetPlane();
            rps.Add(new JsonObject { ["name"] = r.Name, ["origin_mm"] = Dto.JsonArr(pl.Origin), ["normal"] = Dto.JsonArr(pl.Normal, false) });
        }
        o["reference_planes_detail"] = rps;
        var forms = new JsonArray();
        foreach (var f in new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).Cast<GenericForm>())
            forms.Add(new JsonObject { ["id"] = f.Id.Value, ["kind"] = f.GetType().Name, ["solid"] = f.IsSolid, ["subcategory"] = f.Subcategory?.Name, ["name"] = Dto.SafeName(f) });
        o["forms"] = forms;
        var cons = new JsonArray();
        foreach (var c in new FilteredElementCollector(fd).OfClass(typeof(ConnectorElement)).Cast<ConnectorElement>())
            cons.Add(new JsonObject { ["id"] = c.Id.Value, ["domain"] = c.Domain.ToString(), ["origin"] = Dto.JsonArr(c.Origin) });
        if (cons.Count > 0) o["connectors"] = cons;
        return o;
    }

    static string SafeSpec(FamilyParameter p) { try { return p.Definition.GetDataType().TypeId; } catch { return null; } }

    // =============================================================== parameters & types
    [Tool("family_add_parameter", """
        Adds a family parameter. Dimension parameters (data_type=length) are THE way to make a family parametric: drive geometry with them via family_add_dimension / family_add_extrusion(end_parameter=...).
        @name Parameter name. @data_type length | angle | area | volume | number | integer | text | yesno | material | url | currency | mass | force | electrical_power | voltage | current | airflow | flow | pressure | temperature
        @instance true = instance parameter (varies per placed element), false = type parameter.
        @group Palette group: dimensions (geometry), constraints, identity_data, materials, visibility, data, general, mechanical, electrical, plumbing...
        @value Default value (mm for length). @formula Optional formula e.g. 'Width / 2' or 'if(Height > 1000 mm, 1, 0)' (overrides value).
        """, Category = "Family")]
    public static object FamilyAddParameter(string name, string dataType = "length", bool instance = false, string group = "dimensions", JsonNode value = null, string formula = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        FamUtil.EnsureType(fm);
        var existing = fm.get_Parameter(name);
        FamilyParameter p = existing ?? fm.AddParameter(name, FamUtil.Group(group), FamUtil.Spec(dataType), instance);
        if (value != null) SetValue(fm, p, value);
        if (!string.IsNullOrWhiteSpace(formula)) fm.SetFormula(p, formula);
        return new JsonObject { ["name"] = p.Definition.Name, ["kind"] = p.IsInstance ? "instance" : "type", ["existing"] = existing != null };
    }

    static void SetValue(FamilyManager fm, FamilyParameter p, JsonNode v)
    {
        var spec = p.Definition.GetDataType();
        switch (p.StorageType)
        {
            case StorageType.Double:
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s)) fm.SetValueString(p, s);
                else fm.Set(p, U.FromApi(v.GetValue<double>(), spec));
                break;
            case StorageType.Integer:
                if (v is JsonValue jb && jb.TryGetValue<bool>(out var b)) fm.Set(p, b ? 1 : 0); else fm.Set(p, v.GetValue<int>());
                break;
            case StorageType.String: fm.Set(p, v.ToString().Trim('"')); break;
            case StorageType.ElementId:
                if (v is JsonValue nv && nv.TryGetValue<long>(out var id)) fm.Set(p, new ElementId(id));
                else
                {
                    var m = new FilteredElementCollector(Ctx.Doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name.Equals(v.ToString().Trim('"'), StringComparison.OrdinalIgnoreCase))
                            ?? throw new ToolException($"Material '{v}' not found in the family. Use family_add_material first.");
                    fm.Set(p, m.Id);
                }
                break;
        }
    }

    [Tool("family_set_parameter_value", "Sets a family parameter value for a type (or the current type). Lengths in mm.", Category = "Family")]
    public static object FamilySetParameterValue(string parameter, JsonNode value, string typeName = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        FamUtil.EnsureType(fm);
        if (!string.IsNullOrWhiteSpace(typeName))
            fm.CurrentType = fm.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException($"Type '{typeName}' not found.");
        SetValue(fm, FamUtil.Param(fd, parameter), value);
        return new JsonObject { ["ok"] = true, ["type"] = fm.CurrentType?.Name };
    }

    [Tool("family_set_formula", "Sets (or clears with empty string) the formula of a family parameter, e.g. 'Width / 2 - Leg_Size'.", Category = "Family")]
    public static object FamilySetFormula(string parameter, string formula)
    {
        var fd = Ctx.RequireFamilyDoc();
        fd.FamilyManager.SetFormula(FamUtil.Param(fd, parameter), string.IsNullOrWhiteSpace(formula) ? null : formula);
        return new JsonObject { ["parameter"] = parameter, ["formula"] = formula };
    }

    [Tool("family_delete_parameter", "Removes a family parameter.", Category = "Family", Destructive = true)]
    public static object FamilyDeleteParameter(string parameter)
    {
        var fd = Ctx.RequireFamilyDoc();
        fd.FamilyManager.RemoveParameter(FamUtil.Param(fd, parameter));
        return new JsonObject { ["removed"] = parameter };
    }

    [Tool("family_add_type", """
        Creates a family type (e.g. '900 x 2100') and optionally sets parameter values on it in one go (lengths mm).
        @name Type name. @values {parameter: value}. @make_current Make it the current type.
        """, Category = "Family")]
    public static object FamilyAddType(string name, Dictionary<string, JsonNode> values = null, bool makeCurrent = true)
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        var existing = fm.Types.Cast<FamilyType>().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var t = existing ?? fm.NewType(name);
        fm.CurrentType = t;
        if (values != null) foreach (var kv in values) SetValue(fm, FamUtil.Param(fd, kv.Key), kv.Value);
        return new JsonObject { ["type"] = t.Name, ["total_types"] = fm.Types.Size };
    }

    [Tool("family_delete_type", "Deletes a family type.", Category = "Family", Destructive = true)]
    public static object FamilyDeleteType(string name)
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        var t = fm.Types.Cast<FamilyType>().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException("Type not found.");
        fm.CurrentType = t;
        fm.DeleteCurrentType();
        return new JsonObject { ["deleted"] = name };
    }

    [Tool("family_add_material", "Creates a material inside the family document (so geometry can use it) or reuses it if present. Prefer a 'material' family parameter so users can change it in the project.", Category = "Family")]
    public static object FamilyAddMaterial(string name, MaterialTools.RgbColor color = null, int transparency = 0)
    {
        var fd = Ctx.RequireFamilyDoc();
        var m = new FilteredElementCollector(fd).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (m == null) m = (Material)fd.GetElement(Material.Create(fd, name));
        if (color != null) m.Color = new Color(color.R, color.G, color.B);
        m.Transparency = transparency;
        return new JsonObject { ["id"] = m.Id.Value, ["name"] = m.Name };
    }

    // =============================================================== reference planes & dimensions
    static int RefCode(string name, string reference)
    {
        string r = (reference ?? "auto").Trim().ToLowerInvariant().Replace(' ', '_');
        if (r == "auto")
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            return n switch { "left" => 0, "right" => 2, "front" => 3, "back" => 5, "bottom" => 6, "top" => 8, _ => 12 };
        }
        return r switch
        {
            "left" => 0, "center_lr" or "center_left_right" => 1, "right" => 2, "front" => 3, "center_fb" or "center_front_back" => 4, "back" => 5,
            "bottom" => 6, "center_elevation" or "center_el" => 7, "top" => 8, "strong" or "strong_reference" => 12, "weak" or "weak_reference" => 13,
            "none" or "not_a_reference" => 14,
            _ => throw new ToolException("reference: auto, strong, weak, none, left, center_lr, right, front, center_fb, back, bottom, center_elevation, top"),
        };
    }

    [Tool("family_add_reference_plane", """
        Adds a named reference plane — the skeleton that makes a family parametric. Axes: 'x' = plane at constant X (a vertical plane seen as a vertical line in plan: Left/Right), 'y' = constant Y (Front/Back), 'z' = constant Z (horizontal plane: Top/Bottom, seen in elevations).
        @name Plane name (e.g. 'Left', 'Right', 'Top', 'Leg1_Inner'). @axis x | y | z. @offset_mm Position along that axis from the origin.
        @extent_mm Half-length of the drawn line (visual only, default 2000).
        @reference 'Is Reference' setting: auto (named Left/Right/Front/Back/Top/Bottom get that role — needed so the family can be aligned in projects; others are strong references), strong, weak, none, left, center_lr, right, front, center_fb, back, bottom, center_elevation, top.
        """, Category = "Family")]
    public static object FamilyAddReferencePlane(string name, string axis, double offsetMm, double extentMm = 2000, string reference = "auto")
    {
        var fd = Ctx.RequireFamilyDoc();
        var existing = FamUtil.RefPlane(fd, name, true);
        if (existing != null) { MoveRefPlane(fd, existing, axis, offsetMm); return new JsonObject { ["name"] = name, ["moved"] = true }; }
        double L = U.Mm(extentMm), o = U.Mm(offsetMm);
        ReferencePlane rp;
        switch (axis.ToLowerInvariant())
        {
            case "x": rp = fd.FamilyCreate.NewReferencePlane(new XYZ(o, -L, 0), new XYZ(o, L, 0), XYZ.BasisZ, FamUtil.PlanView(fd)); break;
            case "y": rp = fd.FamilyCreate.NewReferencePlane(new XYZ(-L, o, 0), new XYZ(L, o, 0), XYZ.BasisZ, FamUtil.PlanView(fd)); break;
            case "z":
                {
                    var v = FamUtil.Elevation(fd, "Front", "Back", "Left", "Right") ?? throw new ToolException("No elevation view in this family.");
                    rp = fd.FamilyCreate.NewReferencePlane(new XYZ(-L, 0, o), new XYZ(L, 0, o), XYZ.BasisY, v);
                    break;
                }
            default: throw new ToolException("axis must be x, y or z");
        }
        rp.Name = name;
        try { rp.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.Set(RefCode(name, reference)); } catch { }
        return new JsonObject { ["name"] = rp.Name, ["id"] = rp.Id.Value };
    }

    static void MoveRefPlane(Document fd, ReferencePlane rp, string axis, double offsetMm)
    {
        var pl = rp.GetPlane();
        var n = pl.Normal;
        double cur = pl.Origin.DotProduct(n);
        // move along its normal so that its coordinate along the requested axis equals offset
        var ax = axis.ToLowerInvariant() == "x" ? XYZ.BasisX : axis.ToLowerInvariant() == "y" ? XYZ.BasisY : XYZ.BasisZ;
        double curAxis = pl.Origin.DotProduct(ax);
        var delta = ax * (U.Mm(offsetMm) - curAxis);
        if (delta.GetLength() > 1e-9) ElementTransformUtils.MoveElement(fd, rp.Id, delta);
    }

    [Tool("family_add_dimension", """
        Creates a dimension between two reference planes and (optionally) labels it with a family parameter — the label makes the planes move when the parameter changes. Use equal=true with a third plane to create an EQ-constrained symmetric dimension (e.g. Left–Center–Right).
        @plane_a / @plane_b Reference plane names. @plane_c Optional third plane (centre) for EQ dimensions.
        @label Family parameter name to drive the dimension (created as a length type parameter if missing).
        @equal Make the segments equal (needs plane_c).
        @offset_mm Distance of the dimension line from the geometry (default 600).
        """, Category = "Family")]
    public static object FamilyAddDimension(string planeA, string planeB, string label = null, string planeC = null, bool equal = false, double offsetMm = 600)
    {
        var fd = Ctx.RequireFamilyDoc();
        var a = FamUtil.RefPlane(fd, planeA); var b = FamUtil.RefPlane(fd, planeB);
        var c = planeC != null ? FamUtil.RefPlane(fd, planeC) : null;
        var planes = new List<ReferencePlane> { a };
        if (c != null) planes.Add(c);
        planes.Add(b);
        planes = planes.OrderBy(p => p.GetPlane().Origin.DotProduct(AxisOf(p))).ToList();
        var n = planes[0].GetPlane().Normal;
        var axis = AxisOf(planes[0]);
        View view; XYZ lineDir; XYZ basePt;
        if (Math.Abs(axis.Z) > 0.9) { view = FamUtil.Elevation(fd, "Front", "Back") ?? throw new ToolException("No elevation view"); lineDir = XYZ.BasisZ; basePt = new XYZ(U.Mm(offsetMm), 0, 0); }
        else { view = FamUtil.PlanView(fd); lineDir = axis; basePt = axis.IsAlmostEqualTo(XYZ.BasisX) ? new XYZ(0, U.Mm(offsetMm), 0) : new XYZ(U.Mm(offsetMm), 0, 0); }
        var refs = new ReferenceArray();
        foreach (var p in planes) refs.Append(p.GetReference());
        double p0 = planes[0].GetPlane().Origin.DotProduct(axis), p1 = planes[^1].GetPlane().Origin.DotProduct(axis);
        var start = basePt + axis * p0; var end = basePt + axis * p1;
        var dim = fd.FamilyCreate.NewLinearDimension(view, Line.CreateBound(start, end), refs);
        var res = new JsonObject { ["dimension_id"] = dim.Id.Value, ["value_mm"] = U.R(U.ToMm(dim.Value ?? 0), 2) };
        if (!string.IsNullOrWhiteSpace(label))
        {
            var fm = fd.FamilyManager;
            FamUtil.EnsureType(fm);
            var p = FamUtil.Param(fd, label, true) ?? fm.AddParameter(label, GroupTypeId.Geometry, SpecTypeId.Length, false);
            dim.FamilyLabel = p;
            res["label"] = label;
        }
        if (equal && planes.Count == 3) { dim.AreSegmentsEqual = true; res["equal"] = true; }
        return res;
    }

    static XYZ AxisOf(ReferencePlane rp)
    {
        var n = rp.GetPlane().Normal;
        double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        return ax >= ay && ax >= az ? XYZ.BasisX : ay >= az ? XYZ.BasisY : XYZ.BasisZ;
    }

    [Tool("family_setup_dimensions", """
        One-call parametric skeleton for a box-like family: creates type parameters Width/Depth/Height (names configurable), reference planes Left/Right/Front/Back/Top at the default sizes, symmetric EQ dimensions about the centre planes, and labelled overall dimensions. Afterwards build geometry with family_add_extrusion(lock_to_planes=true, end_plane='Top') and it will follow the parameters.
        @width_mm @depth_mm @height_mm Default sizes. @width_param @depth_param @height_param Parameter names.
        """, Category = "Family")]
    public static object FamilySetupDimensions(double widthMm = 1000, double depthMm = 600, double heightMm = 800, string widthParam = "Width", string depthParam = "Depth", string heightParam = "Height")
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        FamUtil.EnsureType(fm);
        foreach (var (n, v) in new[] { (widthParam, widthMm), (depthParam, depthMm), (heightParam, heightMm) })
        {
            var p = fm.get_Parameter(n) ?? fm.AddParameter(n, GroupTypeId.Geometry, SpecTypeId.Length, false);
            fm.Set(p, U.Mm(v));
        }
        var notes = new JsonArray();
        void Try(string what, Action a) { try { a(); } catch (Exception ex) { notes.Add(what + ": " + ex.Message); } }
        FamilyAddReferencePlane("Left", "x", -widthMm / 2); FamilyAddReferencePlane("Right", "x", widthMm / 2);
        FamilyAddReferencePlane("Front", "y", -depthMm / 2); FamilyAddReferencePlane("Back", "y", depthMm / 2);
        FamilyAddReferencePlane("Top", "z", heightMm);
        fd.Regenerate();
        var centerLR = FindCenter(fd, XYZ.BasisX); var centerFB = FindCenter(fd, XYZ.BasisY);
        Try("width EQ", () => FamilyAddDimension("Left", "Right", null, centerLR, true, 800));
        Try("width label", () => FamilyAddDimension("Left", "Right", widthParam, null, false, 1200));
        Try("depth EQ", () => FamilyAddDimension("Front", "Back", null, centerFB, true, 800));
        Try("depth label", () => FamilyAddDimension("Front", "Back", depthParam, null, false, 1200));
        Try("height", () => { var lvl = FindLevelPlane(fd); FamilyAddDimension(lvl, "Top", heightParam, null, false, 800); });
        return new JsonObject { ["parameters"] = new JsonArray(widthParam, depthParam, heightParam), ["planes"] = new JsonArray("Left", "Right", "Front", "Back", "Top"), ["notes"] = notes };
    }

    static string FindCenter(Document fd, XYZ axis)
    {
        var best = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
            .Where(r => AxisOf(r).IsAlmostEqualTo(axis)).OrderBy(r => Math.Abs(r.GetPlane().Origin.DotProduct(axis))).FirstOrDefault();
        return best?.Name;
    }

    static string FindLevelPlane(Document fd)
    {
        var best = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
            .Where(r => AxisOf(r).IsAlmostEqualTo(XYZ.BasisZ) && r.Name != "Top").OrderBy(r => Math.Abs(r.GetPlane().Origin.Z)).FirstOrDefault();
        if (best == null) throw new ToolException("No horizontal reference plane (Ref. Level) found for the height dimension.");
        return best.Name;
    }

    // =============================================================== geometry
    static ElementId GetOrCreateMaterial(Document fd, string name)
    {
        var m = new FilteredElementCollector(fd).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return m != null ? m.Id : Material.Create(fd, name);
    }

    public static void ApplyFormOptionsPublic(Document fd, GenericForm form, string name, string material, string materialParameter, string subcategory, string[] detailLevels)
        => ApplyFormOptions(fd, form, name, material, materialParameter, subcategory, detailLevels);

    static void ApplyFormOptions(Document fd, GenericForm form, string name, string material, string materialParameter, string subcategory, string[] detailLevels)
    {
        var fm = fd.FamilyManager;
        if (!string.IsNullOrWhiteSpace(name)) { try { form.Name = name; } catch { } }
        if (!string.IsNullOrWhiteSpace(materialParameter))
        {
            var fp = FamUtil.Param(fd, materialParameter, true) ?? fm.AddParameter(materialParameter, GroupTypeId.Materials, SpecTypeId.Reference.Material, false);
            FamUtil.EnsureMaterialValue(fd, fp);
            var mp = form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (mp != null) fm.AssociateElementParameterToFamilyParameter(mp, fp);
        }
        else if (!string.IsNullOrWhiteSpace(material))
            form.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(GetOrCreateMaterial(fd, material));
        if (!string.IsNullOrWhiteSpace(subcategory) && fd.OwnerFamily.FamilyCategory != null)
        {
            var parent = fd.OwnerFamily.FamilyCategory;
            var sub = parent.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name.Equals(subcategory, StringComparison.OrdinalIgnoreCase)) ?? fd.Settings.Categories.NewSubcategory(parent, subcategory);
            form.Subcategory = sub;
        }
        if (detailLevels != null && detailLevels.Length > 0)
        {
            var vis = form.GetVisibility();
            vis.IsShownInCoarse = detailLevels.Contains("coarse", StringComparer.OrdinalIgnoreCase);
            vis.IsShownInMedium = detailLevels.Contains("medium", StringComparer.OrdinalIgnoreCase);
            vis.IsShownInFine = detailLevels.Contains("fine", StringComparer.OrdinalIgnoreCase);
            form.SetVisibility(vis);
        }
    }

    [Tool("family_add_extrusion", """
        Adds an extrusion (solid or void) from a 2D profile drawn on an axis-aligned plane. THE basic modelling tool for families: boxes, panels, legs, frames, profiles...
        Planes: 'xy' = plan (profile points [x,y], extrudes UP along +Z), 'xz' = front elevation (profile [x,z], extrudes toward +Y), 'yz' = side elevation (profile [y,z], extrudes toward +X). The plane sits at offset_mm along its normal; the solid spans start_mm..end_mm along the normal from that plane. All mm, origin = family origin.
        Profile: polygon (list of [u,v] points) OR circle (circle_center + circle_radius_mm).
        PARAMETRIC: with lock_to_planes=true every axis-aligned profile edge lying exactly on a reference plane is aligned+locked to it, and end_plane/start_plane lock the top/bottom faces to a horizontal plane (e.g. 'Top') — so it follows the dimensions. Or link depth to a parameter via end_parameter/start_parameter.
        @solid true = solid form, false = void (cuts other solids / the host).
        @material Material name created in the family; @material_parameter family 'material' parameter to bind (recommended).
        @subcategory Subcategory name (e.g. 'Frame'); @detail_levels e.g. ['medium','fine'].
        """, Category = "Family")]
    public static object FamilyAddExtrusion(string plane = "xy", double offsetMm = 0, double[][] profile = null, double[] circleCenter = null, double? circleRadiusMm = null,
        double startMm = 0, double endMm = 100, bool solid = true, string name = null, string material = null, string materialParameter = null, string subcategory = null,
        string startParameter = null, string endParameter = null, bool lockToPlanes = true, string startPlane = null, string endPlane = null, string[] detailLevels = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        plane = plane.ToLowerInvariant();
        var curves = new CurveArray();
        var pts3 = new List<XYZ>();
        if (circleRadiusMm.HasValue)
        {
            var c = circleCenter ?? new[] { 0.0, 0.0 };
            var center = FamUtil.Point3(plane, c[0], c[1], offsetMm);
            XYZ xa, ya;
            switch (plane) { case "xy": xa = XYZ.BasisX; ya = XYZ.BasisY; break; case "xz": xa = XYZ.BasisX; ya = XYZ.BasisZ; break; default: xa = XYZ.BasisY; ya = XYZ.BasisZ; break; }
            curves.Append(Arc.Create(center, U.Mm(circleRadiusMm.Value), 0, 2 * Math.PI, xa, ya));
        }
        else
        {
            if (profile == null || profile.Length < 3) throw new ToolException("Give a profile polygon (3+ points [u,v]) or circle_center + circle_radius_mm.");
            pts3 = Geo.Clean(profile.Select(p => FamUtil.Point3(plane, p[0], p[1], offsetMm)).ToList(), true);
            for (int i = 0; i < pts3.Count; i++) curves.Append(Line.CreateBound(pts3[i], pts3[(i + 1) % pts3.Count]));
        }
        var arr = new CurveArrArray(); arr.Append(curves);
        var sp = FamUtil.Sketch(fd, plane, offsetMm);
        var ext = fd.FamilyCreate.NewExtrusion(solid, arr, sp, U.Mm(endMm));
        if (startMm != 0) ext.StartOffset = U.Mm(startMm);
        var fm = fd.FamilyManager;
        var notes = new JsonArray();
        if (!string.IsNullOrWhiteSpace(endParameter)) { var fp = FamUtil.Param(fd, endParameter); fm.AssociateElementParameterToFamilyParameter(ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), fp); }
        if (!string.IsNullOrWhiteSpace(startParameter)) { var fp = FamUtil.Param(fd, startParameter); fm.AssociateElementParameterToFamilyParameter(ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), fp); }
        ApplyFormOptions(fd, ext, name, material, materialParameter, subcategory, detailLevels);
        fd.Regenerate();

        int locks = 0;
        if (lockToPlanes && pts3.Count > 0) locks += LockProfile(fd, ext, plane, notes);
        if (!string.IsNullOrWhiteSpace(endPlane)) locks += LockFace(fd, ext, plane, endPlane, true, notes);
        if (!string.IsNullOrWhiteSpace(startPlane)) locks += LockFace(fd, ext, plane, startPlane, false, notes);
        var res = new JsonObject { ["id"] = ext.Id.Value, ["kind"] = solid ? "solid extrusion" : "void extrusion", ["locks_created"] = locks };
        if (notes.Count > 0) res["notes"] = notes;
        return res;
    }

    static View ViewForPlane(Document fd, string plane) => plane switch
    {
        "xy" => FamUtil.PlanView(fd),
        "xz" => FamUtil.Elevation(fd, "Front", "Back"),
        _ => FamUtil.Elevation(fd, "Right", "Left"),
    };

    static int LockProfile(Document fd, Extrusion ext, string plane, JsonArray notes)
    {
        int n = 0;
        var view = ViewForPlane(fd, plane);
        if (view == null) { notes.Add("no view to lock profile in"); return 0; }
        var rps = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
        var sketch = ext.Sketch;
        foreach (var id in sketch.GetAllElements())
        {
            if (fd.GetElement(id) is not ModelCurve mc || mc.GeometryCurve is not Line ln) continue;
            var d = ln.Direction;
            var mid = (ln.GetEndPoint(0) + ln.GetEndPoint(1)) / 2;
            ReferencePlane match = null;
            foreach (var rp in rps)
            {
                var pl = rp.GetPlane();
                if (Math.Abs(pl.Normal.DotProduct(d)) > 1e-6) continue; // plane must be parallel to the line
                if (Math.Abs(pl.Normal.DotProduct(mid - pl.Origin)) > 1e-4) continue;
                // the plane must be perpendicular to the sketch plane
                if (Math.Abs(pl.Normal.DotProduct(FamUtil.Normal(plane))) > 1e-6) continue;
                if (rp.Name.StartsWith("Center", StringComparison.OrdinalIgnoreCase) && match != null) continue;
                match = rp; if (!rp.Name.StartsWith("Center", StringComparison.OrdinalIgnoreCase)) break;
            }
            if (match == null) continue;
            try { fd.FamilyCreate.NewAlignment(view, match.GetReference(), mc.GeometryCurve.Reference); n++; }
            catch (Exception ex) { notes.Add($"align to '{match.Name}': {ex.Message}"); }
        }
        return n;
    }

    static int LockFace(Document fd, Extrusion ext, string plane, string planeName, bool end, JsonArray notes)
    {
        try
        {
            var rp = FamUtil.RefPlane(fd, planeName);
            var view = plane == "xy" ? FamUtil.Elevation(fd, "Front", "Back", "Right", "Left") : ViewForPlane(fd, plane == "xz" ? "yz" : "xz");
            if (view == null) return 0;
            var nrm = FamUtil.Normal(plane);
            var bb = ext.get_BoundingBox(null);
            var target = end ? (bb.Max.X * nrm.X + bb.Max.Y * nrm.Y + bb.Max.Z * nrm.Z) : (bb.Min.X * nrm.X + bb.Min.Y * nrm.Y + bb.Min.Z * nrm.Z);
            var pt = (bb.Min + bb.Max) / 2;
            pt = pt - nrm * pt.DotProduct(nrm) + nrm * target;
            var face = Geo.NearestPlanarFace(ext, pt, end ? nrm : -nrm);
            if (face == null) { notes.Add("could not find the extrusion face to lock"); return 0; }
            fd.FamilyCreate.NewAlignment(view, rp.GetReference(), face.Reference);
            return 1;
        }
        catch (Exception ex) { notes.Add($"lock to '{planeName}': {ex.Message}"); return 0; }
    }

    [Tool("family_add_box", """
        Adds a rectangular block (solid or void) — convenience over family_add_extrusion. Box is centred at center [x,y] mm, size width (X) x depth (Y), from base_z_mm up to base_z_mm+height_mm. For parametric blocks use family_setup_dimensions first and pass lock_to_planes + end_plane.
        """, Category = "Family")]
    public static object FamilyAddBox(double widthMm, double depthMm, double heightMm, double[] center = null, double baseZMm = 0, bool solid = true, string name = null,
        string material = null, string materialParameter = null, string subcategory = null, bool lockToPlanes = true, string endPlane = null, string endParameter = null, string[] detailLevels = null)
    {
        double cx = center?[0] ?? 0, cy = center?[1] ?? 0;
        var prof = new[] { new[] { cx - widthMm / 2, cy - depthMm / 2 }, new[] { cx + widthMm / 2, cy - depthMm / 2 }, new[] { cx + widthMm / 2, cy + depthMm / 2 }, new[] { cx - widthMm / 2, cy + depthMm / 2 } };
        return FamilyAddExtrusion("xy", baseZMm, prof, null, null, 0, heightMm, solid, name, material, materialParameter, subcategory, null, endParameter, lockToPlanes, null, endPlane, detailLevels);
    }

    [Tool("family_add_cylinder", "Adds a vertical cylinder (solid or void) centred at [x,y] mm with radius and height, from base_z_mm. For round holes in a block use solid=false.", Category = "Family")]
    public static object FamilyAddCylinder(double radiusMm, double heightMm, double[] center = null, double baseZMm = 0, bool solid = true, string name = null, string material = null,
        string materialParameter = null, string subcategory = null, string endParameter = null, string[] detailLevels = null)
        => FamilyAddExtrusion("xy", baseZMm, null, center ?? new[] { 0.0, 0.0 }, radiusMm, 0, heightMm, solid, name, material, materialParameter, subcategory, null, endParameter, false, null, null, detailLevels);

    [Tool("family_add_revolve", """
        Adds a revolved form (vases, round columns with profile, domes, pipe fittings). The profile is a closed polygon [[r,z],...] in the XZ plane (r = distance from the vertical axis through [axis_x,axis_y], z = height), revolved around the vertical axis by angle_deg (default 360).
        """, Category = "Family")]
    public static object FamilyAddRevolve(double[][] profileRz, double angleDeg = 360, double[] axisXy = null, bool solid = true, string name = null, string material = null,
        string materialParameter = null, string subcategory = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        double ax = axisXy?[0] ?? 0, ay = axisXy?[1] ?? 0;
        // profile in the XZ plane at y = ay, with the axis along Z at x = ax
        var pts = Geo.Clean(profileRz.Select(p => new XYZ(U.Mm(ax + p[0]), U.Mm(ay), U.Mm(p[1]))).ToList(), true);
        var ca = new CurveArray();
        for (int i = 0; i < pts.Count; i++) ca.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
        var arr = new CurveArrArray(); arr.Append(ca);
        var sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(XYZ.BasisY, new XYZ(0, U.Mm(ay), 0)));
        var axis = Line.CreateBound(new XYZ(U.Mm(ax), U.Mm(ay), 0), new XYZ(U.Mm(ax), U.Mm(ay), 1));
        var rev = fd.FamilyCreate.NewRevolution(solid, arr, sp, axis, 0, U.Deg(angleDeg));
        ApplyFormOptions(fd, rev, name, material, materialParameter, subcategory, null);
        return new JsonObject { ["id"] = rev.Id.Value, ["kind"] = solid ? "solid revolve" : "void revolve" };
    }

    [Tool("family_add_blend", """
        Adds a blend (loft between a bottom and a top profile, e.g. tapered legs, funnels, transitions). Both profiles are polygons [[x,y],...] (mm) with the same number of points, placed at z = base_z_mm and z = base_z_mm + height_mm.
        """, Category = "Family")]
    public static object FamilyAddBlend(double[][] bottomProfile, double[][] topProfile, double heightMm, double baseZMm = 0, bool solid = true, string name = null,
        string material = null, string materialParameter = null, string subcategory = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        CurveArray Poly(double[][] p, double z)
        {
            var pts = Geo.Clean(p.Select(q => new XYZ(U.Mm(q[0]), U.Mm(q[1]), U.Mm(z))).ToList(), true);
            var ca = new CurveArray();
            for (int i = 0; i < pts.Count; i++) ca.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
            return ca;
        }
        var sp = FamUtil.Sketch(fd, "xy", baseZMm);
        var blend = fd.FamilyCreate.NewBlend(solid, Poly(topProfile, baseZMm + heightMm), Poly(bottomProfile, baseZMm), sp);
        ApplyFormOptions(fd, blend, name, material, materialParameter, subcategory, null);
        return new JsonObject { ["id"] = blend.Id.Value, ["kind"] = solid ? "solid blend" : "void blend" };
    }

    [Tool("family_add_sweep", """
        Adds a sweep: a closed 2D profile swept along a 3D polyline path (rails, frames, pipes, mouldings, handrails). The path is a polyline of 3D points [[x,y,z],...] in mm (all points must lie in ONE plane); the profile polygon [[u,v],...] mm is placed at the start of the path perpendicular to the first segment: v = the path-plane normal (UP for a path in a horizontal plane; for a straight vertical path it is +X), u = to the right when looking along the first segment with v up (u = t x v).
        """, Category = "Family")]
    public static object FamilyAddSweep(double[][] path, double[][] profile, bool solid = true, string name = null, string material = null, string materialParameter = null, string subcategory = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        var pp = Geo.Clean(path.Select(p => U.Pt(p)).ToList(), false);
        if (pp.Count < 2) throw new ToolException("Path needs at least 2 points.");
        var pathCurves = new CurveArray();
        for (int i = 0; i + 1 < pp.Count; i++) pathCurves.Append(Line.CreateBound(pp[i], pp[i + 1]));
        // plane of the path
        var t = (pp[1] - pp[0]).Normalize();
        XYZ n = pp.Count >= 3 ? (pp[1] - pp[0]).CrossProduct(pp[2] - pp[1]) : XYZ.Zero;
        if (n.GetLength() < 1e-9) n = Math.Abs(t.Z) > 0.99 ? XYZ.BasisX : XYZ.BasisZ; // straight / collinear path: a horizontal path gets a horizontal sketch plane
        n = n.Normalize();
        // deterministic orientation: the normal points up (or towards +X / +Y for a vertical plane)
        if (n.Z < -1e-9 || (Math.Abs(n.Z) <= 1e-9 && (n.X < -1e-9 || (Math.Abs(n.X) <= 1e-9 && n.Y < 0)))) n = -n;
        var sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(n, pp[0]));
        // Revit expects the profile drawn in the XY plane at the family origin (local coordinates); it maps local y to the sketch-plane
        // normal n and local x to n x t (verified live), then places it at the path start. u (right = t x n) is therefore -x.
        var ca = new CurveArray();
        var prof = profile.Select(p => new XYZ(-U.Mm(p[0]), U.Mm(p[1]), 0)).ToList();
        for (int i = 0; i < prof.Count; i++) ca.Append(Line.CreateBound(prof[i], prof[(i + 1) % prof.Count]));
        var arr = new CurveArrArray(); arr.Append(ca);
        var sweepProfile = Ctx.App.Create.NewCurveLoopsProfile(arr);
        var sweep = fd.FamilyCreate.NewSweep(solid, pathCurves, sp, sweepProfile, 0, ProfilePlaneLocation.Start);
        ApplyFormOptions(fd, sweep, name, material, materialParameter, subcategory, null);
        return new JsonObject { ["id"] = sweep.Id.Value, ["kind"] = solid ? "solid sweep" : "void sweep" };
    }

    [Tool("family_add_symbolic_lines", "Adds 2D symbolic lines (visible in the plan/elevation view only, not in 3D) — typical for plan symbols, door swings, equipment outlines. Points [[x,y],...] mm in the given view.", Category = "Family")]
    public static object FamilyAddSymbolicLines(double[][] points, bool closed = false, string view = "plan")
    {
        var fd = Ctx.RequireFamilyDoc();
        View v = view.Equals("plan", StringComparison.OrdinalIgnoreCase) ? FamUtil.PlanView(fd) : FamUtil.Elevation(fd, view) ?? throw new ToolException("view not found (use 'plan', 'Front', 'Back', 'Left', 'Right')");
        var sp = v.SketchPlane ?? throw new ToolException("This view has no sketch plane.");
        var pl = sp.GetPlane();
        XYZ P(double[] p) => pl.Origin + pl.XVec * U.Mm(p[0]) + pl.YVec * U.Mm(p[1]);
        var pts = Geo.Clean(points.Select(P).ToList(), closed);
        int n = 0;
        for (int i = 0; i + 1 < pts.Count; i++) { fd.FamilyCreate.NewSymbolicCurve(Line.CreateBound(pts[i], pts[i + 1]), sp); n++; }
        if (closed && pts.Count > 2) { fd.FamilyCreate.NewSymbolicCurve(Line.CreateBound(pts[^1], pts[0]), sp); n++; }
        return new JsonObject { ["lines"] = n };
    }

    [Tool("family_set_form_properties", """
        Edits an existing form: visibility per detail level/view, solid<->void, subcategory, material, name. Visibility flags apply to model elements: coarse/medium/fine detail levels and the view types (plan_rcp, front_back, left_right).
        """, Category = "Family")]
    public static object FamilySetFormProperties(long id, bool? coarse = null, bool? medium = null, bool? fine = null, bool? planRcp = null, bool? frontBack = null, bool? leftRight = null,
        bool? solid = null, string subcategory = null, string material = null, string materialParameter = null, string name = null, string visibleParameter = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        var f = Find.Element<GenericForm>(fd, id);
        var vis = f.GetVisibility();
        if (coarse.HasValue) vis.IsShownInCoarse = coarse.Value;
        if (medium.HasValue) vis.IsShownInMedium = medium.Value;
        if (fine.HasValue) vis.IsShownInFine = fine.Value;
        if (planRcp.HasValue) vis.IsShownInPlanRCPCut = planRcp.Value;
        if (frontBack.HasValue) vis.IsShownInFrontBack = frontBack.Value;
        if (leftRight.HasValue) vis.IsShownInLeftRight = leftRight.Value;
        f.SetVisibility(vis);
        if (solid.HasValue) f.get_Parameter(BuiltInParameter.ELEMENT_IS_CUTTING)?.Set(solid.Value ? 0 : 1);
        ApplyFormOptions(fd, f, name, material, materialParameter, subcategory, null);
        if (!string.IsNullOrWhiteSpace(visibleParameter))
        {
            var fp = FamUtil.Param(fd, visibleParameter, true) ?? fd.FamilyManager.AddParameter(visibleParameter, GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, false);
            var p = f.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
            if (p != null) fd.FamilyManager.AssociateElementParameterToFamilyParameter(p, fp);
        }
        return new JsonObject { ["id"] = id, ["ok"] = true };
    }

    [Tool("family_associate_parameter", "Binds an element's parameter to a family parameter (e.g. an extrusion's 'Extrusion End' to 'Height', a form's 'Material' to a material parameter, 'Visible' to a yes/no parameter). Element parameter: name or BuiltInParameter.", Category = "Family")]
    public static object FamilyAssociateParameter(long elementId, string elementParameter, string familyParameter)
    {
        var fd = Ctx.RequireFamilyDoc();
        var e = Find.Element(fd, elementId);
        var p = Par.Get(e, elementParameter) ?? throw new ToolException($"Parameter '{elementParameter}' not found on element {elementId}.");
        fd.FamilyManager.AssociateElementParameterToFamilyParameter(p, FamUtil.Param(fd, familyParameter));
        return new JsonObject { ["associated"] = true };
    }

    [Tool("family_set_properties", """
        Family-level settings. @category change the family category. @always_vertical, @work_plane_based, @shared, @cut_with_voids (door/window opening cut), @room_calculation_point. @part_type e.g. for MEP fittings.
        """, Category = "Family")]
    public static object FamilySetProperties(string category = null, bool? alwaysVertical = null, bool? workPlaneBased = null, bool? shared = null, bool? roomCalculationPoint = null, string partType = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        var f = fd.OwnerFamily;
        if (!string.IsNullOrWhiteSpace(category)) f.FamilyCategory = Find.Category(fd, category);
        void SetBool(BuiltInParameter bip, bool? v) { if (v.HasValue) f.get_Parameter(bip)?.Set(v.Value ? 1 : 0); }
        SetBool(BuiltInParameter.FAMILY_ALWAYS_VERTICAL, alwaysVertical);
        SetBool(BuiltInParameter.FAMILY_WORK_PLANE_BASED, workPlaneBased);
        SetBool(BuiltInParameter.FAMILY_SHARED, shared);
        SetBool(BuiltInParameter.ROOM_CALCULATION_POINT, roomCalculationPoint);
        if (!string.IsNullOrWhiteSpace(partType) && Enum.TryParse<PartType>(partType.Replace("_", ""), true, out var pt)) f.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE)?.Set((int)pt);
        return new JsonObject { ["category"] = f.FamilyCategory?.Name, ["parameters"] = Par.Values(f) };
    }

    // =============================================================== MEP connectors
    [Tool("family_add_connector", """
        Adds an MEP connector on the face of a form, so the family connects to ducts/pipes/conduit/cable trays/circuits. The face is the planar face of form_id nearest to point [x,y,z] mm.
        @kind duct | pipe | electrical | conduit | cable_tray. @form_id The solid form carrying the face (family_get_info lists forms).
        @point Point on/near the face (mm). @system_type duct: supply_air|return_air|exhaust_air|other; pipe: domestic_hot_water|domestic_cold_water|sanitary|hydronic_supply|hydronic_return|fire_protection_wet|other; electrical: power_circuit|data|telephone|...
        @shape round | rectangular (duct). @diameter_mm / @width_mm / @height_mm Connector size (fixed values; use size parameters afterwards to make them parametric).
        @flow_direction in | out | bidirectional.
        """, Category = "Family")]
    public static object FamilyAddConnector(string kind, long formId, double[] point, string systemType = null, string shape = "round", double? diameterMm = null, double? widthMm = null, double? heightMm = null, string flowDirection = "bidirectional")
    {
        var fd = Ctx.RequireFamilyDoc();
        var form = Find.Element(fd, formId);
        var face = Geo.NearestPlanarFace(form, U.Pt(point)) ?? throw new ToolException("No planar face with a reference found on that form.");
        ConnectorElement ce;
        string K(string s) => (s ?? "").Replace("_", "").ToLowerInvariant();
        switch (kind.ToLowerInvariant())
        {
            case "duct":
                {
                    var st = K(systemType) switch { "returnair" => DuctSystemType.ReturnAir, "exhaustair" => DuctSystemType.ExhaustAir, "otherair" or "other" => DuctSystemType.OtherAir, "global" => DuctSystemType.Global, _ => DuctSystemType.SupplyAir };
                    ce = ConnectorElement.CreateDuctConnector(fd, st, shape.Equals("rectangular", StringComparison.OrdinalIgnoreCase) ? ConnectorProfileType.Rectangular : ConnectorProfileType.Round, face.Reference);
                    break;
                }
            case "pipe":
                {
                    var st = K(systemType) switch
                    {
                        "domestichotwater" => PipeSystemType.DomesticHotWater, "sanitary" => PipeSystemType.Sanitary, "hydronicsupply" => PipeSystemType.SupplyHydronic,
                        "hydronicreturn" => PipeSystemType.ReturnHydronic, "firewet" or "fireprotectionwet" => PipeSystemType.FireProtectWet, "otherpipe" or "other" => PipeSystemType.OtherPipe,
                        "vent" => PipeSystemType.Vent, "global" => PipeSystemType.Global, _ => PipeSystemType.DomesticColdWater,
                    };
                    ce = ConnectorElement.CreatePipeConnector(fd, st, face.Reference);
                    break;
                }
            case "electrical":
                {
                    var st = K(systemType) switch
                    {
                        "data" => ElectricalSystemType.Data, "telephone" => ElectricalSystemType.Telephone, "security" => ElectricalSystemType.Security, "firealarm" => ElectricalSystemType.FireAlarm,
                        "nursecall" => ElectricalSystemType.NurseCall, "controls" => ElectricalSystemType.Controls, "communication" => ElectricalSystemType.Communication, _ => ElectricalSystemType.PowerCircuit,
                    };
                    ce = ConnectorElement.CreateElectricalConnector(fd, st, face.Reference);
                    break;
                }
            case "conduit": ce = ConnectorElement.CreateConduitConnector(fd, face.Reference); break;
            case "cable_tray": ce = ConnectorElement.CreateCableTrayConnector(fd, face.Reference); break;
            default: throw new ToolException("kind must be duct, pipe, electrical, conduit or cable_tray");
        }
        if (diameterMm.HasValue) { ce.get_Parameter(BuiltInParameter.CONNECTOR_DIAMETER)?.Set(U.Mm(diameterMm.Value)); ce.get_Parameter(BuiltInParameter.CONNECTOR_RADIUS)?.Set(U.Mm(diameterMm.Value) / 2); }
        if (widthMm.HasValue) ce.get_Parameter(BuiltInParameter.CONNECTOR_WIDTH)?.Set(U.Mm(widthMm.Value));
        if (heightMm.HasValue) ce.get_Parameter(BuiltInParameter.CONNECTOR_HEIGHT)?.Set(U.Mm(heightMm.Value));
        var dir = flowDirection.ToLowerInvariant() switch { "in" => FlowDirectionType.In, "out" => FlowDirectionType.Out, _ => FlowDirectionType.Bidirectional };
        try { ce.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_DIRECTION_PARAM)?.Set((int)dir); } catch { }
        return new JsonObject { ["connector_id"] = ce.Id.Value, ["kind"] = kind, ["domain"] = ce.Domain.ToString() };
    }

    [Tool("family_delete_element", "Deletes an element (form, reference plane, dimension, connector...) from the family by id.", Category = "Family", Destructive = true)]
    public static object FamilyDeleteElement(long[] ids)
    {
        var fd = Ctx.RequireFamilyDoc();
        var d = fd.Delete(Find.Ids(ids));
        return new JsonObject { ["deleted"] = d.Count };
    }

    [Tool("family_flex_test", """
        Quality check: cycles every family type and optionally scales length parameters (min/max factor) to verify the geometry regenerates without errors — the 'flex' test every good family must pass. Returns pass/fail per case.
        @factors Scale factors to apply to the named parameters, e.g. [0.5, 1, 2].
        @parameters Length parameters to flex (default: all type length parameters that are not formula-driven).
        """, Category = "Family")]
    public static object FamilyFlexTest(double[] factors = null, string[] parameters = null)
    {
        var fd = Ctx.RequireFamilyDoc();
        var fm = fd.FamilyManager;
        FamUtil.EnsureType(fm);
        factors ??= new[] { 0.5, 1.0, 1.5 };
        var lenParams = fm.Parameters.Cast<FamilyParameter>().Where(p => p.StorageType == StorageType.Double && !p.IsDeterminedByFormula && !p.IsReadOnly &&
            p.Definition.GetDataType() == SpecTypeId.Length && !p.Definition.Name.StartsWith("Ref_", StringComparison.Ordinal) && p.Definition.Name != "Default Elevation" && (parameters == null || parameters.Contains(p.Definition.Name, StringComparer.OrdinalIgnoreCase))).ToList();
        var results = new JsonArray();
        var original = fm.CurrentType;
        foreach (FamilyType t in fm.Types)
        {
            fm.CurrentType = t;
            var baseVals = lenParams.ToDictionary(p => p, p => t.AsDouble(p) ?? 0);
            foreach (var f in factors)
            {
                string status = "ok", err = null;
                using var sub = new SubTransaction(fd);
                sub.Start();
                try
                {
                    foreach (var p in lenParams) if (baseVals[p] > 0) fm.Set(p, baseVals[p] * f);
                    fd.Regenerate();
                }
                catch (Exception ex) { status = "FAILED"; err = ex.Message; }
                sub.RollBack();
                var o = new JsonObject { ["type"] = t.Name, ["factor"] = f, ["status"] = status };
                if (err != null) o["error"] = err;
                results.Add(o);
            }
        }
        if (original != null) fm.CurrentType = original;
        bool allOk = results.All(r => r["status"].ToString() == "ok");
        return new JsonObject { ["passed"] = allOk, ["flexed_parameters"] = new JsonArray(lenParams.Select(p => (JsonNode)p.Definition.Name).ToArray()), ["results"] = results };
    }

    [Tool("family_get_geometry_summary", "Volume/bounding box of all solid forms and void forms of a family — to sanity-check dimensions after building.", Category = "Family", ReadOnly = true)]
    public static object FamilyGetGeometrySummary()
    {
        var fd = Ctx.RequireFamilyDoc();
        var arr = new JsonArray();
        foreach (var f in new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).Cast<GenericForm>())
        {
            var bb = f.get_BoundingBox(null);
            arr.Add(new JsonObject { ["id"] = f.Id.Value, ["kind"] = f.GetType().Name, ["solid"] = f.IsSolid, ["bbox_mm"] = Dto.BBox(bb) });
        }
        return new JsonObject { ["forms"] = arr };
    }
}
