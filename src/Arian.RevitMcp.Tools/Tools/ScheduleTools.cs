using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

public sealed class ScheduleFilterSpec
{
    [Desc("Field (column) name to filter on")] public string Field;
    [Desc("equal | not_equal | contains | not_contains | begins_with | greater | greater_or_equal | less | less_or_equal | has_value | has_no_value")] public string Op = "equal";
    [Desc("Value (string or number)")] public JsonNode Value;
}

public sealed class ScheduleSortSpec
{
    public string Field;
    public bool Descending;
    [Desc("Add a header row for each group")] public bool Header;
    [Desc("Add a blank line between groups")] public bool BlankLine;
}

public static class ScheduleTools
{
    [Tool("list_schedules", "Lists schedule views.", Category = "View", ReadOnly = true)]
    public static object ListSchedules()
    {
        var doc = Ctx.RequireProject();
        var arr = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Where(s => !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
            .Select(s => (JsonNode)new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["category"] = doc.GetElement(s.Definition.CategoryId)?.Name }).ToArray());
        return new JsonObject { ["schedules"] = arr };
    }

    [Tool("list_schedulable_fields", "Lists the field names available for a schedule of a category (to use with create_schedule).", Category = "View", ReadOnly = true)]
    public static object ListSchedulableFields(string category)
    {
        var doc = Ctx.RequireProject();
        var cat = Find.Category(doc, category);
        var tmp = ViewSchedule.CreateSchedule(doc, cat.Id);
        try
        {
            var arr = new JsonArray(tmp.Definition.GetSchedulableFields().Select(f => (JsonNode)f.GetName(doc)).Distinct().OrderBy(n => n.ToString()).ToArray());
            return new JsonObject { ["category"] = cat.Name, ["fields"] = arr };
        }
        finally { doc.Delete(tmp.Id); }
    }

    static SchedulableField Fld(Document doc, ViewSchedule s, string name)
    {
        var fs = s.Definition.GetSchedulableFields();
        return fs.FirstOrDefault(f => f.GetName(doc).Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? throw new ToolException($"Field '{name}' is not schedulable in this category. Use list_schedulable_fields.");
    }

    [Tool("create_schedule", """
        Creates a schedule (door schedule, room finish schedule, wall type takeoff, equipment list, quantity takeoff...).
        @category Category (Doors, Windows, Rooms, Walls, Floors, Structural Columns, Mechanical Equipment, Lighting Fixtures ...).
        @name Schedule name. @fields Column names in order (see list_schedulable_fields). Examples: ['Mark','Type','Level','Width','Height','Count'].
        @filters Row filters. @sort Sorting/grouping fields in order. @itemize Itemize every instance (default true; false = group identical rows with Count).
        @totals_fields Fields to total (numeric columns, e.g. ['Area']). @grand_total Show grand total.
        @sheet Optional sheet number/name to place the schedule on.
        """, Category = "View")]
    public static object CreateSchedule(string category, string name, string[] fields, ScheduleFilterSpec[] filters = null, ScheduleSortSpec[] sort = null,
        bool itemize = true, string[] totalsFields = null, bool grandTotal = false, string sheet = null)
    {
        var doc = Ctx.RequireProject();
        var cat = Find.Category(doc, category);
        var s = ViewSchedule.CreateSchedule(doc, cat.Id);
        try { s.Name = name; } catch { s.Name = name + " " + DateTime.Now.ToString("HHmmss"); }
        var def = s.Definition;
        var added = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
        var warnings = new JsonArray();
        foreach (var f in fields)
        {
            try { var sf = def.AddField(Fld(doc, s, f)); added[f] = sf; }
            catch (Exception ex) { warnings.Add($"field '{f}': {ex.Message}"); }
        }
        ScheduleField Get(string n)
        {
            if (added.TryGetValue(n, out var sf)) return sf;
            var hidden = def.AddField(Fld(doc, s, n)); hidden.IsHidden = true; added[n] = hidden; return hidden;
        }
        if (filters != null)
            foreach (var f in filters)
            {
                try
                {
                    var sf = Get(f.Field);
                    var ft = (f.Op ?? "equal").ToLowerInvariant() switch
                    {
                        "not_equal" => ScheduleFilterType.NotEqual, "contains" => ScheduleFilterType.Contains, "not_contains" => ScheduleFilterType.NotContains,
                        "begins_with" => ScheduleFilterType.BeginsWith, "greater" => ScheduleFilterType.GreaterThan, "greater_or_equal" => ScheduleFilterType.GreaterThanOrEqual,
                        "less" => ScheduleFilterType.LessThan, "less_or_equal" => ScheduleFilterType.LessThanOrEqual, "has_value" => ScheduleFilterType.HasValue, "has_no_value" => ScheduleFilterType.HasNoValue,
                        _ => ScheduleFilterType.Equal,
                    };
                    if (ft == ScheduleFilterType.HasValue || ft == ScheduleFilterType.HasNoValue) def.AddFilter(new ScheduleFilter(sf.FieldId, ft));
                    else if (f.Value is JsonValue jv && jv.TryGetValue<double>(out var d) && sf.FieldType != ScheduleFieldType.Instance && false) def.AddFilter(new ScheduleFilter(sf.FieldId, ft, d));
                    else
                    {
                        // numeric?
                        if (f.Value is JsonValue nv && nv.TryGetValue<double>(out var dv) && IsNumeric(doc, sf))
                            def.AddFilter(new ScheduleFilter(sf.FieldId, ft, U.FromApi(dv, SpecOf(doc, sf))));
                        else def.AddFilter(new ScheduleFilter(sf.FieldId, ft, f.Value?.ToString().Trim('"') ?? ""));
                    }
                }
                catch (Exception ex) { warnings.Add($"filter '{f.Field}': {ex.Message}"); }
            }
        if (sort != null)
            foreach (var so in sort)
            {
                try { def.AddSortGroupField(new ScheduleSortGroupField(Get(so.Field).FieldId, so.Descending ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending) { ShowHeader = so.Header, ShowBlankLine = so.BlankLine }); }
                catch (Exception ex) { warnings.Add($"sort '{so.Field}': {ex.Message}"); }
            }
        def.IsItemized = itemize;
        if (totalsFields != null)
            foreach (var t in totalsFields) { try { Get(t).DisplayType = ScheduleFieldDisplayType.Totals; } catch (Exception ex) { warnings.Add($"totals '{t}': {ex.Message}"); } }
        if (grandTotal) { def.ShowGrandTotal = true; def.ShowGrandTotalCount = true; }
        var o = new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["fields"] = def.GetFieldCount() };
        if (!string.IsNullOrWhiteSpace(sheet)) o["placed_on_sheet"] = ViewTools.PlaceViewOnSheet(sheet, s.Name) is JsonObject ? sheet : null;
        if (warnings.Count > 0) o["warnings"] = warnings;
        return o;
    }

    static bool IsNumeric(Document doc, ScheduleField f)
    {
        try { var sp = SpecOf(doc, f); return sp != null && UnitUtils.IsMeasurableSpec(sp); } catch { return false; }
    }

    static ForgeTypeId SpecOf(Document doc, ScheduleField f)
    {
        try { return f.GetSpecTypeId(); } catch { return null; }
    }

    [Tool("get_schedule_data", """
        Reads a schedule's table as rows of text (exactly as displayed in Revit) — to verify counts, check data or feed other analysis.
        @schedule Schedule name/id. @max_rows Max body rows (default 500).
        """, Category = "View", ReadOnly = true)]
    public static object GetScheduleData(string schedule, int maxRows = 500)
    {
        var doc = Ctx.RequireProject();
        var s = Find.View(doc, schedule) as ViewSchedule ?? throw new ToolException("Not a schedule view.");
        var td = s.GetTableData();
        var header = td.GetSectionData(SectionType.Header);
        var body = td.GetSectionData(SectionType.Body);
        var cols = body.NumberOfColumns;
        bool headersInBody = header.NumberOfRows <= 1;
        int startRow = 0;
        var heads = new JsonArray();
        for (int c = 0; c < cols; c++)
        {
            string h = "";
            try { h = headersInBody ? s.GetCellText(SectionType.Body, 0, c) : s.GetCellText(SectionType.Header, header.NumberOfRows - 1, c); } catch { }
            heads.Add(h);
        }
        if (headersInBody) { startRow = 1; if (body.NumberOfRows > 1 && Enumerable.Range(0, cols).All(c => string.IsNullOrEmpty(s.GetCellText(SectionType.Body, 1, c)))) startRow = 2; }
        var rows = new JsonArray();
        for (int r = startRow; r < body.NumberOfRows && rows.Count < maxRows; r++)
        {
            var row = new JsonArray();
            for (int c = 0; c < cols; c++) row.Add(s.GetCellText(SectionType.Body, r, c));
            rows.Add(row);
        }
        return new JsonObject { ["name"] = s.Name, ["columns"] = heads, ["row_count"] = rows.Count, ["rows"] = rows };
    }

    [Tool("delete_schedule", "Deletes a schedule view.", Category = "View", Destructive = true)]
    public static object DeleteSchedule(string schedule)
    {
        var doc = Ctx.RequireProject();
        var s = Find.View(doc, schedule);
        doc.Delete(s.Id);
        return new JsonObject { ["deleted"] = schedule };
    }
}
