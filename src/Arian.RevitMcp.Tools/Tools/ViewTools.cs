using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Tools;

public static class ViewTools
{
    static ViewFamilyType Vft(Document doc, ViewFamily fam, string name = null)
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Where(v => v.ViewFamily == fam).ToList();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var hit = all.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return all.FirstOrDefault() ?? throw new ToolException($"No view family type for {fam} in this project.");
    }

    static void ApplyCommon(View v, string name, string template, int? scale, string detail)
    {
        if (!string.IsNullOrWhiteSpace(name)) { try { v.Name = name; } catch { v.Name = name + " (" + DateTime.Now.ToString("HHmmss") + ")"; } }
        if (!string.IsNullOrWhiteSpace(template)) v.ViewTemplateId = Find.ViewTemplate(v.Document, template).Id;
        if (scale.HasValue && v.ViewTemplateId == ElementId.InvalidElementId) v.Scale = scale.Value;
        if (!string.IsNullOrWhiteSpace(detail)) v.DetailLevel = ParseDetail(detail);
    }

    static ViewDetailLevel ParseDetail(string s) => s.Trim().ToLowerInvariant() switch
    {
        "coarse" => ViewDetailLevel.Coarse,
        "medium" => ViewDetailLevel.Medium,
        "fine" => ViewDetailLevel.Fine,
        _ => throw new ToolException("detail_level must be coarse, medium or fine"),
    };

    static JsonObject ViewInfo(View v) => new() { ["id"] = v.Id.Value, ["name"] = v.Name, ["type"] = v.ViewType.ToString() };

    // ================================================================ image
    [Tool("get_view_image", """
        Renders a view to a PNG and returns it as an image you can LOOK at. Use after building to visually verify the model (plans, sections, elevations, 3D). Defaults to the active view.
        The view is temporarily cropped to the model content so the drawing fills the image (the crop is not kept).
        @view View name/id (default: active view).
        @width_px Image width in pixels (default 1400).
        @fit_to_page Zoom to fit the model in the image.
        @crop_to_content Temporarily crop the view to the extent of the model (default true).
        @shaded Temporarily render 3D views with the shaded display style (default true).
        """, Category = "View", ReadOnly = true)]
    public static object GetViewImage(string view = null, int widthPx = 1400, bool fitToPage = true, bool cropToContent = true, bool shaded = true)
    {
        var doc = Ctx.RequireDoc();
        View v = string.IsNullOrWhiteSpace(view) ? doc.ActiveView : Find.View(doc, view);
        if (v == null) throw new ToolException("No active view. Pass a view name.");
        var dir = Path.Combine(Path.GetTempPath(), "arian-mcp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string note = null;
        using (var tg = new TransactionGroup(doc, "Arian MCP: temp view settings"))
        {
            tg.Start();
            try
            {
                if (cropToContent || shaded)
                {
                    using var t = new Transaction(doc, "temp view");
                    t.Start();
                    try
                    {
                        if (shaded && v is View3D v3 && v3.DisplayStyle != DisplayStyle.Shading && v3.DisplayStyle != DisplayStyle.Realistic) { try { v3.DisplayStyle = DisplayStyle.Shading; } catch { } }
                        if (cropToContent) note = TryCrop(doc, v);
                    }
                    catch (Exception ex) { note = "crop skipped: " + ex.Message; }
                    t.Commit();
                }
                var opts = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(dir, "view"),
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = Math.Clamp(widthPx, 200, 4000),
                    FitDirection = FitDirectionType.Horizontal,
                    ZoomType = fitToPage ? ZoomFitType.FitToPage : ZoomFitType.Zoom,
                };
                opts.SetViewsAndSheets(new List<ElementId> { v.Id });
                doc.ExportImage(opts);
            }
            finally { tg.RollBack(); }
        }
        var file = Directory.GetFiles(dir, "*.png").FirstOrDefault() ?? Directory.GetFiles(dir).FirstOrDefault()
                   ?? throw new ToolException("Revit did not produce an image for this view (is it a valid graphical view?).");
        var bytes = File.ReadAllBytes(file);
        try { Directory.Delete(dir, true); } catch { }
        var info = new JsonObject { ["view"] = v.Name, ["view_type"] = v.ViewType.ToString(), ["bytes"] = bytes.Length };
        if (note != null) info["note"] = note;
        return new ImageResult { Bytes = bytes, Info = info };
    }

    /// <summary>Crops the view to the bounding box of its visible model elements (inside a temporary transaction).</summary>
    static string TryCrop(Document doc, View v)
    {
        if (v is View3D v3 && v3.IsPerspective) return "perspective view not cropped";
        if (v is ViewSheet || v is ViewSchedule || v is ViewDrafting) return null;
        if (!v.CropBoxActive && v.ViewType == ViewType.ThreeD) { }
        BoundingBoxXYZ total = null;
        foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
        {
            var c = e.Category;
            if (c == null || c.CategoryType != CategoryType.Model) continue;
            if (c.Id.Value == (long)BuiltInCategory.OST_Levels || c.Id.Value == (long)BuiltInCategory.OST_Cameras) continue;
            BoundingBoxXYZ bb = null;
            try { bb = e.get_BoundingBox(v) ?? e.get_BoundingBox(null); } catch { }
            if (bb == null) continue;
            if (total == null) total = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max };
            else
            {
                total.Min = new XYZ(Math.Min(total.Min.X, bb.Min.X), Math.Min(total.Min.Y, bb.Min.Y), Math.Min(total.Min.Z, bb.Min.Z));
                total.Max = new XYZ(Math.Max(total.Max.X, bb.Max.X), Math.Max(total.Max.Y, bb.Max.Y), Math.Max(total.Max.Z, bb.Max.Z));
            }
        }
        if (total == null) return "view has no model content";
        var cb = v.CropBox;
        var inv = cb.Transform.Inverse;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var x in new[] { total.Min.X, total.Max.X })
            foreach (var y in new[] { total.Min.Y, total.Max.Y })
                foreach (var z in new[] { total.Min.Z, total.Max.Z })
                {
                    var p = inv.OfPoint(new XYZ(x, y, z));
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
        double mx = Math.Max((maxX - minX) * 0.08, U.Mm(500)), my = Math.Max((maxY - minY) * 0.08, U.Mm(500));
        var nb = new BoundingBoxXYZ { Transform = cb.Transform, Min = new XYZ(minX - mx, minY - my, cb.Min.Z), Max = new XYZ(maxX + mx, maxY + my, cb.Max.Z) };
        v.CropBox = nb;
        v.CropBoxActive = true;
        try { v.CropBoxVisible = false; } catch { }
        return null;
    }

    [Tool("open_view", "Makes a view the active view in the Revit window.", Category = "View", ReadOnly = true)]
    public static object OpenView(string view)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var ui = Ctx.UiDoc ?? throw new ToolException("No active UI document.");
        if (ui.Document.Equals(doc)) ui.RequestViewChange(v);
        return new JsonObject { ["view"] = v.Name };
    }

    // ================================================================ create views
    [Tool("create_floor_plan", """
        Creates a floor plan (or ceiling/structural/area plan) view for a level.
        @level Level name/id.
        @kind floor | ceiling | structural | area (default floor)
        @name View name. @view_template Optional template to apply. @scale e.g. 100. @detail_level coarse|medium|fine.
        """, Category = "View")]
    public static object CreateFloorPlan(string level, string kind = "floor", string name = null, string viewTemplate = null, int? scale = null, string detailLevel = null)
    {
        var doc = Ctx.RequireProject();
        var lv = Find.Level(doc, level);
        var fam = kind.ToLowerInvariant() switch
        {
            "ceiling" => ViewFamily.CeilingPlan,
            "structural" => ViewFamily.StructuralPlan,
            "area" => ViewFamily.AreaPlan,
            _ => ViewFamily.FloorPlan,
        };
        View v;
        if (fam == ViewFamily.AreaPlan)
        {
            var scheme = new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).FirstOrDefault() ?? throw new ToolException("No area scheme exists.");
            v = ViewPlan.CreateAreaPlan(doc, scheme.Id, lv.Id);
        }
        else v = ViewPlan.Create(doc, Vft(doc, fam).Id, lv.Id);
        ApplyCommon(v, name, viewTemplate, scale, detailLevel);
        return ViewInfo(v);
    }

    [Tool("create_3d_view", """
        Creates a 3D view: isometric (default) or perspective. For perspective give eye and target points.
        @name View name. @perspective Perspective camera. @eye [x,y,z] mm camera position. @target [x,y,z] mm point looked at.
        For isometric, eye/target set the viewing direction (default: south-west looking north-east, from above).
        """, Category = "View")]
    public static object Create3DView(string name = null, bool perspective = false, double[] eye = null, double[] target = null)
    {
        var doc = Ctx.RequireProject();
        View3D v;
        var fam = perspective ? ViewFamily.ThreeDimensional : ViewFamily.ThreeDimensional;
        var vft = Vft(doc, fam);
        v = perspective ? View3D.CreatePerspective(doc, vft.Id) : View3D.CreateIsometric(doc, vft.Id);
        if (eye != null || perspective)
        {
            var e = eye != null ? U.Pt(eye) : new XYZ(-30, -30, 20);
            var t = target != null ? U.Pt(target) : XYZ.Zero;
            var fwd = (t - e).Normalize();
            var right = fwd.CrossProduct(XYZ.BasisZ);
            if (right.GetLength() < 1e-6) right = XYZ.BasisX;
            var up = right.CrossProduct(fwd).Normalize();
            v.SetOrientation(new ViewOrientation3D(e, up, fwd));
        }
        if (!string.IsNullOrWhiteSpace(name)) { try { v.Name = name; } catch { } }
        try { v.DisplayStyle = DisplayStyle.Shading; } catch { }
        return ViewInfo(v);
    }

    [Tool("create_section", """
        Creates a section view cut along a line in plan. The section looks to the LEFT of the line direction start->end (flip=true reverses).
        @start [x,y] mm. @end [x,y] mm: the section line.
        @bottom_mm / @top_mm: vertical extent (absolute elevations).
        @depth_mm: how far the view looks (far clip).
        @name View name. @flip Look the other way.
        """, Category = "View")]
    public static object CreateSection(double[] start, double[] end, double bottomMm, double topMm, double depthMm = 5000, string name = null, bool flip = false)
    {
        var doc = Ctx.RequireProject();
        var s = U.Pt(start, 0); var e = U.Pt(end, 0);
        var dir = (e - s).Normalize();
        if (flip) { dir = -dir; var t = s; s = e; e = t; }
        double len = s.DistanceTo(e);
        var mid = (s + e) / 2;
        var tr = Transform.Identity;
        tr.Origin = new XYZ(mid.X, mid.Y, 0);
        tr.BasisX = dir;
        tr.BasisY = XYZ.BasisZ;
        tr.BasisZ = dir.CrossProduct(XYZ.BasisZ);
        var box = new BoundingBoxXYZ
        {
            Transform = tr,
            Min = new XYZ(-len / 2, U.Mm(bottomMm), 0),
            Max = new XYZ(len / 2, U.Mm(topMm), U.Mm(depthMm)),
        };
        var v = ViewSection.CreateSection(doc, Vft(doc, ViewFamily.Section).Id, box);
        if (!string.IsNullOrWhiteSpace(name)) { try { v.Name = name; } catch { } }
        return ViewInfo(v);
    }

    [Tool("create_elevation", """
        Creates an exterior/interior elevation view looking in a compass direction from a point (places an elevation marker in the given plan view).
        @point [x,y] mm: marker location (the viewer stands here). @direction north|south|east|west: direction the camera LOOKS toward.
        @plan_view Plan view name to host the marker (default: first floor plan). @scale e.g. 100.
        @name View name.
        """, Category = "View")]
    public static object CreateElevation(double[] point, string direction = "north", string planView = null, int scale = 100, string name = null)
    {
        var doc = Ctx.RequireProject();
        var plan = planView != null ? (ViewPlan)Find.View(doc, planView)
            : new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(p => !p.IsTemplate && p.ViewType == ViewType.FloorPlan);
        var marker = ElevationMarker.CreateElevationMarker(doc, Vft(doc, ViewFamily.Elevation).Id, U.Pt(point, 0), scale);
        // marker index: 0=north(looks +Y?) etc. Determine by testing view direction after creation.
        int want = direction.ToLowerInvariant() switch { "north" => 0, "east" => 1, "south" => 2, "west" => 3, _ => 0 };
        var target = new XYZ(direction.ToLowerInvariant() switch { "east" => 1, "west" => -1, _ => 0 }, direction.ToLowerInvariant() switch { "north" => 1, "south" => -1, _ => 0 }, 0);
        ViewSection best = null; double bestDot = -2;
        for (int i = 0; i < 4; i++)
        {
            var ev = marker.CreateElevation(doc, plan.Id, i);
            double d = ev.ViewDirection.DotProduct(-target); // ViewDirection points toward the viewer
            if (d > bestDot) { bestDot = d; best = ev; }
        }
        // delete the other elevations
        var keep = best.Id;
        for (int i = 0; i < 4; i++)
        {
            var id = marker.GetViewId(i);
            if (id != ElementId.InvalidElementId && id != keep) { try { doc.Delete(id); } catch { } }
        }
        if (!string.IsNullOrWhiteSpace(name)) { try { best.Name = name; } catch { } }
        return ViewInfo(best);
    }

    [Tool("create_callout", "Creates a callout (detail window) from a parent plan/section view over a rectangle.", Category = "View")]
    public static object CreateCallout(string parentView, double[] min, double[] max, string name = null, string kind = "detail")
    {
        var doc = Ctx.RequireProject();
        var parent = Find.View(doc, parentView);
        var fam = kind == "floor_plan" ? ViewFamily.FloorPlan : ViewFamily.Detail;
        var v = ViewSection.CreateCallout(doc, parent.Id, Vft(doc, fam).Id, U.Pt(min, 0), U.Pt(max, 0));
        if (!string.IsNullOrWhiteSpace(name)) { try { v.Name = name; } catch { } }
        return ViewInfo(v);
    }

    [Tool("create_drafting_view", "Creates a drafting view (2D detailing only).", Category = "View")]
    public static object CreateDraftingView(string name, int scale = 20)
    {
        var doc = Ctx.RequireProject();
        var v = ViewDrafting.Create(doc, Vft(doc, ViewFamily.Drafting).Id);
        try { v.Name = name; } catch { }
        v.Scale = scale;
        return ViewInfo(v);
    }

    [Tool("duplicate_view", """
        Duplicates a view.
        @mode duplicate | with_detailing | as_dependent
        @new_name Name for the copy.
        """, Category = "View")]
    public static object DuplicateView(string view, string mode = "with_detailing", string newName = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var opt = mode.ToLowerInvariant() switch { "duplicate" => ViewDuplicateOption.Duplicate, "as_dependent" => ViewDuplicateOption.AsDependent, _ => ViewDuplicateOption.WithDetailing };
        var id = v.Duplicate(opt);
        var nv = (View)doc.GetElement(id);
        if (!string.IsNullOrWhiteSpace(newName)) { try { nv.Name = newName; } catch { } }
        return ViewInfo(nv);
    }

    [Tool("delete_view", "Deletes a view.", Category = "View", Destructive = true)]
    public static object DeleteView(string view)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        doc.Delete(v.Id);
        return new JsonObject { ["deleted"] = view };
    }

    // ================================================================ view properties
    [Tool("set_view_properties", """
        Changes view settings.
        @view View name/id.
        @name Rename. @scale e.g. 50/100/200. @detail_level coarse|medium|fine.
        @display_style wireframe | hidden_line | shaded | consistent_colors | realistic.
        @view_template Template name to apply ('none' removes the template).
        @discipline architectural|structural|mechanical|electrical|plumbing|coordination.
        @crop_active Enable/disable the crop region. @crop_visible Show/hide the crop region.
        @phase Phase name for the view. @phase_filter Phase filter name.
        @underlay_level Underlay level for plans.
        """, Category = "View")]
    public static object SetViewProperties(string view, string name = null, int? scale = null, string detailLevel = null, string displayStyle = null,
        string viewTemplate = null, string discipline = null, bool? cropActive = null, bool? cropVisible = null, string phase = null,
        string phaseFilter = null, string underlayLevel = null)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        if (viewTemplate != null)
            v.ViewTemplateId = viewTemplate.Equals("none", StringComparison.OrdinalIgnoreCase) ? ElementId.InvalidElementId : Find.ViewTemplate(doc, viewTemplate).Id;
        if (name != null) v.Name = name;
        if (scale.HasValue) v.Scale = scale.Value;
        if (detailLevel != null) v.DetailLevel = ParseDetail(detailLevel);
        if (displayStyle != null)
        {
            v.DisplayStyle = displayStyle.ToLowerInvariant().Replace(' ', '_') switch
            {
                "wireframe" => DisplayStyle.Wireframe,
                "hidden_line" or "hidden" => DisplayStyle.HLR,
                "shaded" => DisplayStyle.Shading,
                "consistent_colors" => DisplayStyle.ShadingWithEdges,
                "realistic" => DisplayStyle.Realistic,
                _ => throw new ToolException("display_style: wireframe, hidden_line, shaded, consistent_colors, realistic"),
            };
        }
        if (discipline != null)
        {
            var p = v.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
            int val = discipline.ToLowerInvariant() switch { "architectural" => 1, "structural" => 2, "mechanical" => 4, "electrical" => 8, "plumbing" => 16, "coordination" => 4095, _ => 1 };
            p?.Set(val);
        }
        if (cropActive.HasValue) v.CropBoxActive = cropActive.Value;
        if (cropVisible.HasValue) v.CropBoxVisible = cropVisible.Value;
        if (phase != null) v.get_Parameter(BuiltInParameter.VIEW_PHASE)?.Set(Find.Named<Phase>(doc, phase).Id);
        if (phaseFilter != null)
        {
            var pf = new FilteredElementCollector(doc).OfClass(typeof(PhaseFilter)).FirstOrDefault(e => e.Name.Equals(phaseFilter, StringComparison.OrdinalIgnoreCase));
            if (pf == null) throw new ToolException("Phase filter not found: " + phaseFilter);
            v.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER)?.Set(pf.Id);
        }
        if (underlayLevel != null && v is ViewPlan vp) vp.SetUnderlayRange(Find.Level(doc, underlayLevel).Id, ElementId.InvalidElementId);
        return ViewInfo(v);
    }

    [Tool("set_view_range", """
        Sets the view range of a plan view (all mm, measured from the view's associated level unless noted).
        @view Plan view. @top_offset_mm @cut_plane_offset_mm @bottom_offset_mm @view_depth_offset_mm: offsets from the level.
        """, Category = "View")]
    public static object SetViewRange(string view, double topOffsetMm = 2300, double cutPlaneOffsetMm = 1200, double bottomOffsetMm = 0, double viewDepthOffsetMm = 0)
    {
        var doc = Ctx.RequireProject();
        var vp = Find.View(doc, view) as ViewPlan ?? throw new ToolException("Not a plan view.");
        var r = vp.GetViewRange();
        r.SetOffset(PlanViewPlane.TopClipPlane, U.Mm(topOffsetMm));
        r.SetOffset(PlanViewPlane.CutPlane, U.Mm(cutPlaneOffsetMm));
        r.SetOffset(PlanViewPlane.BottomClipPlane, U.Mm(bottomOffsetMm));
        r.SetOffset(PlanViewPlane.ViewDepthPlane, U.Mm(viewDepthOffsetMm));
        vp.SetViewRange(r);
        return ViewInfo(vp);
    }

    [Tool("set_view_crop", "Sets the crop box of a view to a rectangle in model coordinates (plans/elevations) and turns cropping on.", Category = "View")]
    public static object SetViewCrop(string view, double[] min, double[] max)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var box = v.CropBox;
        var inv = box.Transform.Inverse;
        var a = inv.OfPoint(U.Pt(min)); var b = inv.OfPoint(U.Pt(max));
        box.Min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), box.Min.Z);
        box.Max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), box.Max.Z);
        v.CropBox = box;
        v.CropBoxActive = true;
        return ViewInfo(v);
    }

    [Tool("set_section_box", "Sets the 3D section box of a 3D view to the given model-space box and enables it.", Category = "View")]
    public static object SetSectionBox(string view, double[] min, double[] max, bool enabled = true)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view) as View3D ?? throw new ToolException("Not a 3D view.");
        v.SetSectionBox(new BoundingBoxXYZ { Min = U.Pt(min), Max = U.Pt(max) });
        v.IsSectionBoxActive = enabled;
        return ViewInfo(v);
    }

    // ================================================================ visibility / graphics
    [Tool("set_category_visibility", """
        Shows or hides whole categories in a view (e.g. hide 'Furniture' in a structural plan).
        @view View. @categories Category names. @visible true to show, false to hide.
        """, Category = "View")]
    public static object SetCategoryVisibility(string view, string[] categories, bool visible)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        foreach (var c in categories) v.SetCategoryHidden(Find.Category(doc, c).Id, !visible);
        return new JsonObject { ["updated"] = categories.Length, ["visible"] = visible };
    }

    [Tool("hide_elements", "Hides (or unhides) specific elements in a view.", Category = "View")]
    public static object HideElements(string view, long[] ids, bool hide = true)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var list = Find.Ids(ids);
        if (hide) v.HideElements(list); else v.UnhideElements(list);
        return new JsonObject { ["count"] = list.Count };
    }

    [Tool("isolate_elements", "Temporarily isolates elements in a view (hides everything else). Use reset=true to restore.", Category = "View")]
    public static object IsolateElements(string view, long[] ids = null, bool reset = false)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        if (reset) { v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate); return new JsonObject { ["reset"] = true }; }
        v.IsolateElementsTemporary(Find.Ids(ids));
        return new JsonObject { ["isolated"] = ids.Length };
    }

    public sealed class RgbColor
    {
        public byte R, G, B;
    }

    [Tool("override_graphics", """
        Overrides how elements or a whole category are drawn in a view (colour, line weight, transparency, halftone, surface/cut fill colour & pattern). Great for colour-coding (rooms by department, fire ratings, phases).
        @view View name/id.
        @element_ids Elements to override (or use category).
        @category Category name to override for all elements.
        @line_color / @fill_color RGB {r,g,b} 0-255 (fill_color colours surface AND cut patterns with solid fill).
        @line_weight 1-16. @transparency 0-100 (surface). @halftone Draw in halftone.
        @solid_fill Use the solid fill pattern for surfaces/cut with fill_color (default true).
        @clear Remove overrides instead.
        """, Category = "View")]
    public static object OverrideGraphics(string view, long[] elementIds = null, string category = null, RgbColor lineColor = null, RgbColor fillColor = null,
        int? lineWeight = null, int? transparency = null, bool? halftone = null, bool solidFill = true, bool clear = false)
    {
        var doc = Ctx.RequireProject();
        var v = Find.View(doc, view);
        var ogs = new OverrideGraphicSettings();
        if (!clear)
        {
            if (lineColor != null) { var c = new Color(lineColor.R, lineColor.G, lineColor.B); ogs.SetProjectionLineColor(c); ogs.SetCutLineColor(c); }
            if (lineWeight.HasValue) { ogs.SetProjectionLineWeight(lineWeight.Value); ogs.SetCutLineWeight(lineWeight.Value); }
            if (transparency.HasValue) ogs.SetSurfaceTransparency(Math.Clamp(transparency.Value, 0, 100));
            if (halftone.HasValue) ogs.SetHalftone(halftone.Value);
            if (fillColor != null)
            {
                var c = new Color(fillColor.R, fillColor.G, fillColor.B);
                ogs.SetSurfaceForegroundPatternColor(c); ogs.SetCutForegroundPatternColor(c);
                if (solidFill)
                {
                    var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                        .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
                    if (solid != null) { ogs.SetSurfaceForegroundPatternId(solid.Id); ogs.SetCutForegroundPatternId(solid.Id); }
                }
            }
        }
        int n = 0;
        if (!string.IsNullOrWhiteSpace(category)) { v.SetCategoryOverrides(Find.Category(doc, category).Id, ogs); n++; }
        if (elementIds != null) foreach (var id in elementIds) { v.SetElementOverrides(new ElementId(id), ogs); n++; }
        return new JsonObject { ["overrides_applied"] = n, ["cleared"] = clear };
    }

    // ================================================================ sheets
    [Tool("list_titleblocks", "Lists title block types available for sheets.", Category = "View", ReadOnly = true)]
    public static object ListTitleblocks()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<ElementType>()
            .Select(t => (JsonNode)new JsonObject { ["id"] = t.Id.Value, ["name"] = t.FamilyName + ": " + t.Name }).ToArray());
        return new JsonObject { ["titleblocks"] = arr };
    }

    [Tool("create_sheet", """
        Creates a sheet.
        @number Sheet number (e.g. A-101). @name Sheet name. @titleblock Title block type name (default: first available; 'none' for no title block).
        """, Category = "View")]
    public static object CreateSheet(string number, string name, string titleblock = null)
    {
        var doc = Ctx.RequireProject();
        ElementId tb = ElementId.InvalidElementId;
        if (!"none".Equals(titleblock, StringComparison.OrdinalIgnoreCase))
        {
            var t = titleblock != null ? Find.Type(doc, titleblock, BuiltInCategory.OST_TitleBlocks)
                : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<ElementType>().FirstOrDefault();
            if (t != null) { if (t is FamilySymbol fs && !fs.IsActive) fs.Activate(); tb = t.Id; }
        }
        var s = ViewSheet.Create(doc, tb);
        s.SheetNumber = number; s.Name = name;
        return new JsonObject { ["id"] = s.Id.Value, ["number"] = s.SheetNumber, ["name"] = s.Name };
    }

    [Tool("place_view_on_sheet", """
        Places a view (or schedule) on a sheet.
        @sheet Sheet number or name or id. @view View name/id. @center [x,y] mm position of the viewport centre on the sheet (default sheet centre-ish [420,297]).
        """, Category = "View")]
    public static object PlaceViewOnSheet(string sheet, string view, double[] center = null)
    {
        var doc = Ctx.RequireProject();
        var sh = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(s => s.SheetNumber.Equals(sheet, StringComparison.OrdinalIgnoreCase) || s.Name.Equals(sheet, StringComparison.OrdinalIgnoreCase) || s.Id.Value.ToString() == sheet)
            ?? throw new ToolException($"Sheet '{sheet}' not found.");
        var v = Find.View(doc, view);
        var c = center != null ? U.Pt(center, 0) : new XYZ(U.Mm(420), U.Mm(297), 0);
        if (v is ViewSchedule vs)
        {
            var inst = ScheduleSheetInstance.Create(doc, sh.Id, vs.Id, c);
            return new JsonObject { ["schedule_instance_id"] = inst.Id.Value };
        }
        var vp = Viewport.Create(doc, sh.Id, v.Id, c);
        return new JsonObject { ["viewport_id"] = vp.Id.Value, ["sheet"] = sh.SheetNumber };
    }

    // ================================================================ export
    static JsonArray NewFiles(string folder, DateTime since)
        => new JsonArray(new DirectoryInfo(folder).GetFiles().Where(f => f.LastWriteTime >= since).Select(f => (JsonNode)new JsonObject { ["file"] = f.FullName, ["bytes"] = f.Length }).ToArray());

    /// <summary>A view by name/id or a sheet by number/name/id.</summary>
    static View ViewOrSheet(Document doc, string key)
    {
        var sh = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(x => x.SheetNumber.Equals(key, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(key, StringComparison.OrdinalIgnoreCase) || x.Id.Value.ToString() == key);
        return sh ?? Find.View(doc, key);
    }

    [Tool("export_pdf", """
        Exports views/sheets to PDF.
        @folder Output folder. @file_name File name (no extension). @views View names/ids or sheet numbers/names/ids to export (default: all sheets).
        @combine Combine into a single PDF.
        """, Category = "View", ReadOnly = true)]
    public static object ExportPdf(string folder, string fileName = "export", string[] views = null, bool combine = true)
    {
        var doc = Ctx.RequireProject();
        Directory.CreateDirectory(folder);
        List<ElementId> ids = views == null || views.Length == 0
            ? new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).ToElementIds().ToList()
            : views.Select(v => ViewOrSheet(doc, v).Id).ToList();
        var opt = new PDFExportOptions { FileName = fileName, Combine = combine };
        var t0 = DateTime.Now.AddSeconds(-2);
        bool ok = doc.Export(folder, ids, opt);
        return new JsonObject { ["exported"] = ok, ["folder"] = folder, ["views"] = ids.Count, ["files"] = NewFiles(folder, t0) };
    }

    [Tool("export_dwg", "Exports views or sheets (names/ids/sheet numbers) to DWG. Returns the folder and the files written.", Category = "View", ReadOnly = true)]
    public static object ExportDwg(string folder, string[] views, string fileName = "export")
    {
        var doc = Ctx.RequireProject();
        Directory.CreateDirectory(folder);
        var ids = views.Select(v => ViewOrSheet(doc, v).Id).ToList();
        var t0 = DateTime.Now.AddSeconds(-2);
        bool ok = doc.Export(folder, fileName, ids, new DWGExportOptions());
        return new JsonObject { ["exported"] = ok, ["folder"] = folder, ["files"] = NewFiles(folder, t0) };
    }

    [Tool("export_ifc", "Exports the model to IFC (IFC2x3 or IFC4).", Category = "View", ReadOnly = true)]
    public static object ExportIfc(string folder, string fileName = "export", string version = "IFC4")
    {
        var doc = Ctx.RequireProject();
        Directory.CreateDirectory(folder);
        var opt = new IFCExportOptions { FileVersion = version.Equals("IFC2x3", StringComparison.OrdinalIgnoreCase) ? IFCVersion.IFC2x3CV2 : IFCVersion.IFC4 };
        // IFC export needs a transaction
        using var t = new Transaction(doc, "Arian MCP: export IFC");
        t.Start();
        bool ok = doc.Export(folder, fileName, opt);
        t.RollBack();
        return new JsonObject { ["exported"] = ok, ["file"] = Path.Combine(folder, fileName + ".ifc") };
    }
}
