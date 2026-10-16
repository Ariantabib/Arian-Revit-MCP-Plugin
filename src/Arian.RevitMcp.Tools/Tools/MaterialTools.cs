using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace Arian.RevitMcp.Tools;

public sealed class LayerSpec
{
    [Desc("Material name (must exist; create with create_material). Empty/null = no material (air gap).")] public string Material;
    [Desc("Layer thickness in mm (0 for membrane layers).")] public double ThicknessMm;
    [Desc("structure | substrate | insulation | finish1 | finish2 | membrane | structural_deck")] public string Function = "structure";
    [Desc("Wrap this layer at inserts/ends (walls).")] public bool Wraps;
}

public static class MaterialTools
{
    // ================================================================= materials
    static JsonObject MatInfo(Material m, bool full)
    {
        var o = new JsonObject
        {
            ["id"] = m.Id.Value,
            ["name"] = m.Name,
            ["class"] = m.MaterialClass,
            ["category"] = m.MaterialCategory,
            ["color"] = m.Color != null && m.Color.IsValid ? new JsonObject { ["r"] = m.Color.Red, ["g"] = m.Color.Green, ["b"] = m.Color.Blue } : null,
            ["transparency"] = m.Transparency,
        };
        if (full)
        {
            o["shininess"] = m.Shininess; o["smoothness"] = m.Smoothness;
            var doc = m.Document;
            o["appearance_asset"] = doc.GetElement(m.AppearanceAssetId)?.Name;
            o["physical_asset"] = doc.GetElement(m.StructuralAssetId)?.Name;
            o["thermal_asset"] = doc.GetElement(m.ThermalAssetId)?.Name;
            o["surface_pattern"] = doc.GetElement(m.SurfaceForegroundPatternId)?.Name;
            o["cut_pattern"] = doc.GetElement(m.CutForegroundPatternId)?.Name;
            o["parameters"] = Par.Values(m);
        }
        return o;
    }

    [Tool("list_materials", """
        Lists project materials.
        @name_contains Filter by name. @material_class Filter by class (Concrete, Metal, Wood, Glass, Masonry, Plastic, Stone, Earth, Fabric, Gypsum, Insulation, Liquid, Generic...).
        @limit Max results.
        """, Category = "Material", ReadOnly = true)]
    public static object ListMaterials(string nameContains = null, string materialClass = null, int limit = 300)
    {
        var doc = Ctx.RequireDoc();
        var arr = new JsonArray();
        foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name))
        {
            if (!string.IsNullOrEmpty(nameContains) && !m.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(materialClass) && !string.Equals(m.MaterialClass, materialClass, StringComparison.OrdinalIgnoreCase)) continue;
            arr.Add(MatInfo(m, false));
            if (arr.Count >= limit) break;
        }
        return new JsonObject { ["count"] = arr.Count, ["materials"] = arr };
    }

    [Tool("get_material", "Full details of a material (colour, transparency, shininess, assets, patterns, all parameters).", Category = "Material", ReadOnly = true)]
    public static object GetMaterial(string material)
    {
        var doc = Ctx.RequireDoc();
        return MatInfo(Find.Material(doc, material), true);
    }

    public sealed class MaterialSpec
    {
        public string Name;
        public RgbColor Color;
        public int? Transparency;
        public int? Shininess;
        public int? Smoothness;
        public string MaterialClass;
        public string SurfacePattern; public RgbColor SurfacePatternColor;
        public string CutPattern; public RgbColor CutPatternColor;
        public string CopyFrom;
        public bool UseSolidSurfacePatternWithColor;
    }

    public sealed class RgbColor { public byte R, G, B; }

    [Tool("create_material", """
        Creates a material (or updates it if the name exists), with colour, transparency, graphics patterns and a rendering appearance asset that uses the colour.
        @name Material name, e.g. 'Concrete - Cast In Place C30' or 'Paint - White RAL 9016'.
        @color {r,g,b} 0-255: shading colour and render diffuse colour.
        @transparency 0-100. @shininess 0-128. @smoothness 0-100.
        @material_class Class label shown in the Material Browser: Concrete, Metal, Wood, Glass, Masonry, Plastic, Stone, Gypsum, Insulation, Generic...
        @surface_pattern / @cut_pattern Fill pattern names (e.g. 'Solid fill', 'Crosshatch', 'Diagonal crosshatch'); with *_color for pattern colours.
        @copy_from Existing material to duplicate (inherits assets/patterns); the other args then override.
        """, Category = "Material", NoTransaction = true)]
    public static object CreateMaterial(string name, RgbColor color = null, int? transparency = null, int? shininess = null, int? smoothness = null,
        string materialClass = null, string surfacePattern = null, RgbColor surfacePatternColor = null, string cutPattern = null,
        RgbColor cutPatternColor = null, string copyFrom = null)
    {
        var doc = Ctx.RequireDoc();
        Material mat;
        using (var t = new Transaction(doc, "Arian MCP: create material"))
        {
            t.Start();
            mat = Find.Material(doc, name, true);
            if (mat == null)
            {
                if (!string.IsNullOrWhiteSpace(copyFrom)) mat = Find.Material(doc, copyFrom).Duplicate(name);
                else mat = (Material)doc.GetElement(Material.Create(doc, name));
            }
            if (color != null) mat.Color = new Color(color.R, color.G, color.B);
            if (transparency.HasValue) mat.Transparency = Math.Clamp(transparency.Value, 0, 100);
            if (shininess.HasValue) mat.Shininess = Math.Clamp(shininess.Value, 0, 128);
            if (smoothness.HasValue) mat.Smoothness = Math.Clamp(smoothness.Value, 0, 100);
            if (!string.IsNullOrWhiteSpace(materialClass)) mat.MaterialClass = materialClass;
            ApplyPatterns(doc, mat, surfacePattern, surfacePatternColor ?? color, cutPattern, cutPatternColor);
            t.Commit();
        }
        if (color != null || transparency.HasValue) SetAppearanceColor(doc, mat, color, transparency);
        return MatInfo(mat, true);
    }

    static void ApplyPatterns(Document doc, Material m, string surfacePattern, RgbColor surfaceColor, string cutPattern, RgbColor cutColor)
    {
        static string Norm(string x) => (x ?? "").Trim().Trim('<', '>').Trim();
        FillPatternElement Pat(string n) => new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => Norm(f.Name).Equals(Norm(n), StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException($"Fill pattern '{n}' not found. Use list_fill_patterns.");
        if (!string.IsNullOrWhiteSpace(surfacePattern))
        {
            m.SurfaceForegroundPatternId = Pat(surfacePattern).Id;
            if (surfaceColor != null) m.SurfaceForegroundPatternColor = new Color(surfaceColor.R, surfaceColor.G, surfaceColor.B);
        }
        if (!string.IsNullOrWhiteSpace(cutPattern))
        {
            m.CutForegroundPatternId = Pat(cutPattern).Id;
            if (cutColor != null) m.CutForegroundPatternColor = new Color(cutColor.R, cutColor.G, cutColor.B);
        }
    }

    /// <summary>Gives the material its own appearance asset (duplicated from a generic one) and writes the diffuse colour.</summary>
    static void SetAppearanceColor(Document doc, Material mat, RgbColor color, int? transparency)
    {
        try
        {
            ElementId assetId;
            using (var t = new Transaction(doc, "Arian MCP: appearance asset"))
            {
                t.Start();
                var current = doc.GetElement(mat.AppearanceAssetId) as AppearanceAssetElement;
                bool shared = current != null && new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Count(x => x.AppearanceAssetId == current.Id) > 1;
                if (current == null || shared)
                {
                    var baseAsset = current
                        ?? new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)).Cast<AppearanceAssetElement>()
                            .FirstOrDefault(a => a.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase))
                        ?? new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)).Cast<AppearanceAssetElement>().FirstOrDefault();
                    if (baseAsset == null) { t.RollBack(); return; }
                    string nm = mat.Name + " (appearance)";
                    int k = 1;
                    while (new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)).Any(a => a.Name == nm)) nm = mat.Name + " (appearance " + (++k) + ")";
                    var dup = baseAsset.Duplicate(nm);
                    mat.AppearanceAssetId = dup.Id;
                    assetId = dup.Id;
                }
                else assetId = current.Id;
                t.Commit();
            }
            using var txa = new Transaction(doc, "Arian MCP: appearance edit");
            txa.Start();
            using var scope = new AppearanceAssetEditScope(doc);
            var editable = scope.Start(assetId);
            if (color != null)
            {
                var c = new Color(color.R, color.G, color.B);
                foreach (var pn in new[] { "generic_diffuse", "common_Tint_color" })
                {
                    try
                    {
                        if (editable.FindByName(pn) is AssetPropertyDoubleArray4d p)
                        {
                            if (pn == "generic_diffuse") p.SetValueAsColor(c);
                        }
                    }
                    catch { }
                }
                // newer unified assets store colour in other names
                foreach (var pn in new[] { "generic_diffuse", "metal_f0", "glazing_transmittance_color" })
                {
                    try { if (editable.FindByName(pn) is AssetPropertyDoubleArray4d p2 && pn != "generic_diffuse") { } } catch { }
                }
            }
            if (transparency.HasValue)
            {
                try { if (editable.FindByName("generic_transparency") is AssetPropertyDouble d) d.Value = Math.Clamp(transparency.Value, 0, 100) / 100.0; } catch { }
            }
            scope.Commit(true);
            txa.Commit();
        }
        catch (Exception ex) { Log.Warn("Appearance asset edit failed: " + ex.Message); }
    }

    [Tool("duplicate_material", "Duplicates a material under a new name.", Category = "Material")]
    public static object DuplicateMaterial(string material, string newName)
    {
        var doc = Ctx.RequireDoc();
        var m = Find.Material(doc, material).Duplicate(newName);
        return MatInfo(m, false);
    }

    [Tool("delete_material", "Deletes a material.", Category = "Material", Destructive = true)]
    public static object DeleteMaterial(string material)
    {
        var doc = Ctx.RequireDoc();
        var m = Find.Material(doc, material);
        doc.Delete(m.Id);
        return new JsonObject { ["deleted"] = material };
    }

    [Tool("set_element_material", """
        Paints/assigns a material on elements: sets the 'Material' parameter of families/types (instance first, then type), or paints the faces of DirectShapes/any solid via the Paint tool.
        @ids Elements. @material Material name. @paint_faces Use face painting (works on walls, floors, any element) instead of the Material parameter.
        """, Category = "Material")]
    public static object SetElementMaterial(long[] ids, string material, bool paintFaces = false)
    {
        var doc = Ctx.RequireDoc();
        var m = Find.Material(doc, material);
        int n = 0;
        foreach (var e in Find.Elements(doc, ids))
        {
            if (paintFaces) { ArchBuildTools.PaintAll(doc, e, m); n++; continue; }
            var p = e.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM) ?? e.LookupParameter("Material") ?? e.LookupParameter("Structural Material");
            if (p == null || p.IsReadOnly)
            {
                var t = doc.GetElement(e.GetTypeId());
                p = t?.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM) ?? t?.LookupParameter("Material") ?? t?.LookupParameter("Structural Material");
            }
            if (p != null && !p.IsReadOnly) { p.Set(m.Id); n++; }
        }
        return new JsonObject { ["updated"] = n, ["note"] = n < ids.Length ? "Some elements have no writable Material parameter; use paint_faces=true or set_type_layers." : null };
    }

    // ================================================================= fill patterns / line styles
    [Tool("list_fill_patterns", "Lists fill patterns (drafting and model).", Category = "Material", ReadOnly = true)]
    public static object ListFillPatterns(string target = null)
    {
        var doc = Ctx.RequireDoc();
        var arr = new JsonArray();
        foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().OrderBy(f => f.Name))
        {
            var fp = f.GetFillPattern();
            var tg = fp.Target.ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(target) && tg != target.ToLowerInvariant()) continue;
            arr.Add(new JsonObject { ["id"] = f.Id.Value, ["name"] = f.Name, ["target"] = tg, ["solid"] = fp.IsSolidFill });
        }
        return new JsonObject { ["patterns"] = arr };
    }

    public sealed class HatchLine
    {
        [Desc("Angle of the hatch lines in degrees")] public double AngleDeg;
        [Desc("Spacing between parallel lines in mm (at model scale for model patterns, paper scale for drafting)")] public double SpacingMm;
    }

    [Tool("create_fill_pattern", """
        Creates a custom hatch fill pattern (drafting or model) from one or more line families (e.g. a brick bond = lines at 0° and 90°; crosshatch = 45° and 135°).
        @name Pattern name. @lines List of {angle_deg, spacing_mm}. @target drafting | model.
        """, Category = "Material")]
    public static object CreateFillPattern(string name, HatchLine[] lines, string target = "drafting")
    {
        var doc = Ctx.RequireDoc();
        var tg = target.Equals("model", StringComparison.OrdinalIgnoreCase) ? FillPatternTarget.Model : FillPatternTarget.Drafting;
        var fp = new FillPattern(name, tg, FillPatternHostOrientation.ToHost);
        var grids = new List<FillGrid>();
        foreach (var l in lines)
            grids.Add(new FillGrid { Angle = U.Deg(l.AngleDeg), Origin = UV.Zero, Offset = U.Mm(l.SpacingMm), Shift = 0 });
        fp.SetFillGrids(grids);
        var el = FillPatternElement.Create(doc, fp);
        return new JsonObject { ["id"] = el.Id.Value, ["name"] = el.Name };
    }

    [Tool("create_line_style", """
        Creates a new line style (subcategory of Lines) with colour, weight and optional pattern, usable for detail lines.
        @name Style name. @color {r,g,b}. @weight 1-16. @pattern Line pattern name (e.g. 'Dash', 'Center', 'Solid').
        """, Category = "Material")]
    public static object CreateLineStyle(string name, RgbColor color = null, int weight = 1, string pattern = null)
    {
        var doc = Ctx.RequireDoc();
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        Category sub = lines.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? doc.Settings.Categories.NewSubcategory(lines, name);
        sub.SetLineWeight(Math.Clamp(weight, 1, 16), GraphicsStyleType.Projection);
        if (color != null) sub.LineColor = new Color(color.R, color.G, color.B);
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var lp = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>().FirstOrDefault(p => p.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase));
            if (lp != null) sub.SetLinePatternId(lp.Id, GraphicsStyleType.Projection);
        }
        return new JsonObject { ["id"] = sub.Id.Value, ["name"] = sub.Name };
    }

    // ================================================================= compound types (wall / floor / roof / ceiling)
    static MaterialFunctionAssignment Fn(string s) => (s ?? "structure").Trim().ToLowerInvariant().Replace(' ', '_') switch
    {
        "structure" or "structural" => MaterialFunctionAssignment.Structure,
        "substrate" => MaterialFunctionAssignment.Substrate,
        "insulation" or "thermal" or "thermal_air_layer" => MaterialFunctionAssignment.Insulation,
        "finish1" or "finish_1" or "exterior_finish" => MaterialFunctionAssignment.Finish1,
        "finish2" or "finish_2" or "interior_finish" => MaterialFunctionAssignment.Finish2,
        "membrane" or "membrane_layer" => MaterialFunctionAssignment.Membrane,
        "structural_deck" or "deck" => MaterialFunctionAssignment.StructuralDeck,
        _ => throw new ToolException($"Unknown layer function '{s}'. Use structure, substrate, insulation, finish1, finish2, membrane, structural_deck."),
    };

    static string FnName(MaterialFunctionAssignment f) => f switch
    {
        MaterialFunctionAssignment.Structure => "structure",
        MaterialFunctionAssignment.Substrate => "substrate",
        MaterialFunctionAssignment.Insulation => "insulation",
        MaterialFunctionAssignment.Finish1 => "finish1",
        MaterialFunctionAssignment.Finish2 => "finish2",
        MaterialFunctionAssignment.Membrane => "membrane",
        MaterialFunctionAssignment.StructuralDeck => "structural_deck",
        _ => f.ToString().ToLowerInvariant(),
    };

    static CompoundStructure BuildStructure(Document doc, LayerSpec[] layers, bool wall, CompoundStructure existing = null)
    {
        if (layers == null || layers.Length == 0) throw new ToolException("At least one layer is required.");
        var list = new List<CompoundStructureLayer>();
        foreach (var l in layers)
        {
            var mat = string.IsNullOrWhiteSpace(l.Material) ? ElementId.InvalidElementId : Find.Material(doc, l.Material).Id;
            var fn = Fn(l.Function);
            double w = fn == MaterialFunctionAssignment.Membrane ? 0 : U.Mm(Math.Max(l.ThicknessMm, 1));
            list.Add(new CompoundStructureLayer(w, fn, mat));
        }
        CompoundStructure cs;
        if (existing != null) { cs = existing; cs.SetLayers(list); }
        else cs = CompoundStructure.CreateSimpleCompoundStructure(list);
        // core = from first to last structure layer; layers outside are shells
        int first = list.FindIndex(x => x.Function == MaterialFunctionAssignment.Structure || x.Function == MaterialFunctionAssignment.StructuralDeck);
        int last = list.FindLastIndex(x => x.Function == MaterialFunctionAssignment.Structure || x.Function == MaterialFunctionAssignment.StructuralDeck);
        if (first < 0) { first = 0; last = list.Count - 1; }
        try
        {
            cs.SetNumberOfShellLayers(ShellLayerType.Exterior, first);
            cs.SetNumberOfShellLayers(ShellLayerType.Interior, list.Count - 1 - last);
        }
        catch { }
        for (int i = 0; i < list.Count; i++) { try { cs.SetLayerWidth(i, list[i].Width); cs.SetMaterialId(i, list[i].MaterialId); cs.SetLayerFunction(i, list[i].Function); } catch { } }
        if (wall)
        {
            for (int i = 0; i < layers.Length; i++) { try { cs.SetParticipatesInWrapping(i, layers[i].Wraps); } catch { } }
            try { if (first >= 0) cs.StructuralMaterialIndex = first; } catch { }
        }
        return cs;
    }

    [Tool("create_compound_type", """
        Creates (or redefines) a layered wall/floor/roof/ceiling type with named layers and materials — e.g. a 300 mm cavity wall or a 150 mm slab with screed + finish. Duplicates based_on, then sets the layers from OUTSIDE to INSIDE (for walls: exterior first; for floors/roofs: top first).
        Wall example: layers=[{material:"Brick",thickness_mm:100,function:"finish1"},{material:"Insulation",thickness_mm:50,function:"insulation"},{material:"Concrete",thickness_mm:200,function:"structure"},{material:"Gypsum",thickness_mm:15,function:"finish2"}]
        @kind wall | floor | roof | ceiling
        @name New type name. @based_on Existing type to duplicate (default: first type of that kind).
        @layers Layers outside->inside with material names (see list_materials/create_material).
        """, Category = "Material")]
    public static object CreateCompoundType(string kind, string name, LayerSpec[] layers, string basedOn = null)
    {
        var doc = Ctx.RequireProject();
        var (cls, cat) = KindInfo(kind);
        ElementType baseT = basedOn != null ? Find.Type(doc, basedOn, cat, cls)
            : new FilteredElementCollector(doc).OfClass(cls).Cast<ElementType>().Where(t => t is not WallType w || w.Kind == WallKind.Basic)
                  .OrderByDescending(t => t.Name.StartsWith("Generic")).FirstOrDefault() ?? throw new ToolException($"No {kind} type available to duplicate.");
        var existing = new FilteredElementCollector(doc).OfClass(cls).Cast<ElementType>().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var type = existing ?? baseT.Duplicate(name);
        var curStruct = type is WallType w0 ? w0.GetCompoundStructure() : (type as HostObjAttributes)?.GetCompoundStructure();
        var cs = BuildStructure(doc, layers, kind.Equals("wall", StringComparison.OrdinalIgnoreCase), curStruct);
        switch (type)
        {
            case WallType wt: wt.SetCompoundStructure(cs); break;
            case HostObjAttributes h: h.SetCompoundStructure(cs); break;
            default: throw new ToolException("Type does not support compound structure.");
        }
        return TypeLayers((ElementType)doc.GetElement(type.Id));
    }

    static (Type, BuiltInCategory) KindInfo(string kind) => kind.ToLowerInvariant() switch
    {
        "wall" => (typeof(WallType), BuiltInCategory.OST_Walls),
        "floor" or "slab" => (typeof(FloorType), BuiltInCategory.OST_Floors),
        "roof" => (typeof(RoofType), BuiltInCategory.OST_Roofs),
        "ceiling" => (typeof(CeilingType), BuiltInCategory.OST_Ceilings),
        _ => throw new ToolException("kind must be wall, floor, roof or ceiling"),
    };

    static JsonObject TypeLayers(ElementType t)
    {
        CompoundStructure cs = t is WallType wt ? wt.GetCompoundStructure() : (t as HostObjAttributes)?.GetCompoundStructure();
        var arr = new JsonArray();
        if (cs != null)
            foreach (var l in cs.GetLayers())
                arr.Add(new JsonObject
                {
                    ["material"] = l.MaterialId == ElementId.InvalidElementId ? null : t.Document.GetElement(l.MaterialId)?.Name,
                    ["thickness_mm"] = U.R(U.ToMm(l.Width), 1),
                    ["function"] = FnName(l.Function),
                });
        return new JsonObject { ["id"] = t.Id.Value, ["name"] = t.Name, ["family"] = t.FamilyName, ["total_thickness_mm"] = cs == null ? null : U.R(U.ToMm(cs.GetWidth()), 1), ["layers"] = arr };
    }

    [Tool("get_type_layers", "Returns the layer stack (material, thickness, function) of a wall/floor/roof/ceiling type.", Category = "Material", ReadOnly = true)]
    public static object GetTypeLayers(string type)
    {
        var doc = Ctx.RequireProject();
        var t = Find.Type(doc, type);
        return TypeLayers(t);
    }

    [Tool("set_type_layers", "Replaces the layer stack of an existing wall/floor/roof/ceiling type (outside -> inside).", Category = "Material")]
    public static object SetTypeLayers(string type, LayerSpec[] layers)
    {
        var doc = Ctx.RequireProject();
        var t = Find.Type(doc, type);
        var cs = BuildStructure(doc, layers, t is WallType, t is WallType wt0 ? wt0.GetCompoundStructure() : (t as HostObjAttributes)?.GetCompoundStructure());
        if (t is WallType wt) wt.SetCompoundStructure(cs);
        else if (t is HostObjAttributes h) h.SetCompoundStructure(cs);
        else throw new ToolException("Type does not support layers.");
        return TypeLayers((ElementType)doc.GetElement(t.Id));
    }

    [Tool("create_simple_type", """
        Creates a new simple (non-layered) type for categories like stairs, railings, door/window types of a family: duplicates an existing type and sets parameters in one go.
        @based_on Existing type ('Family: Type', name or id). @name New type name. @parameters Type parameters to set {name: value} (mm values for lengths).
        """, Category = "Material")]
    public static object CreateSimpleType(string basedOn, string name, Dictionary<string, JsonNode> parameters = null)
    {
        var doc = Ctx.RequireDoc();
        var t = Find.Type(doc, basedOn);
        var n = t.Duplicate(name);
        JsonObject errs = null;
        if (parameters != null) errs = Par.SetMany(n, parameters);
        return new JsonObject { ["id"] = n.Id.Value, ["name"] = n.Name, ["errors"] = errs };
    }
}
