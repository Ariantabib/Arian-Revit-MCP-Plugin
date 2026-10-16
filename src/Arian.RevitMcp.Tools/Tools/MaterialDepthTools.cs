using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-6: appearance (colour, gloss, textures, bump), physical/thermal assets, Persian + global material library, material pass, render/export helpers.</summary>
public static class MaterialDepthTools
{
    // ------------------------------------------------------------------ appearance
    [Tool("set_material_appearance", """
        Edits the RENDER appearance of a material (own copy of the appearance asset is made if it is shared): colour, glossiness, reflectivity, diffuse texture image, bump image, texture scale.
        @material Material name. @color {r,g,b}. @glossiness 0-100. @reflectivity 0-100 (reflection at normal incidence). @diffuse_texture Path to an image (jpg/png/tif) used as colour map. @bump_texture Path to a bump-map image. @bump_amount 0-100.
        @texture_scale_mm Real-world width of one texture tile in mm (default: keep). Returns which properties were applied.
        """, Category = "Material", NoTransaction = true)]
    public static object SetMaterialAppearance(string material, MaterialTools.RgbColor color = null, double? glossiness = null, double? reflectivity = null, string diffuseTexture = null,
        string bumpTexture = null, double? bumpAmount = null, double? textureScaleMm = null)
    {
        var doc = Ctx.RequireDoc();
        var mat = Find.Material(doc, material);
        foreach (var p in new[] { diffuseTexture, bumpTexture }) if (!string.IsNullOrWhiteSpace(p) && !System.IO.File.Exists(p)) throw new ToolException("Image not found: " + p);
        ElementId assetId;
        using (var t = new Transaction(doc, "Arian MCP: appearance asset"))
        {
            t.Start();
            var current = doc.GetElement(mat.AppearanceAssetId) as AppearanceAssetElement;
            bool shared = current != null && new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Count(x => x.AppearanceAssetId == current.Id) > 1;
            if (current == null || shared)
            {
                var baseAsset = current ?? new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)).Cast<AppearanceAssetElement>().FirstOrDefault(a => a.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase))
                    ?? new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)).Cast<AppearanceAssetElement>().FirstOrDefault()
                    ?? throw new ToolException("The project has no appearance asset to copy from.");
                string nm = mat.Name + " (appearance)"; int k = 1;
                while (new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement)).Any(a => a.Name == nm)) nm = mat.Name + " (appearance " + (++k) + ")";
                var dup = baseAsset.Duplicate(nm); mat.AppearanceAssetId = dup.Id; assetId = dup.Id;
            }
            else assetId = current.Id;
            t.Commit();
        }
        var applied = new JsonArray(); var skipped = new JsonArray();
        // one edit scope per property: a failing property must not poison the others
        void Edit(string what, Action<Asset> a)
        {
            try
            {
                using var tx = new Transaction(doc, "Arian MCP: appearance " + what);
                tx.Start();
                using (var scope = new AppearanceAssetEditScope(doc))
                {
                    var ed = scope.Start(assetId);
                    a(ed);
                    scope.Commit(true);
                }
                tx.Commit();
                applied.Add(what);
            }
            catch (Exception ex) { skipped.Add(what + ": " + ex.Message.Split((char)10)[0]); }
        }
        void Bitmap(Asset ed, string propName, string path)
        {
            var prop = ed.FindByName(propName);
            var conn = prop.NumberOfConnectedProperties > 0 ? prop.GetConnectedProperty(0) as Asset : null;
            if (conn == null) { prop.AddConnectedAsset("UnifiedBitmap"); conn = prop.GetConnectedProperty(0) as Asset; }
            ((AssetPropertyString)conn.FindByName("unifiedbitmap_Bitmap")).Value = path;
            if (textureScaleMm.HasValue)
                foreach (var n in new[] { "texture_RealWorldScaleX", "texture_RealWorldScaleY" })
                    try { ((AssetPropertyDistance)conn.FindByName(n)).Value = UnitUtils.ConvertToInternalUnits(textureScaleMm.Value, UnitTypeId.Millimeters); } catch { }
        }
        if (color != null) Edit("color", ed => ((AssetPropertyDoubleArray4d)ed.FindByName("generic_diffuse")).SetValueAsColor(new Color(color.R, color.G, color.B)));
        if (glossiness.HasValue) Edit("glossiness", ed => ((AssetPropertyDouble)ed.FindByName("generic_glossiness")).Value = Math.Clamp(glossiness.Value, 0, 100) / 100.0);
        if (reflectivity.HasValue) Edit("reflectivity", ed => ((AssetPropertyDouble)ed.FindByName("generic_reflectivity_at_0deg")).Value = Math.Clamp(reflectivity.Value, 0, 100) / 100.0);
        if (!string.IsNullOrWhiteSpace(diffuseTexture)) Edit("diffuse_texture", ed => Bitmap(ed, "generic_diffuse", diffuseTexture));
        if (!string.IsNullOrWhiteSpace(bumpTexture)) Edit("bump_texture", ed => Bitmap(ed, "generic_bump_map", bumpTexture));
        if (bumpAmount.HasValue) Edit("bump_amount", ed => ((AssetPropertyDouble)ed.FindByName("generic_bump_amount")).Value = Math.Clamp(bumpAmount.Value, 0, 100) / 100.0);
        return new JsonObject { ["material"] = mat.Name, ["applied"] = applied, ["skipped"] = skipped };
    }

    // ------------------------------------------------------------------ physical / thermal
    sealed record Phys(string Id, string Cls, double Density, double EMpa, double Poisson, double CompressiveMpa, double YieldMpa, double TensileMpa, double ThermExp, double Conductivity, double SpecificHeat);
    static readonly Phys[] Presets =
    {
        new("concrete", "Concrete", 2400, 30000, 0.2, 25, 0, 0, 1.0e-5, 1.7, 880),
        new("steel", "Metal", 7850, 200000, 0.3, 0, 240, 360, 1.2e-5, 50, 460),
        new("steel_st37", "Metal", 7850, 210000, 0.3, 0, 240, 370, 1.2e-5, 50, 460),
        new("aluminium", "Metal", 2700, 70000, 0.33, 0, 160, 220, 2.3e-5, 160, 900),
        new("timber", "Wood", 500, 11000, 0.3, 0, 0, 0, 5e-6, 0.13, 1600),
        new("masonry", "Basic", 1800, 5000, 0.2, 5, 0, 0, 6e-6, 0.8, 840),
        new("insulation", "Basic", 30, 5, 0.1, 0, 0, 0, 1e-5, 0.035, 1030),
        new("glass", "Basic", 2500, 70000, 0.22, 0, 0, 0, 9e-6, 1.0, 840),
    };

    [Tool("set_material_physical", """
        Assigns Structural (physical) and Thermal property sets to a material, from a preset or custom values — used by analysis, energy and quantity workflows.
        @material Material. @preset concrete | steel | steel_st37 | aluminium | timber | masonry | insulation | glass (defaults for the numbers below).
        @density_kg_m3 @youngs_modulus_mpa @poisson @compressive_mpa (concrete/masonry) @yield_mpa @tensile_mpa (metals) @conductivity_w_mk (thermal) @specific_heat_j_kgk — override the preset.
        """, Category = "Material")]
    public static object SetMaterialPhysical(string material, string preset = "concrete", double? densityKgM3 = null, double? youngsModulusMpa = null, double? poisson = null,
        double? compressiveMpa = null, double? yieldMpa = null, double? tensileMpa = null, double? conductivityWMk = null, double? specificHeatJKgK = null)
    {
        var doc = Ctx.RequireDoc();
        var mat = Find.Material(doc, material);
        var p = Presets.FirstOrDefault(x => x.Id.Equals(preset, StringComparison.OrdinalIgnoreCase)) ?? throw new ToolException("preset must be one of: " + string.Join(", ", Presets.Select(x => x.Id)));
        double dens = densityKgM3 ?? p.Density, e = youngsModulusMpa ?? p.EMpa, nu = poisson ?? p.Poisson;
        double Pa(double mpa) => UnitUtils.ConvertToInternalUnits(mpa * 1e6, UnitTypeId.Pascals);
        var cls = p.Cls switch { "Concrete" => StructuralAssetClass.Concrete, "Metal" => StructuralAssetClass.Metal, "Wood" => StructuralAssetClass.Wood, _ => StructuralAssetClass.Basic };
        var sa = new StructuralAsset(mat.Name + " (structural)", cls) { Behavior = StructuralBehavior.Isotropic };
        sa.Density = UnitUtils.ConvertToInternalUnits(dens, UnitTypeId.KilogramsPerCubicMeter);
        sa.SetPoissonRatio(nu);
        sa.SetYoungModulus(Pa(e));
        double g = e / (2 * (1 + nu)); sa.SetShearModulus(Pa(g));
        double te = p.ThermExp; sa.SetThermalExpansionCoefficient(te);
        var notes = new JsonArray();
        try { if (cls == StructuralAssetClass.Concrete) sa.ConcreteCompression = Pa(compressiveMpa ?? p.CompressiveMpa); } catch (Exception ex) { notes.Add("compression: " + ex.Message); }
        try { if (cls == StructuralAssetClass.Metal) { sa.MinimumYieldStress = Pa(yieldMpa ?? p.YieldMpa); sa.MinimumTensileStrength = Pa(tensileMpa ?? p.TensileMpa); } } catch (Exception ex) { notes.Add("metal strengths: " + ex.Message); }
        var ta = new ThermalAsset(mat.Name + " (thermal)", ThermalMaterialType.Solid) { Behavior = StructuralBehavior.Isotropic };
        try { ta.ThermalConductivity = UnitUtils.ConvertToInternalUnits(conductivityWMk ?? p.Conductivity, UnitTypeId.WattsPerMeterKelvin); } catch (Exception ex) { notes.Add("conductivity: " + ex.Message); }
        try { ta.SpecificHeat = UnitUtils.ConvertToInternalUnits(specificHeatJKgK ?? p.SpecificHeat, UnitTypeId.JoulesPerKilogramDegreeCelsius); } catch (Exception ex) { notes.Add("specific heat: " + ex.Message); }
        try { ta.Density = UnitUtils.ConvertToInternalUnits(dens, UnitTypeId.KilogramsPerCubicMeter); } catch { }
        var ps = new FilteredElementCollector(doc).OfClass(typeof(PropertySetElement)).Cast<PropertySetElement>().FirstOrDefault(x => x.Name == sa.Name);
        if (ps != null) { doc.Delete(ps.Id); }
        ps = PropertySetElement.Create(doc, sa);
        mat.SetMaterialAspectByPropertySet(MaterialAspect.Structural, ps.Id);
        var pt = new FilteredElementCollector(doc).OfClass(typeof(PropertySetElement)).Cast<PropertySetElement>().FirstOrDefault(x => x.Name == ta.Name);
        if (pt != null) { doc.Delete(pt.Id); }
        pt = PropertySetElement.Create(doc, ta);
        mat.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, pt.Id);
        return new JsonObject { ["material"] = mat.Name, ["preset"] = p.Id, ["density_kg_m3"] = dens, ["youngs_modulus_mpa"] = e, ["structural_asset_id"] = ps.Id.Value, ["thermal_asset_id"] = pt.Id.Value, ["notes"] = notes };
    }

    // ------------------------------------------------------------------ library
    sealed record Lib(string Name, string Cls, int R, int G, int B, string Phys = null, string Cut = null, string Surface = null, int? Transparency = null);
    static readonly Lib[] Library =
    {
        // Iranian / regional (bilingual: Persian name - English name)
        new("بتن مسلح - Reinforced Concrete C25", "Concrete", 170, 170, 170, "concrete", "Diagonal crosshatch"),
        new("بتن مگر - Lean Concrete", "Concrete", 190, 190, 185, "concrete", "Diagonal crosshatch"),
        new("آجر فشاری - Clay Brick", "Masonry", 178, 92, 66, "masonry", "Crosshatch"),
        new("آجر نما - Facing Brick", "Masonry", 190, 105, 80, "masonry", "Crosshatch"),
        new("آجر سفال - Hollow Clay Block", "Masonry", 200, 120, 85, "masonry", "Crosshatch"),
        new("بلوک سیمانی - Concrete Block", "Masonry", 160, 160, 158, "masonry", "Crosshatch"),
        new("سنگ تراورتن - Travertine", "Stone", 226, 210, 178, null, null),
        new("سنگ گرانیت - Granite", "Stone", 120, 120, 125, null, null),
        new("سنگ مرمریت - Marble", "Stone", 235, 232, 225, null, null),
        new("سنگ لاشه - Rubble Stone", "Stone", 150, 140, 125, null, null),
        new("سرامیک - Ceramic Tile", "Ceramic", 240, 240, 236, null, null),
        new("کاشی لعابی - Glazed Tile", "Ceramic", 225, 235, 240, null, null),
        new("پارکت چوبی - Wood Parquet", "Wood", 160, 110, 70, "timber", null),
        new("چوب راش - Beech Timber", "Wood", 205, 165, 120, "timber", null),
        new("چوب نراد - Spruce Timber", "Wood", 220, 190, 140, "timber", null),
        new("گچ و خاک - Gypsum Plaster", "Gypsum", 235, 232, 225, null, null),
        new("ملات سیمان - Cement Plaster", "Concrete", 195, 195, 190, "concrete", null),
        new("ملات ماسه سیمان - Screed", "Concrete", 185, 185, 180, "concrete", null),
        new("رنگ سفید - Paint White", "Plastic", 245, 245, 245, null, null),
        new("رنگ کرم - Paint Cream", "Plastic", 240, 230, 205, null, null),
        new("رنگ طوسی - Paint Grey", "Plastic", 165, 165, 168, null, null),
        new("فولاد ST37 - Steel ST37", "Metal", 150, 155, 160, "steel_st37", "Diagonal crosshatch"),
        new("فولاد زنگ‌نزن - Stainless Steel", "Metal", 200, 202, 205, "steel", null),
        new("آلومینیوم - Aluminium", "Metal", 195, 198, 200, "aluminium", null),
        new("مس - Copper", "Metal", 184, 115, 51, "steel", null),
        new("روی - Zinc Sheet", "Metal", 170, 175, 180, "steel", null),
        new("شیشه شفاف - Clear Glass", "Glass", 200, 225, 235, "glass", null, null, 80),
        new("شیشه رفلکس - Reflective Glass", "Glass", 120, 150, 160, "glass", null, null, 60),
        new("پشم سنگ - Rock Wool", "Insulation", 230, 215, 120, "insulation", "Diagonal crosshatch"),
        new("پلی‌استایرن - EPS Insulation", "Insulation", 245, 245, 240, "insulation", null),
        new("ایزوگام - Bitumen Membrane", "Generic", 45, 45, 48, null, null),
        new("قیر - Bitumen", "Generic", 30, 30, 32, null, null),
        new("موزائیک - Mosaic Tile", "Ceramic", 190, 185, 175, null, null),
        new("خاک - Soil", "Earth", 120, 95, 70, null, "Sand - Dense"),
        new("چمن - Grass", "Plant", 85, 140, 60, null, null),
        // global
        new("Gypsum Board", "Gypsum", 240, 238, 232, "insulation", null),
        new("Plywood", "Wood", 205, 170, 115, "timber", null),
        new("OSB", "Wood", 190, 150, 100, "timber", null),
        new("Fibre Cement Panel", "Concrete", 180, 182, 178, "concrete", null),
    };

    [Tool("create_material_library", """
        Creates a curated material library in the project: Persian/Iranian materials (reinforced concrete, clay brick, facing brick, travertine, granite, plaster, ceramic, paint, ST37 steel, rock wool, ...) and common global ones — each with colour, class, cut pattern and, where relevant, structural/thermal physical properties.
        @only Optional name filter (substring) to create just some. @physical false to skip physical assets (faster).
        """, Category = "Material", NoTransaction = true)]
    public static object CreateMaterialLibrary(string only = null, bool physical = true)
    {
        var doc = Ctx.RequireDoc();
        var made = new JsonArray(); var warn = new JsonArray();
        foreach (var m in Library.Where(x => string.IsNullOrWhiteSpace(only) || x.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            Activity.Check();
            try
            {
                MaterialTools.CreateMaterial(m.Name, new MaterialTools.RgbColor { R = (byte)m.R, G = (byte)m.G, B = (byte)m.B }, m.Transparency, null, null, m.Cls, null, null, null, null, null);
                if (!string.IsNullOrEmpty(m.Cut))
                {
                    try { MaterialTools.CreateMaterial(m.Name, cutPattern: m.Cut, cutPatternColor: new MaterialTools.RgbColor { R = 90, G = 90, B = 90 }); } catch (Exception ex) { warn.Add($"{m.Name}: cut pattern '{m.Cut}' skipped ({ex.Message})"); }
                }
                if (physical && m.Phys != null)
                {
                    using var tg = new Transaction(doc, "Arian MCP: physical assets");
                    tg.Start();
                    try { SetMaterialPhysical(m.Name, m.Phys); tg.Commit(); } catch (Exception ex) { tg.RollBack(); warn.Add($"{m.Name}: physical skipped ({ex.Message})"); }
                }
                made.Add(m.Name);
            }
            catch (Exception ex) { warn.Add($"{m.Name}: {ex.Message}"); }
        }
        return new JsonObject { ["created_or_updated"] = made.Count, ["warnings"] = warn, ["materials"] = made };
    }

    // ------------------------------------------------------------------ material pass
    [Tool("material_pass", """
        Automatic 'material pass': assigns materials to the layers of wall / floor / roof / ceiling types from rules, by type-name substring and (optionally) layer function.
        @rules Array of {name_contains, material, function?} — function = structure | substrate | insulation | finish1 | finish2 | membrane | thermal (omit for all layers). @kinds wall, floor, roof, ceiling (default all). @dry_run true = only report.
        """, Category = "Material")]
    public static object MaterialPass(JsonArray rules, string[] kinds = null, bool dryRun = false)
    {
        var doc = Ctx.RequireDoc();
        kinds ??= new[] { "wall", "floor", "roof", "ceiling" };
        var types = new List<HostObjAttributes>();
        foreach (var k in kinds)
            types.AddRange(k.ToLowerInvariant() switch
            {
                "wall" => new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Where(w => w.Kind == WallKind.Basic),
                "floor" => new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<HostObjAttributes>(),
                "roof" => new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<HostObjAttributes>(),
                "ceiling" => new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<HostObjAttributes>(),
                _ => Enumerable.Empty<HostObjAttributes>(),
            });
        var changes = new JsonArray();
        foreach (var t in types)
        {
            var cs = t.GetCompoundStructure(); if (cs == null) continue; bool dirty = false;
            foreach (var r in rules)
            {
                string contains = r?["name_contains"]?.GetValue<string>(), matName = r?["material"]?.GetValue<string>(), fn = r?["function"]?.GetValue<string>();
                if (string.IsNullOrEmpty(matName) || (!string.IsNullOrEmpty(contains) && !t.Name.Contains(contains, StringComparison.OrdinalIgnoreCase))) continue;
                var mat = Find.Material(doc, matName);
                for (int i = 0; i < cs.LayerCount; i++)
                {
                    if (!string.IsNullOrEmpty(fn) && !cs.GetLayerFunction(i).ToString().Equals(fn, StringComparison.OrdinalIgnoreCase)) continue;
                    if (cs.GetMaterialId(i) == mat.Id) continue;
                    if (!dryRun) cs.SetMaterialId(i, mat.Id);
                    dirty = true; changes.Add(new JsonObject { ["type"] = t.Name, ["layer"] = i, ["function"] = cs.GetLayerFunction(i).ToString(), ["material"] = mat.Name });
                }
            }
            if (dirty && !dryRun) { try { t.SetCompoundStructure(cs); } catch (Exception ex) { changes.Add(new JsonObject { ["type"] = t.Name, ["error"] = ex.Message }); } }
        }
        return new JsonObject { ["dry_run"] = dryRun, ["layers_changed"] = changes.Count, ["changes"] = new JsonArray(changes.Take(200).Select(x => x.DeepClone()).ToArray()) };
    }

    // ------------------------------------------------------------------ rendering support
    [Tool("set_view_visual_style", "Sets a view's visual style: wireframe | hidden_line | shaded | consistent_colors | realistic. 'realistic' shows material appearance (textures) in the 3D view and in exported images. @view View name/id (default active).", Category = "View")]
    public static object SetViewVisualStyle(string style, string view = null)
    {
        var doc = Ctx.RequireProject();
        var v = view != null ? Find.View(doc, view) : doc.ActiveView;
        v.DisplayStyle = style.ToLowerInvariant() switch
        {
            "wireframe" => DisplayStyle.Wireframe, "hidden_line" or "hidden" => DisplayStyle.HLR, "shaded" => DisplayStyle.Shading,
            "consistent_colors" or "consistent" => DisplayStyle.ShadingWithEdges, "realistic" => DisplayStyle.Realistic,
            _ => throw new ToolException("style must be wireframe | hidden_line | shaded | consistent_colors | realistic"),
        };
        return new JsonObject { ["view"] = v.Name, ["style"] = v.DisplayStyle.ToString() };
    }

    [Tool("render_view_image", """
        Exports a view as a high-resolution PNG/JPG file using Revit's own renderer for the view's visual style (use set_view_visual_style 'realistic' first for materials/textures). @view View name/id (default active). @width_px Pixel size of the longest side (default 3000). @format png | jpg. @path Output file (default: Documents/ArianRenders).
        """, Category = "View", NoTransaction = true)]
    public static object RenderViewImage(string view = null, int widthPx = 3000, string format = "png", string path = null)
    {
        var doc = Ctx.RequireProject();
        var v = view != null ? Find.View(doc, view) : doc.ActiveView;
        string dir = path != null ? System.IO.Path.GetDirectoryName(path) : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianRenders");
        System.IO.Directory.CreateDirectory(dir);
        string baseName = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : (v.Name + "_" + DateTime.Now.ToString("HHmmss")).Replace(' ', '_');
        string stem = System.IO.Path.Combine(dir, baseName);
        var opt = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews, FilePath = stem, HLRandWFViewsFileType = format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? ImageFileType.JPEGLossless : ImageFileType.PNG,
            ImageResolution = ImageResolution.DPI_300, ZoomType = ZoomFitType.FitToPage, PixelSize = widthPx,
        };
        opt.SetViewsAndSheets(new List<ElementId> { v.Id });
        doc.ExportImage(opt);
        var file = System.IO.Directory.GetFiles(dir, baseName + "*").OrderByDescending(System.IO.File.GetLastWriteTime).FirstOrDefault();
        return new JsonObject { ["view"] = v.Name, ["file"] = file, ["bytes"] = file == null ? 0 : new System.IO.FileInfo(file).Length };
    }

    [Tool("export_fbx", "Exports a 3D view to FBX (Twinmotion, Lumion, Unreal, 3ds Max, Blender workflows) or other 3D formats. @view 3D view name/id (default active 3D view). @format fbx | obj | stl | step (default fbx). @path Output path (default Documents/ArianRenders).", Category = "View", NoTransaction = true)]
    public static object ExportFbx(string view = null, string format = "fbx", string path = null)
    {
        var doc = Ctx.RequireProject();
        var v = view != null ? Find.View(doc, view) : doc.ActiveView;
        if (v is not View3D v3) throw new ToolException("3D export needs a 3D view (open or pass one).");
        format = (format ?? "fbx").ToLowerInvariant();
        path ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianRenders", doc.Title + "." + format);
        string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileNameWithoutExtension(path);
        System.IO.Directory.CreateDirectory(dir);
        bool ok;
        switch (format)
        {
            case "fbx": { var vs = new ViewSet(); vs.Insert(v3); ok = doc.Export(dir, name, vs, new FBXExportOptions()); break; }
            case "obj": ok = doc.Export(dir, name, new OBJExportOptions { ViewId = v3.Id }); break;
            case "stl": ok = doc.Export(dir, name, new STLExportOptions { ViewId = v3.Id }); break;
            case "step": ok = doc.Export(dir, name, new STEPExportOptions { ViewId = v3.Id }); break;
            default: throw new ToolException("format must be fbx | obj | stl | step");
        }
        return new JsonObject { ["exported"] = ok, ["format"] = format, ["path"] = path };
    }

    [Tool("list_render_plugins", "Detects installed Revit rendering/visualisation add-ins (Enscape, Twinmotion, Lumion, V-Ray, Corona, D5, Cloud rendering...) from their .addin manifests and tells how to hand a model over to them.", Category = "View", ReadOnly = true, NoDocument = true)]
    public static object ListRenderPlugins()
    {
        var dirs = new[] { @"C:\ProgramData\Autodesk\Revit\Addins\2027", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027") };
        var known = new[] { "Enscape", "Twinmotion", "Lumion", "V-Ray", "Vray", "Corona", "D5", "Unreal", "Datasmith", "Thea", "Arkio", "Fuzor" };
        var found = new JsonArray();
        foreach (var d in dirs.Where(System.IO.Directory.Exists))
            foreach (var f in System.IO.Directory.GetFiles(d, "*.addin"))
            {
                string txt = ""; try { txt = System.IO.File.ReadAllText(f); } catch { }
                var hit = known.FirstOrDefault(k => System.IO.Path.GetFileName(f).Contains(k, StringComparison.OrdinalIgnoreCase) || txt.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (hit != null) found.Add(new JsonObject { ["plugin"] = hit, ["manifest"] = f });
            }
        return new JsonObject
        {
            ["installed"] = found,
            ["how_to"] = "Plugins have their own ribbon tabs. Programmatic hand-over: set_view_visual_style 'realistic' + render_view_image for stills, export_fbx for Twinmotion/Lumion/Unreal, then (last resort) drive the plugin's own tab with computer-use. Material appearance (set_material_appearance) is respected by all of them."
        };
    }
}
