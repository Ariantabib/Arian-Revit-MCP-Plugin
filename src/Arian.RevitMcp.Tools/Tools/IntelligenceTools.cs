using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

/// <summary>Phase-8: brief → program, cost estimation (5D), model report (HTML), envelope/energy summary, sun study, BCF export.</summary>
public static class IntelligenceTools
{
    static double R(double v, int d = 1) => Math.Round(v, d);
    static string Inv(double v, string f = "0.##") => v.ToString(f, CultureInfo.InvariantCulture);

    // ====================================================================== brief → program
    [Tool("generate_program_from_brief", """
        Turns a short residential brief into a physical PROGRAM: list of rooms with target areas (m²), level, zone (public/private/service) and adjacency hints — ready to feed create_plan_from_rooms or a manual design.
        @bedrooms Number of bedrooms (default 3). @total_area_m2 Target gross area (default: from bedrooms). @floors 1 or 2 (default 2). @master_suite Add an ensuite master (default true). @extras e.g. ['study','guest','laundry','garage','storage','prayer','roof_terrace'].
        @style Short free text echoed back (e.g. 'modern villa'). Areas follow typical Iranian/international residential norms; adjust to the client.
        """, Category = "Architecture", ReadOnly = true, NoDocument = true)]
    public static object GenerateProgramFromBrief(int bedrooms = 3, double? totalAreaM2 = null, int floors = 2, bool masterSuite = true, string[] extras = null, string style = null)
    {
        extras ??= Array.Empty<string>();
        bool Has(string k) => extras.Any(e => e.Contains(k, StringComparison.OrdinalIgnoreCase));
        var rooms = new List<(string name, double area, string level, string zone, string adj)>();
        string up = floors >= 2 ? "First Floor" : "Ground Floor";
        rooms.Add(("Entrance Hall", 9, "Ground Floor", "public", "street, living, stairs"));
        rooms.Add(("Living Room", 32, "Ground Floor", "public", "dining, terrace, entrance"));
        rooms.Add(("Dining", 16, "Ground Floor", "public", "living, kitchen"));
        rooms.Add(("Kitchen", 15, "Ground Floor", "service", "dining, laundry"));
        rooms.Add(("Guest WC", 3, "Ground Floor", "service", "entrance"));
        rooms.Add(("Staircase", 8, "Ground Floor", "circulation", "entrance, first floor"));
        if (Has("laundry")) rooms.Add(("Laundry", 6, "Ground Floor", "service", "kitchen"));
        if (Has("study")) rooms.Add(("Study", 12, "Ground Floor", "private", "entrance"));
        if (Has("guest")) rooms.Add(("Guest Bedroom", 14, "Ground Floor", "private", "guest WC"));
        if (Has("garage")) rooms.Add(("Garage", 28, "Ground Floor", "service", "entrance"));
        if (Has("storage")) rooms.Add(("Storage", 6, "Ground Floor", "service", "garage/kitchen"));
        if (Has("prayer")) rooms.Add(("Prayer Room", 6, "Ground Floor", "private", "living"));
        int beds = Math.Max(1, bedrooms);
        if (masterSuite) { rooms.Add(("Master Bedroom", 22, up, "private", "ensuite, dressing, balcony")); rooms.Add(("Master Bath", 8, up, "service", "master bedroom")); rooms.Add(("Dressing", 6, up, "private", "master bedroom")); beds--; }
        for (int i = 1; i <= beds; i++) rooms.Add(($"Bedroom {i}", 15, up, "private", "family bath, corridor"));
        rooms.Add(("Family Bath", 6, up, "service", "bedrooms"));
        rooms.Add(("Upper Landing", 10, up, "circulation", "stairs, bedrooms"));
        if (floors >= 2 && Has("roof_terrace")) rooms.Add(("Roof Terrace", 30, "Roof", "outdoor", "upper landing"));
        double net = rooms.Sum(r => r.area);
        double gross = net * 1.15;   // walls + circulation not listed
        double scale = totalAreaM2.HasValue ? totalAreaM2.Value / gross : 1.0;
        var arr = new JsonArray(rooms.Select(r => (JsonNode)new JsonObject { ["name"] = r.name, ["target_area_m2"] = R(r.area * scale), ["level"] = r.level, ["zone"] = r.zone, ["adjacent_to"] = r.adj }).ToArray());
        return new JsonObject
        {
            ["style"] = style, ["bedrooms"] = bedrooms, ["floors"] = floors, ["net_area_m2"] = R(net * scale), ["estimated_gross_area_m2"] = R(gross * scale),
            ["rooms"] = arr,
            ["design_hints"] = new JsonArray("Zone public rooms to the south/street side with the best views; keep wet rooms stacked vertically for plumbing.", "Orient living and master bedroom to south glazing, shade with overhangs.", "Keep the staircase central to shorten corridors.", "Typical clear ceiling height 2.7 m (min 2.4 m).")
        };
    }

    // ====================================================================== 5D cost
    sealed record Price(string Match, string Unit, double Rate);

    static readonly Price[] DefaultPrices =
    {
        new("Brick", "m2", 55), new("Masonry", "m2", 50), new("Concrete", "m3", 140), new("Generic", "m2", 40), new("Curtain", "m2", 320), new("Storefront", "m2", 280),
        new("Floor", "m2", 90), new("Roof", "m2", 110), new("Ceiling", "m2", 35), new("Door", "count", 420), new("Window", "count", 380), new("Fixed", "count", 300),
        new("Stair", "count", 6500), new("Railing", "m", 180), new("Column", "count", 650), new("Beam", "m", 160), new("Footing", "count", 900),
    };

    [Tool("estimate_cost", """
        5D quick cost estimate from the model quantities: walls, floors, roofs, ceilings, doors, windows, curtain walls, stairs, railings, columns, beams, footings. Prices come from @price_list (match = text found in the family/type name, unit = m2 | m3 | m | count, rate per unit) or built-in sample rates (USD-like, indicative only).
        @price_list [{match, unit, rate}] (first matching rule wins). @categories Categories to price (default: the list above). @currency Label. @csv_path Optional CSV output.
        """, Category = "Query", ReadOnly = true)]
    public static object EstimateCost(JsonArray priceList = null, string[] categories = null, string currency = "USD", string csvPath = null)
    {
        var rules = priceList?.Select(p => new Price(p!["match"]!.GetValue<string>(), p["unit"]?.GetValue<string>() ?? "m2", p["rate"]!.GetValue<double>())).ToList() ?? DefaultPrices.ToList();
        categories ??= new[] { "Walls", "Floors", "Roofs", "Ceilings", "Doors", "Windows", "Stairs", "Railings", "Structural Columns", "Structural Framing", "Structural Foundations", "Curtain Wall Panels" };
        var lines = new JsonArray(); double total = 0; var unpriced = new JsonArray();
        var csv = new StringBuilder("Category,Type,Count,Length m,Area m2,Volume m3,Unit,Rate,Cost\n");
        foreach (var cat in categories)
        {
            JsonObject q;
            try { q = (JsonObject)ManageTools.GetQuantities(cat); } catch { continue; }
            foreach (var g in q["groups"]!.AsArray())
            {
                string name = g!["group"]!.GetValue<string>(); int n = g["count"]!.GetValue<int>();
                double len = g["length_m"]!.GetValue<double>(), area = g["area_m2"]!.GetValue<double>(), vol = g["volume_m3"]!.GetValue<double>();
                var rule = rules.FirstOrDefault(r => name.Contains(r.Match, StringComparison.OrdinalIgnoreCase)) ?? rules.FirstOrDefault(r => cat.Contains(r.Match, StringComparison.OrdinalIgnoreCase));
                if (rule == null) { unpriced.Add(cat + ": " + name); continue; }
                double qty = rule.Unit switch { "m3" => vol, "m" => len, "count" => n, _ => area };
                if (qty <= 0 && rule.Unit != "count") { qty = rule.Unit == "m3" ? vol : area; }
                double cost = qty * rule.Rate; total += cost;
                lines.Add(new JsonObject { ["category"] = cat, ["type"] = name, ["count"] = n, ["quantity"] = R(qty, 2), ["unit"] = rule.Unit, ["rate"] = rule.Rate, ["cost"] = R(cost, 0) });
                csv.AppendLine($"{cat},\"{name.Replace("\"", "'")}\",{n},{Inv(len)},{Inv(area)},{Inv(vol)},{rule.Unit},{Inv(rule.Rate)},{Inv(cost, "0")}");
            }
        }
        double floorArea = 0;
        foreach (var f in new FilteredElementCollector(Ctx.RequireProject()).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()) floorArea += U.ToM2(((Autodesk.Revit.DB.SpatialElement)f).Area);
        if (csvPath != null) { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(csvPath))); System.IO.File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true)); }
        return new JsonObject { ["currency"] = currency, ["total"] = R(total, 0), ["room_area_m2"] = R(floorArea), ["cost_per_m2"] = floorArea > 0 ? R(total / floorArea, 0) : null, ["lines"] = lines, ["unpriced"] = unpriced, ["note"] = "Indicative only — replace price_list with local unit rates.", ["csv"] = csvPath };
    }

    // ====================================================================== envelope / energy
    [Tool("analyze_envelope", "Building-envelope summary for energy/daylight discussions: gross exterior wall area by orientation (N/E/S/W relative to project north), window area and window-to-wall ratio per orientation, roof area, heated floor area (rooms), volume.", Category = "Query", ReadOnly = true)]
    public static object AnalyzeEnvelope()
    {
        var doc = Ctx.RequireProject();
        var wall = new double[4]; var win = new double[4];
        string[] names = { "North", "East", "South", "West" };
        int Dir(XYZ v) { double a = Math.Atan2(v.X, v.Y) * 180 / Math.PI; if (a < 0) a += 360; return (int)Math.Round(a / 90) % 4; }
        foreach (Wall w in new FilteredElementCollector(doc).OfClass(typeof(Wall)))
        {
            if (w.WallType.Function != WallFunction.Exterior || w.Orientation == null) continue;
            wall[Dir(w.Orientation)] += U.ToM2(w.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0);
        }
        foreach (FamilyInstance f in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType())
        {
            if (f.Host is not Wall hw || hw.WallType.Function != WallFunction.Exterior) continue;
            var t = doc.GetElement(f.GetTypeId());
            double w = (t?.LookupParameter("Width")?.AsDouble() ?? t?.LookupParameter("Rough Width")?.AsDouble() ?? 0), h = (t?.LookupParameter("Height")?.AsDouble() ?? t?.LookupParameter("Rough Height")?.AsDouble() ?? 0);
            win[Dir(hw.Orientation * (f.FacingFlipped ? -1 : 1))] += U.ToM2(w * h);
        }
        double roof = 0; foreach (var r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType()) roof += U.ToM2(r.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0);
        double rooms = 0, vol = 0; foreach (var r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Autodesk.Revit.DB.Architecture.Room>()) { rooms += U.ToM2(r.Area); vol += U.ToM3(r.Volume); }
        var arr = new JsonArray(Enumerable.Range(0, 4).Select(i => (JsonNode)new JsonObject { ["orientation"] = names[i], ["exterior_wall_m2"] = R(wall[i]), ["window_m2"] = R(win[i]), ["wwr_percent"] = wall[i] + win[i] > 0 ? R(win[i] / (wall[i] + win[i]) * 100) : 0 }).ToArray());
        double tw = wall.Sum(), tg = win.Sum();
        return new JsonObject { ["orientations"] = arr, ["total_exterior_wall_m2"] = R(tw), ["total_window_m2"] = R(tg), ["overall_wwr_percent"] = tw + tg > 0 ? R(tg / (tw + tg) * 100) : 0, ["roof_m2"] = R(roof), ["room_floor_area_m2"] = R(rooms), ["room_volume_m3"] = R(vol), ["note"] = "Orientation is relative to project north (set true north with set_project_location for real compass directions)." };
    }

    // ====================================================================== sun study
    [Tool("sun_study", """
        Renders a sun/shadow study of a 3D view: one image per time of day on a given date, with shadows on (project location is used for the sun position).
        @view 3D view name (default: 'Arian Snapshot' or the first 3D view). @date ISO date (default 21 June). @hours e.g. [9,12,15,18]. @folder Output folder (default Documents/ArianRenders/sun). @width_px Image width.
        """, Category = "View", NoTransaction = true)]
    public static object SunStudy(string view = null, string date = null, double[] hours = null, string folder = null, int widthPx = 1600)
    {
        var doc = Ctx.RequireProject();
        var v3 = (view != null ? Find.View(doc, view) : new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate)) as View3D ?? throw new ToolException("A 3D view is required.");
        hours ??= new double[] { 9, 12, 15, 18 };
        var d0 = date == null ? new DateTime(DateTime.Now.Year, 6, 21) : DateTime.Parse(date, CultureInfo.InvariantCulture);
        folder ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianRenders", "sun");
        System.IO.Directory.CreateDirectory(folder);
        var files = new JsonArray(); var notes = new JsonArray();
        using (var t = new Transaction(doc, "Arian MCP: sun study setup"))
        {
            t.Start();
            v3.DisplayStyle = DisplayStyle.Realistic;
            try { var p = v3.LookupParameter("Cast Shadows"); if (p != null && !p.IsReadOnly) p.Set(1); } catch (Exception ex) { notes.Add("shadows: " + ex.Message); }
            t.Commit();
        }
        foreach (var h in hours)
        {
            Activity.Check();
            var when = d0.Date.AddHours(h);
            using (var t = new Transaction(doc, "Arian MCP: sun time"))
            {
                t.Start();
                try
                {
                    var s = v3.SunAndShadowSettings;
                    s.SunAndShadowType = SunAndShadowType.StillImage;
                    s.StartDateAndTime = DateTime.SpecifyKind(when, DateTimeKind.Local);
                }
                catch (Exception ex) { notes.Add("sun settings: " + ex.Message); }
                t.Commit();
            }
            string stem = System.IO.Path.Combine(folder, $"sun_{when:yyyyMMdd}_{(int)h:00}h");
            var opt = new ImageExportOptions { ExportRange = ExportRange.SetOfViews, FilePath = stem, HLRandWFViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150, ZoomType = ZoomFitType.FitToPage, PixelSize = widthPx };
            opt.SetViewsAndSheets(new List<ElementId> { v3.Id });
            doc.ExportImage(opt);
            var f = System.IO.Directory.GetFiles(folder, System.IO.Path.GetFileName(stem) + "*").OrderByDescending(System.IO.File.GetLastWriteTime).FirstOrDefault();
            files.Add(new JsonObject { ["time"] = when.ToString("yyyy-MM-dd HH:mm"), ["file"] = f });
        }
        return new JsonObject { ["view"] = v3.Name, ["images"] = files, ["notes"] = notes };
    }

    // ====================================================================== BCF
    [Tool("export_bcf_clashes", """
        Detects clashes between two categories and writes them as a BCF 2.1 file (issues for Navisworks, Solibri, BIMcollab, Revizto...). Each clash becomes a topic with both elements' ids/names.
        @category_a @category_b Categories e.g. 'Ducts' / 'Structural Framing'. @path Output .bcfzip path. @limit Max topics (default 200).
        """, Category = "Manage", ReadOnly = true)]
    public static object ExportBcfClashes(string categoryA, string categoryB, string path, int limit = 200)
    {
        var doc = Ctx.RequireProject();
        var ca = Find.Category(doc, categoryA); var cb = Find.Category(doc, categoryB);
        var topics = new List<(Element a, Element b)>(); var seen = new HashSet<(long, long)>();
        foreach (var a in new FilteredElementCollector(doc).OfCategoryId(ca.Id).WhereElementIsNotElementType().ToList())
            foreach (var b in new FilteredElementCollector(doc).OfCategoryId(cb.Id).WhereElementIsNotElementType().WherePasses(new ElementIntersectsElementFilter(a)))
            {
                if (a.Id == b.Id) continue;
                var key = a.Id.Value < b.Id.Value ? (a.Id.Value, b.Id.Value) : (b.Id.Value, a.Id.Value);
                if (seen.Add(key) && topics.Count < limit) topics.Add((a, b));
            }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string txt) { var e = zip.CreateEntry(name); using var w = new System.IO.StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(txt); }
        Add("bcf.version", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Version VersionId=\"2.1\"><DetailedVersion>2.1</DetailedVersion></Version>");
        string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        foreach (var (a, b) in topics)
        {
            string guid = Guid.NewGuid().ToString();
            string na = $"{a.Category?.Name} '{a.Name}' (id {a.Id.Value})", nb = $"{b.Category?.Name} '{b.Name}' (id {b.Id.Value})";
            Add($"{guid}/markup.bcf", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Markup><Header><File IfcProject=\"{X(doc.Title)}\" Filename=\"{X(doc.Title)}.rvt\"/></Header><Topic Guid=\"{guid}\" TopicType=\"Clash\" TopicStatus=\"Open\"><Title>{X($"Clash: {a.Category?.Name} vs {b.Category?.Name}")}</Title><CreationDate>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</CreationDate><CreationAuthor>arian-revit-mcp</CreationAuthor><Description>{X(na + " intersects " + nb)}</Description></Topic></Markup>");
        }
        return new JsonObject { ["path"] = path, ["topics"] = topics.Count };
    }

    // ====================================================================== HTML report
    [Tool("generate_model_report", """
        Writes a self-contained HTML report of the model: project info, levels, room/area table, quantities by category, building-rule check, warnings, envelope summary, estimated cost and images of chosen views embedded. Opens anywhere (no internet needed).
        @path Output .html. @title Report title. @views View names to render and embed (default: 3D 'Arian Snapshot' if present). @include_cost Include the sample cost estimate (default false). @rules_profile Profile for check_building_rules (default iran_residential).
        """, Category = "Manage", NoTransaction = true)]
    public static object GenerateModelReport(string path, string title = null, string[] views = null, bool includeCost = false, string rulesProfile = "iran_residential")
    {
        var doc = Ctx.RequireProject();
        string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=en><meta charset=utf-8><title>").Append(H(title ?? doc.Title)).Append("</title><style>body{font-family:Segoe UI,Arial,sans-serif;max-width:1100px;margin:30px auto;padding:0 16px;color:#1d2330}h1{font-size:28px}h2{margin-top:34px;border-bottom:2px solid #2b6cd9;padding-bottom:4px}table{border-collapse:collapse;width:100%;margin:8px 0}td,th{border:1px solid #d5dae3;padding:5px 9px;text-align:left;font-size:13px}th{background:#eef2fa}.bad{color:#b32626}.ok{color:#1b7f46}img{max-width:100%;border:1px solid #d5dae3;margin:8px 0;border-radius:6px}.kv{display:grid;grid-template-columns:210px 1fr;gap:3px 12px}</style>");
        sb.Append("<h1>").Append(H(title ?? doc.Title)).Append("</h1><p>Generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(" by Arian Revit MCP Plugin</p>");
        var pi = doc.ProjectInformation;
        sb.Append("<h2>Project</h2><div class=kv>");
        foreach (var (k, v) in new[] { ("Name", pi.Name), ("Number", pi.Number), ("Client", pi.ClientName), ("Address", pi.Address), ("Status", pi.Status) }) sb.Append($"<b>{k}</b><span>{H(v)}</span>");
        sb.Append("</div>");
        // levels
        sb.Append("<h2>Levels</h2><table><tr><th>Level</th><th>Elevation (mm)</th></tr>");
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation)) sb.Append($"<tr><td>{H(l.Name)}</td><td>{U.ToMm(l.Elevation):0}</td></tr>");
        sb.Append("</table>");
        // rooms
        var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Autodesk.Revit.DB.Architecture.Room>().Where(r => r.Area > 0).OrderBy(r => r.Level?.Elevation).ThenBy(r => r.Number).ToList();
        sb.Append("<h2>Rooms / areas</h2><table><tr><th>Level</th><th>No.</th><th>Name</th><th>Area (m²)</th></tr>");
        foreach (var r in rooms) sb.Append($"<tr><td>{H(r.Level?.Name)}</td><td>{H(r.Number)}</td><td>{H(r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString())}</td><td>{U.ToM2(r.Area):0.0}</td></tr>");
        sb.Append($"<tr><th colspan=3>Total</th><th>{rooms.Sum(r => U.ToM2(r.Area)):0.0}</th></tr></table>");
        // quantities
        sb.Append("<h2>Quantities</h2><table><tr><th>Category</th><th>Type</th><th>Count</th><th>Length (m)</th><th>Area (m²)</th><th>Volume (m³)</th></tr>");
        foreach (var cat in new[] { "Walls", "Floors", "Roofs", "Ceilings", "Doors", "Windows", "Stairs", "Railings", "Structural Columns", "Structural Framing", "Structural Foundations" })
        {
            try
            {
                var q = (JsonObject)ManageTools.GetQuantities(cat);
                foreach (var g in q["groups"]!.AsArray()) sb.Append($"<tr><td>{cat}</td><td>{H(g!["group"]!.GetValue<string>())}</td><td>{g["count"]}</td><td>{g["length_m"]}</td><td>{g["area_m2"]}</td><td>{g["volume_m3"]}</td></tr>");
            }
            catch { }
        }
        sb.Append("</table>");
        // rules
        try
        {
            var rr = (JsonObject)DocDepthTools.CheckBuildingRules(rulesProfile);
            sb.Append($"<h2>Building-rule check ({H(rulesProfile)})</h2><p>{rr["checks_run"]} checks, <b class='{(rr["violations"]!.GetValue<int>() == 0 ? "ok" : "bad")}'>{rr["violations"]} violation(s)</b>. WWR {rr["window_to_wall_percent"]}%.</p>");
            if (rr["items"] is JsonArray its && its.Count > 0) { sb.Append("<table><tr><th>Rule</th><th>Message</th></tr>"); foreach (var i in its) sb.Append($"<tr><td>{H(i!["rule"]?.GetValue<string>())}</td><td>{H(i["message"]?.GetValue<string>())}</td></tr>"); sb.Append("</table>"); }
            sb.Append($"<p><i>{H(rr["note"]?.GetValue<string>())}</i></p>");
        }
        catch (Exception ex) { sb.Append("<p>Rule check failed: " + H(ex.Message) + "</p>"); }
        // envelope
        try
        {
            var env = (JsonObject)AnalyzeEnvelope();
            sb.Append("<h2>Envelope</h2><table><tr><th>Orientation</th><th>Exterior wall m²</th><th>Window m²</th><th>WWR %</th></tr>");
            foreach (var o in env["orientations"]!.AsArray()) sb.Append($"<tr><td>{o!["orientation"]}</td><td>{o["exterior_wall_m2"]}</td><td>{o["window_m2"]}</td><td>{o["wwr_percent"]}</td></tr>");
            sb.Append($"</table><p>Roof {env["roof_m2"]} m² · Room floor area {env["room_floor_area_m2"]} m² · Volume {env["room_volume_m3"]} m³ · Overall WWR {env["overall_wwr_percent"]}%</p>");
        }
        catch { }
        if (includeCost)
        {
            try
            {
                var c = (JsonObject)EstimateCost();
                sb.Append($"<h2>Cost estimate (indicative)</h2><p><b>{c["total"]} {c["currency"]}</b> — {c["cost_per_m2"]} per m²</p><table><tr><th>Category</th><th>Type</th><th>Qty</th><th>Unit</th><th>Cost</th></tr>");
                foreach (var l in c["lines"]!.AsArray()) sb.Append($"<tr><td>{l!["category"]}</td><td>{H(l["type"]?.GetValue<string>())}</td><td>{l["quantity"]}</td><td>{l["unit"]}</td><td>{l["cost"]}</td></tr>");
                sb.Append("</table>");
            }
            catch { }
        }
        // warnings
        var warns = doc.GetWarnings();
        sb.Append($"<h2>Warnings</h2><p>{warns.Count} warning(s) in the model.</p>");
        foreach (var g in warns.GroupBy(w => w.GetDescriptionText()).OrderByDescending(g => g.Count()).Take(10)) sb.Append($"<p>{g.Count()} × {H(g.Key)}</p>");
        // images
        var imgs = new List<(string name, string b64)>();
        var vlist = views != null && views.Length > 0 ? views.Select(v => Find.View(doc, v)).ToList() : new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(x => !x.IsTemplate && x.Name == "Arian Snapshot").Cast<View>().ToList();
        foreach (var v in vlist)
        {
            try
            {
                string stem = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arian-report-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var opt = new ImageExportOptions { ExportRange = ExportRange.SetOfViews, FilePath = stem, HLRandWFViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150, ZoomType = ZoomFitType.FitToPage, PixelSize = 1600 };
                opt.SetViewsAndSheets(new List<ElementId> { v.Id }); doc.ExportImage(opt);
                var f = System.IO.Directory.GetFiles(System.IO.Path.GetTempPath(), System.IO.Path.GetFileName(stem) + "*").FirstOrDefault();
                if (f != null) { imgs.Add((v.Name, Convert.ToBase64String(System.IO.File.ReadAllBytes(f)))); try { System.IO.File.Delete(f); } catch { } }
            }
            catch { }
        }
        if (imgs.Count > 0) { sb.Append("<h2>Views</h2>"); foreach (var (n, b) in imgs) sb.Append($"<h3>{H(n)}</h3><img alt='{H(n)}' src='data:image/png;base64,{b}'>"); }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
        System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return new JsonObject { ["path"] = path, ["bytes"] = new System.IO.FileInfo(path).Length, ["images"] = imgs.Count, ["rooms"] = rooms.Count };
    }
}
