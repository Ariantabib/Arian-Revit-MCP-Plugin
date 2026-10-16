using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Tools;

public static class ManageTools
{
    // ================================================================= warnings
    [Tool("get_warnings", """
        Lists the model's warnings grouped by description with counts and (optionally) the element ids involved. Use before issuing a model / to find modelling problems (overlapping walls, duplicate instances, room not enclosed, ...).
        @details Include the failing element ids per warning.
        @limit Max groups.
        """, Category = "Manage", ReadOnly = true)]
    public static object GetWarnings(bool details = false, int limit = 30)
    {
        var doc = Ctx.RequireDoc();
        var warns = doc.GetWarnings();
        var groups = warns.GroupBy(w => w.GetDescriptionText()).OrderByDescending(g => g.Count()).Take(limit);
        var arr = new JsonArray();
        foreach (var g in groups)
        {
            var o = new JsonObject { ["description"] = g.Key, ["count"] = g.Count(), ["severity"] = g.First().GetSeverity().ToString() };
            if (details)
            {
                var items = new JsonArray();
                foreach (var w in g.Take(25))
                    items.Add(new JsonObject { ["failing"] = Dto.IdList(w.GetFailingElements()), ["additional"] = Dto.IdList(w.GetAdditionalElements()) });
                o["items"] = items;
            }
            arr.Add(o);
        }
        return new JsonObject { ["total_warnings"] = warns.Count, ["groups"] = arr };
    }

    [Tool("resolve_warnings", """
        Automatically cleans up safe classes of warnings.
        @strategy 'delete_duplicates' deletes the extra element of 'Identical instances in the same place' / duplicate-mark style warnings (keeps one); 'select' selects the failing elements in the UI for manual review.
        @description_contains Only warnings whose text contains this (default: identical/duplicate ones).
        @dry_run Report what would be deleted without deleting.
        """, Category = "Manage")]
    public static object ResolveWarnings(string strategy = "delete_duplicates", string descriptionContains = null, bool dryRun = true)
    {
        var doc = Ctx.RequireDoc();
        var warns = doc.GetWarnings();
        var toDelete = new HashSet<ElementId>();
        var toSelect = new HashSet<ElementId>();
        foreach (var w in warns)
        {
            string d = w.GetDescriptionText();
            bool match = descriptionContains != null ? d.Contains(descriptionContains, StringComparison.OrdinalIgnoreCase)
                : d.Contains("identical instances", StringComparison.OrdinalIgnoreCase) || d.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            var ids = w.GetFailingElements().ToList();
            foreach (var i in ids) toSelect.Add(i);
            if (strategy == "delete_duplicates") foreach (var i in ids.Skip(1)) toDelete.Add(i);
        }
        if (strategy == "select") { Ctx.UiDoc?.Selection.SetElementIds(toSelect.ToList()); return new JsonObject { ["selected"] = toSelect.Count }; }
        if (!dryRun) { foreach (var id in toDelete.ToList()) { try { if (doc.GetElement(id) != null) doc.Delete(id); } catch { } } }
        return new JsonObject { ["would_delete"] = dryRun ? toDelete.Count : null, ["deleted"] = dryRun ? null : toDelete.Count, ["ids"] = Dto.IdList(toDelete.Take(200)), ["dry_run"] = dryRun };
    }

    // ================================================================= purge / audit
    [Tool("purge_unused", """
        Removes unused families, types, materials, line patterns, view filters etc. (Revit's Purge Unused). Runs several passes since purging reveals more unused items.
        @dry_run Only report (default true — set false to actually delete).
        @exclude Classes to keep, e.g. ['Material','FillPatternElement'].
        """, Category = "Manage", Destructive = true)]
    public static object PurgeUnused(bool dryRun = true, string[] exclude = null)
    {
        var doc = Ctx.RequireDoc();
        var ex = new HashSet<string>(exclude ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var summary = new Dictionary<string, int>();
        int total = 0;
        for (int pass = 0; pass < 6; pass++)
        {
            var unused = doc.GetUnusedElements(new HashSet<ElementId>()).Where(id =>
            {
                var e = doc.GetElement(id); return e != null && !ex.Contains(e.GetType().Name);
            }).ToList();
            if (unused.Count == 0) break;
            foreach (var id in unused)
            {
                var e = doc.GetElement(id); string k = e?.GetType().Name ?? "?";
                summary[k] = summary.TryGetValue(k, out var n) ? n + 1 : 1;
            }
            if (dryRun) { total = unused.Count; break; }
            int removed = 0;
            foreach (var id in unused) { try { if (doc.GetElement(id) != null) { doc.Delete(id); removed++; } } catch { } }
            total += removed;
            if (removed == 0) break;
        }
        return new JsonObject { ["dry_run"] = dryRun, ["unused_total"] = total, ["by_class"] = new JsonObject(summary.OrderByDescending(k => k.Value).Select(k => new KeyValuePair<string, JsonNode>(k.Key, k.Value))) };
    }

    [Tool("audit_model_health", "BIM health check: warnings, in-place families, imported CAD, unused types, views not on sheets, groups, links, design options, file size — with a short list of recommended fixes.", Category = "Manage", ReadOnly = true)]
    public static object AuditModelHealth()
    {
        var doc = Ctx.RequireProject();
        var o = new JsonObject();
        var recs = new JsonArray();
        int warn = doc.GetWarnings().Count; o["warnings"] = warn;
        if (warn > 100) recs.Add($"{warn} warnings: review with get_warnings; fix or resolve_warnings.");
        var inPlace = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Count(f => f.IsInPlace); o["in_place_families"] = inPlace;
        if (inPlace > 0) recs.Add($"{inPlace} in-place families: prefer loadable families.");
        var cad = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().Count(i => !i.IsLinked); o["imported_cad"] = cad;
        if (cad > 0) recs.Add("Imported (not linked) CAD found: link instead or delete.");
        try { var un = doc.GetUnusedElements(new HashSet<ElementId>()).Count; o["unused_elements"] = un; if (un > 200) recs.Add($"{un} unused elements: run purge_unused."); } catch { }
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
        var placed = new HashSet<ElementId>(sheets.SelectMany(s => s.GetAllPlacedViews()));
        int notPlaced = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser && v.ViewType != ViewType.Internal && !placed.Contains(v.Id) && v.CanBePrinted);
        o["views_not_on_sheets"] = notPlaced;
        o["sheets"] = sheets.Count;
        o["groups"] = new FilteredElementCollector(doc).OfClass(typeof(Group)).GetElementCount();
        o["links"] = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
        o["design_options"] = new FilteredElementCollector(doc).OfClass(typeof(DesignOption)).GetElementCount();
        o["rooms_unplaced_or_unenclosed"] = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<SpatialElement>().Count(r => r.Area <= 0);
        o["file_size_mb"] = string.IsNullOrEmpty(doc.PathName) || !File.Exists(doc.PathName) ? null : U.R(new FileInfo(doc.PathName).Length / 1048576.0, 1);
        o["recommendations"] = recs;
        return o;
    }

    // ================================================================= links & imports
    [Tool("list_links", "Lists linked Revit models and CAD links.", Category = "Manage", ReadOnly = true)]
    public static object ListLinks()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var t = doc.GetElement(l.GetTypeId()) as RevitLinkType;
            arr.Add(new JsonObject { ["id"] = l.Id.Value, ["name"] = l.Name, ["loaded"] = RevitLinkType.IsLoaded(doc, l.GetTypeId()), ["path"] = t?.GetExternalFileReference()?.GetAbsolutePath() is { } p ? ModelPathUtils.ConvertModelPathToUserVisiblePath(p) : null });
        }
        foreach (var c in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
            arr.Add(new JsonObject { ["id"] = c.Id.Value, ["name"] = c.Name, ["kind"] = c.IsLinked ? "cad link" : "cad import" });
        return new JsonObject { ["links"] = arr };
    }

    [Tool("link_revit_model", "Links another Revit model into the project (origin-to-origin by default).", Category = "Manage", NoTransaction = true)]
    public static object LinkRevitModel(string path)
    {
        var doc = Ctx.RequireProject();
        if (!File.Exists(path)) throw new ToolException("File not found: " + path);
        using var t = new Transaction(doc, "Arian MCP: link model");
        t.Start();
        var res = RevitLinkType.Create(doc, ModelPathUtils.ConvertUserVisiblePathToModelPath(path), new RevitLinkOptions(false));
        var inst = RevitLinkInstance.Create(doc, res.ElementId);
        t.Commit();
        return new JsonObject { ["link_instance_id"] = inst.Id.Value };
    }

    [Tool("link_cad", "Links a DWG/DXF file into a view (typically a plan) at the origin.", Category = "Manage")]
    public static object LinkCad(string path, string view = null, string units = "millimeter", bool currentViewOnly = true)
    {
        var doc = Ctx.RequireProject();
        var v = view != null ? Find.View(doc, view) : doc.ActiveView;
        var opt = new DWGImportOptions { ColorMode = ImportColorMode.BlackAndWhite, Unit = units.ToLowerInvariant() switch { "meter" or "metre" => ImportUnit.Meter, "foot" => ImportUnit.Foot, "inch" => ImportUnit.Inch, "centimeter" => ImportUnit.Centimeter, _ => ImportUnit.Millimeter }, Placement = ImportPlacement.Origin, ThisViewOnly = currentViewOnly, VisibleLayersOnly = true };
        doc.Link(path, opt, v, out var id);
        return new JsonObject { ["id"] = id.Value };
    }

    // ================================================================= worksets / phases / design options
    [Tool("list_worksets", "Lists user worksets (workshared projects).", Category = "Manage", ReadOnly = true)]
    public static object ListWorksets()
    {
        var doc = Ctx.RequireProject();
        if (!doc.IsWorkshared) return new JsonObject { ["workshared"] = false };
        var arr = new JsonArray(new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).Select(w => (JsonNode)new JsonObject { ["id"] = w.Id.IntegerValue, ["name"] = w.Name, ["owner"] = w.Owner, ["open"] = w.IsOpen }).ToArray());
        return new JsonObject { ["workshared"] = true, ["worksets"] = arr };
    }

    [Tool("create_workset", "Creates a user workset (project must be workshared).", Category = "Manage")]
    public static object CreateWorkset(string name)
    {
        var doc = Ctx.RequireProject();
        if (!doc.IsWorkshared) throw new ToolException("Document is not workshared. Use enable_worksharing first.");
        var w = Workset.Create(doc, name);
        return new JsonObject { ["id"] = w.Id.IntegerValue, ["name"] = w.Name };
    }

    [Tool("enable_worksharing", "Enables worksharing on the project (creates default worksets).", Category = "Manage", NoTransaction = true)]
    public static object EnableWorksharing()
    {
        var doc = Ctx.RequireProject();
        if (!doc.IsWorkshared) doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
        return new JsonObject { ["workshared"] = doc.IsWorkshared };
    }

    [Tool("list_phases", "Lists project phases and phase filters.", Category = "Manage", ReadOnly = true)]
    public static object ListPhases()
    {
        var doc = Ctx.RequireProject();
        return new JsonObject
        {
            ["phases"] = new JsonArray(doc.Phases.Cast<Phase>().Select(p => (JsonNode)new JsonObject { ["id"] = p.Id.Value, ["name"] = p.Name }).ToArray()),
            ["phase_filters"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(PhaseFilter)).Select(p => (JsonNode)p.Name).ToArray()),
        };
    }

    [Tool("set_element_phase", """
        Sets the phase an element was created in and/or demolished in (existing/new/demolished workflow for renovations).
        @ids Elements. @created Phase name created in. @demolished Phase name demolished in ('none' to clear).
        """, Category = "Manage")]
    public static object SetElementPhase(long[] ids, string created = null, string demolished = null)
    {
        var doc = Ctx.RequireProject();
        foreach (var e in Find.Elements(doc, ids))
        {
            if (created != null) e.get_Parameter(BuiltInParameter.PHASE_CREATED)?.Set(Find.Named<Phase>(doc, created).Id);
            if (demolished != null)
                e.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED)?.Set(demolished.Equals("none", StringComparison.OrdinalIgnoreCase) ? ElementId.InvalidElementId : Find.Named<Phase>(doc, demolished).Id);
        }
        return new JsonObject { ["updated"] = ids.Length };
    }

    [Tool("list_design_options", "Lists design option sets and options.", Category = "Manage", ReadOnly = true)]
    public static object ListDesignOptions()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(DesignOption)).Cast<DesignOption>().Select(d => (JsonNode)new JsonObject { ["id"] = d.Id.Value, ["name"] = d.Name, ["primary"] = d.IsPrimary }).ToArray());
        return new JsonObject { ["design_options"] = arr };
    }

    // ================================================================= parameters
    [Tool("list_project_parameters", "Lists project/shared parameters with their bound categories and instance/type nature.", Category = "Manage", ReadOnly = true)]
    public static object ListProjectParameters()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray();
        var it = doc.ParameterBindings.ForwardIterator();
        while (it.MoveNext())
        {
            var def = it.Key; var b = it.Current as ElementBinding;
            var o = new JsonObject { ["name"] = def.Name, ["kind"] = b is InstanceBinding ? "instance" : "type" };
            try { o["data_type"] = def.GetDataType().TypeId; } catch { }
            if (b != null) o["categories"] = new JsonArray(b.Categories.Cast<Category>().Select(c => (JsonNode)c.Name).ToArray());
            arr.Add(o);
        }
        return new JsonObject { ["count"] = arr.Count, ["parameters"] = arr };
    }

    [Tool("create_project_parameter", """
        Creates a project parameter (via a shared parameter definition) bound to categories — for BIM data such as 'Fire Rating', 'Cost Code', 'Zone', 'Assembly Code'.
        @name Parameter name. @data_type text | integer | number | length | area | volume | yesno | material ... @categories Category names to bind (e.g. ['Walls','Doors']).
        @instance true = instance parameter, false = type parameter. @group Palette group (identity_data, data, general, dimensions...).
        """, Category = "Manage")]
    public static object CreateProjectParameter(string name, string[] categories, string dataType = "text", bool instance = true, string group = "data")
    {
        var doc = Ctx.RequireProject();
        var app = Ctx.App;
        string old = app.SharedParametersFilename;
        string file = Path.Combine(PluginSettings.Dir, "ArianSharedParameters.txt");
        if (!File.Exists(file)) File.WriteAllText(file, "");
        app.SharedParametersFilename = file;
        try
        {
            var df = app.OpenSharedParameterFile();
            var grp = df.Groups.get_Item("Arian") ?? df.Groups.Create("Arian");
            var def = grp.Definitions.get_Item(name) ?? grp.Definitions.Create(new ExternalDefinitionCreationOptions(name, FamUtil.Spec(dataType)) { Visible = true });
            var cs = app.Create.NewCategorySet();
            foreach (var c in categories) cs.Insert(Find.Category(doc, c));
            Binding binding = instance ? app.Create.NewInstanceBinding(cs) : app.Create.NewTypeBinding(cs);
            bool ok = doc.ParameterBindings.Insert(def, binding, FamUtil.Group(group));
            if (!ok) ok = doc.ParameterBindings.ReInsert(def, binding, FamUtil.Group(group));
            return new JsonObject { ["created"] = ok, ["name"] = name, ["kind"] = instance ? "instance" : "type" };
        }
        finally { app.SharedParametersFilename = old; }
    }

    // ================================================================= units / location
    [Tool("set_project_units", """
        Sets the project display units. @length mm | cm | m | ft | in | ft_in. @area m2 | ft2. @volume m3 | ft3. @angle deg. Decimal places via @length_decimals.
        """, Category = "Manage")]
    public static object SetProjectUnits(string length = "mm", string area = "m2", string volume = "m3", int lengthDecimals = 0)
    {
        var doc = Ctx.RequireProject();
        var u = doc.GetUnits();
        var lu = length.ToLowerInvariant() switch { "cm" => UnitTypeId.Centimeters, "m" => UnitTypeId.Meters, "ft" => UnitTypeId.Feet, "in" => UnitTypeId.Inches, "ft_in" => UnitTypeId.FeetFractionalInches, _ => UnitTypeId.Millimeters };
        var lf = new FormatOptions(lu); lf.Accuracy = Math.Pow(10, -lengthDecimals); u.SetFormatOptions(SpecTypeId.Length, lf);
        u.SetFormatOptions(SpecTypeId.Area, new FormatOptions(area == "ft2" ? UnitTypeId.SquareFeet : UnitTypeId.SquareMeters));
        u.SetFormatOptions(SpecTypeId.Volume, new FormatOptions(volume == "ft3" ? UnitTypeId.CubicFeet : UnitTypeId.CubicMeters));
        doc.SetUnits(u);
        return new JsonObject { ["length"] = length, ["area"] = area, ["volume"] = volume };
    }

    [Tool("set_project_location", "Sets the project's geographic location (latitude/longitude in degrees) and place name.", Category = "Manage")]
    public static object SetProjectLocation(double latitude, double longitude, string placeName = null)
    {
        var doc = Ctx.RequireProject();
        var loc = doc.SiteLocation;
        loc.Latitude = U.Deg(latitude); loc.Longitude = U.Deg(longitude);
        if (!string.IsNullOrWhiteSpace(placeName)) loc.PlaceName = placeName;
        return new JsonObject { ["latitude"] = latitude, ["longitude"] = longitude };
    }

    [Tool("undo", "Undo the last Revit operation(s) via the UI command (posts the Undo command; takes effect right after this call returns).", Category = "Manage", NoTransaction = true, ReadOnly = true)]
    public static object Undo(int steps = 1)
    {
        var id = RevitCommandId.LookupPostableCommandId(PostableCommand.Undo);
        for (int i = 0; i < Math.Clamp(steps, 1, 20); i++) Ctx.UiApp.PostCommand(id);
        return new JsonObject { ["posted_undo"] = steps };
    }

    // ================================================================= quantities
    [Tool("get_quantities", """
        Quantity takeoff computed directly (no schedule needed): counts and summed length/area/volume of a category grouped by type, level and/or material. Works for any discipline (walls, floors, columns, ducts, pipes, doors...).
        @category Category name. @group_by Any of: type, level, material, family (default ['type']). @level Optional level filter.
        """, Category = "Query", ReadOnly = true)]
    public static object GetQuantities(string category, string[] groupBy = null, string level = null)
    {
        var doc = Ctx.RequireProject();
        groupBy ??= new[] { "type" };
        var cat = Find.Category(doc, category);
        Level lv = level != null ? Find.Level(doc, level) : null;
        var groups = new Dictionary<string, (int n, double len, double area, double vol)>();
        foreach (var e in new FilteredElementCollector(doc).OfCategoryId(cat.Id).WhereElementIsNotElementType())
        {
            if (lv != null && Dto.LevelOf(e)?.Id != lv.Id) continue;
            var parts = new List<string>();
            foreach (var g in groupBy)
            {
                switch (g.ToLowerInvariant())
                {
                    case "type": { var t = doc.GetElement(e.GetTypeId()) as ElementType; parts.Add(t == null ? "-" : t.FamilyName + ": " + t.Name); break; }
                    case "family": { var t = doc.GetElement(e.GetTypeId()) as ElementType; parts.Add(t?.FamilyName ?? "-"); break; }
                    case "level": parts.Add(Dto.LevelOf(e)?.Name ?? "-"); break;
                    case "material":
                        {
                            var mids = e.GetMaterialIds(false);
                            parts.Add(mids.Count == 0 ? "-" : string.Join("+", mids.Select(i => doc.GetElement(i)?.Name).OrderBy(x => x)));
                            break;
                        }
                }
            }
            string key = string.Join(" | ", parts);
            double len = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
            double area = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
            double vol = e.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble() ?? 0;
            groups.TryGetValue(key, out var cur);
            groups[key] = (cur.n + 1, cur.len + len, cur.area + area, cur.vol + vol);
        }
        var arr = new JsonArray(groups.OrderBy(g => g.Key).Select(g => (JsonNode)new JsonObject
        {
            ["group"] = g.Key, ["count"] = g.Value.n, ["length_m"] = U.R(U.ToMm(g.Value.len) / 1000, 2), ["area_m2"] = U.R(U.ToM2(g.Value.area), 2), ["volume_m3"] = U.R(U.ToM3(g.Value.vol), 3),
        }).ToArray());
        return new JsonObject { ["category"] = cat.Name, ["total_count"] = groups.Values.Sum(g => g.n), ["groups"] = arr };
    }
}
