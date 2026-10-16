using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Tools;

public static class DocTools
{
    static readonly string TemplateRoot = @"C:\ProgramData\Autodesk\RVT 2027\Templates\English";

    static readonly Dictionary<string, string> TemplateAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["architectural"] = "DefaultMetric.rte",
        ["architecture"] = "DefaultMetric.rte",
        ["default"] = "DefaultMetric.rte",
        ["multi"] = "Default-Multi-Discipline_Metric.rte",
        ["multi_discipline"] = "Default-Multi-Discipline_Metric.rte",
        ["structural"] = "Structural Analysis-DefaultMetric.rte",
        ["structure"] = "Structural Analysis-DefaultMetric.rte",
        ["mechanical"] = "Mechanical-Default_Metric.rte",
        ["electrical"] = "Electrical-Default_Metric.rte",
        ["plumbing"] = "Plumbing-Default_Metric.rte",
        ["systems"] = "Systems-Default_Metric.rte",
        ["construction"] = "Construction-DefaultMetric.rte",
        ["precast"] = "Precast Detailing-DefaultMetric.rte",
        ["imperial"] = @"..\English-Imperial\default.rte",
    };

    [Tool("list_templates", """
        Lists available project templates (.rte): Autodesk metric templates plus any .rte in extra_dirs. Use the returned 'path' or a short alias (architectural, multi, structural, mechanical, electrical, plumbing, systems, construction) with create_project.
        @extra_dirs Additional folders to scan for .rte files.
        """, Category = "Session", ReadOnly = true, NoDocument = true)]
    public static object ListTemplates(string[] extraDirs = null)
    {
        var list = new JsonArray();
        var dirs = new List<string> { TemplateRoot, @"C:\ProgramData\Autodesk\RVT 2027\Templates" };
        dirs.Add(@"D:\Revit Files");
        if (extraDirs != null) dirs.AddRange(extraDirs);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs.Where(Directory.Exists))
        {
            foreach (var f in Directory.EnumerateFiles(d, "*.rte", d == TemplateRoot ? SearchOption.TopDirectoryOnly : SearchOption.TopDirectoryOnly))
                if (seen.Add(f)) list.Add(new JsonObject { ["name"] = Path.GetFileNameWithoutExtension(f), ["path"] = f });
        }
        try { var def = Ctx.App.DefaultProjectTemplate; if (!string.IsNullOrEmpty(def)) list.Add(new JsonObject { ["name"] = "(user default template)", ["path"] = def }); } catch { }
        return new JsonObject { ["aliases"] = new JsonArray(TemplateAliases.Keys.Select(k => (JsonNode)k).ToArray()), ["templates"] = list };
    }

    [Tool("create_project", """
        Creates a new Revit project from a template, saves it, and opens it as the active document. Returns the document title. If a project with the same file is already open it is closed (without saving) and replaced.
        @template Template alias (architectural, multi, structural, mechanical, electrical, plumbing, systems, construction), part of a template name from list_templates (e.g. 'arian' picks the user's own "Arian Architectural Template"), or a full path to an .rte file. Default: architectural metric. If the user owns a custom template, PREFER it: it contains their wall/floor types, families and standards.
        @save_path Where to save the .rvt (folder or full path). Default: Documents\ArianProjects\<name>.rvt
        @name File name (without extension) when save_path is a folder or empty.
        """, Category = "Session", NoDocument = true, NoTransaction = true)]
    public static object CreateProject(string template = "architectural", string savePath = null, string name = null)
    {
        string tpl = template;
        if (string.IsNullOrWhiteSpace(tpl)) tpl = "architectural";
        if (TemplateAliases.TryGetValue(tpl, out var alias)) tpl = Path.GetFullPath(Path.Combine(TemplateRoot, alias));
        if (!File.Exists(tpl))
        {
            // match by (part of) the template name among all known templates, preferring the user's own (e.g. template='arian')
            var all = new List<string>();
            foreach (var dd in new[] { @"D:\Revit Files", TemplateRoot, @"C:\ProgramData\Autodesk\RVT 2027\Templates" }.Where(Directory.Exists))
                all.AddRange(Directory.EnumerateFiles(dd, "*.rte", SearchOption.TopDirectoryOnly));
            var hit = all.Where(f => Path.GetFileNameWithoutExtension(f).Contains(tpl, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => Path.GetFileNameWithoutExtension(f)).FirstOrDefault();   // highest version name first
            if (hit != null) tpl = hit;
        }
        if (!File.Exists(tpl)) throw new ToolException($"Template not found: {tpl}. Use list_templates.");

        string file;
        if (!string.IsNullOrWhiteSpace(savePath) && savePath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) file = savePath;
        else
        {
            var dir = string.IsNullOrWhiteSpace(savePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianProjects") : savePath;
            Directory.CreateDirectory(dir);
            string baseName = string.IsNullOrWhiteSpace(name) ? "ArianProject_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") : name;
            file = Path.Combine(dir, baseName + ".rvt");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file));

        var d = Ctx.App.NewProjectDocument(tpl);
        // a project with the same path may still be open (e.g. re-running a scenario): Revit cannot overwrite an open file
        var existing = Ctx.App.Documents.Cast<Document>().FirstOrDefault(x => !x.IsFamilyDocument && string.Equals(x.PathName, file, StringComparison.OrdinalIgnoreCase));
        UIDocument ui;
        if (existing == null)
        {
            d.SaveAs(file, new SaveAsOptions { OverwriteExistingFile = true });
            d.Close(false);
            ui = Ctx.UiApp.OpenAndActivateDocument(file);
        }
        else
        {
            var tmp = Path.Combine(Path.GetTempPath(), "arian-new-" + Guid.NewGuid().ToString("N") + ".rvt");
            d.SaveAs(tmp, new SaveAsOptions { OverwriteExistingFile = true });
            d.Close(false);
            ui = Ctx.UiApp.OpenAndActivateDocument(tmp);      // becomes active, so the old (possibly active) one can be closed
            existing.Close(false);
            ui.Document.SaveAs(file, new SaveAsOptions { OverwriteExistingFile = true });
        }
        Ctx.Doc = ui.Document;
        return new JsonObject { ["title"] = ui.Document.Title, ["path"] = file, ["template"] = tpl, ["levels"] = new FilteredElementCollector(ui.Document).OfClass(typeof(Level)).GetElementCount() };
    }

    [Tool("open_document", """
        Opens a Revit project (.rvt) or family (.rfa) file and activates it.
        @path Full file path.
        @activate Make it the active (visible) document.
        @detach For workshared central files: detach from central (preserve worksets).
        @audit Audit on open.
        """, Category = "Session", NoDocument = true, NoTransaction = true)]
    public static object OpenDocument(string path, bool activate = true, bool detach = false, bool audit = false)
    {
        if (!File.Exists(path)) throw new ToolException("File not found: " + path);
        var mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        var opts = new OpenOptions { Audit = audit };
        if (detach) opts.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
        Document d;
        if (activate) { var ui = Ctx.UiApp.OpenAndActivateDocument(mp, opts, false); d = ui.Document; }
        else d = Ctx.App.OpenDocumentFile(mp, opts);
        return new JsonObject { ["title"] = d.Title, ["kind"] = d.IsFamilyDocument ? "family" : "project", ["path"] = d.PathName };
    }

    [Tool("activate_document", "Brings an already-open document to the front (makes it the active UI document).",
        Category = "Session", NoDocument = true, NoTransaction = true)]
    public static object ActivateDocument(string document)
    {
        var d = Ctx.FindDocument(document);
        if (d == null) throw new ToolException("Document not found");
        if (!string.IsNullOrEmpty(d.PathName)) Ctx.UiApp.OpenAndActivateDocument(d.PathName);
        else throw new ToolException("This document has never been saved, so it can't be re-activated by path. Save it first.");
        return new JsonObject { ["active"] = d.Title };
    }

    [Tool("save_document", "Saves the document in place. For never-saved documents use save_document_as.",
        Category = "Session", NoTransaction = true)]
    public static object SaveDocument()
    {
        var d = Ctx.RequireDoc();
        if (string.IsNullOrEmpty(d.PathName)) throw new ToolException("This document has no file path yet. Use save_document_as.");
        d.Save();
        return new JsonObject { ["saved"] = d.PathName };
    }

    [Tool("save_document_as", """
        Saves the document to a new path (.rvt for projects, .rfa for families).
        @path Full destination path.
        @overwrite Overwrite if the file exists.
        """, Category = "Session", NoTransaction = true)]
    public static object SaveDocumentAs(string path, bool overwrite = true)
    {
        var d = Ctx.RequireDoc();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        d.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = overwrite });
        return new JsonObject { ["saved"] = path, ["title"] = d.Title };
    }

    [Tool("close_document", """
        Closes a document. The active document cannot be closed through the API if it is the only open one with views; in that case the call reports it.
        @save Save before closing.
        """, Category = "Session", NoTransaction = true, Destructive = true)]
    public static object CloseDocument(bool save = false)
    {
        var d = Ctx.RequireDoc();
        string title = d.Title;
        bool ok = d.Close(save);
        return new JsonObject { ["closed"] = ok, ["title"] = title };
    }

    [Tool("sync_with_central", """
        Synchronise a workshared document with central.
        @comment Sync comment.
        @relinquish Relinquish all ownership after syncing.
        """, Category = "Session", NoTransaction = true)]
    public static object SyncWithCentral(string comment = "Arian MCP sync", bool relinquish = true)
    {
        var d = Ctx.RequireProject();
        if (!d.IsWorkshared) throw new ToolException("The document is not workshared.");
        var t = new TransactWithCentralOptions();
        var s = new SynchronizeWithCentralOptions { Comment = comment };
        s.SetRelinquishOptions(new RelinquishOptions(relinquish) { });
        d.SynchronizeWithCentral(t, s);
        return new JsonObject { ["synced"] = true };
    }

    [Tool("get_project_info", "Project information (name, number, client, address, status, issue date, author, building name, organization) plus site location, true-north and units.",
        Category = "Session", ReadOnly = true)]
    public static object GetProjectInfo()
    {
        var d = Ctx.RequireProject();
        var pi = d.ProjectInformation;
        var o = new JsonObject
        {
            ["title"] = d.Title,
            ["path"] = d.PathName,
            ["project_information"] = Par.Values(pi, true),
        };
        try
        {
            var loc = d.SiteLocation;
            o["site"] = new JsonObject
            {
                ["place"] = loc.PlaceName,
                ["latitude_deg"] = U.R(U.ToDeg(loc.Latitude), 6),
                ["longitude_deg"] = U.R(U.ToDeg(loc.Longitude), 6),
                ["time_zone"] = loc.TimeZone,
            };
        }
        catch { }
        try
        {
            var fo = d.GetUnits();
            o["length_display_unit"] = fo.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId;
            o["area_display_unit"] = fo.GetFormatOptions(SpecTypeId.Area).GetUnitTypeId().TypeId;
        }
        catch { }
        return o;
    }

    [Tool("set_project_info", """
        Sets Project Information fields (e.g. {"Project Name":"Villa A","Project Number":"2026-01","Client Name":"...","Project Address":"...","Project Status":"Schematic Design","Author":"..."}).
        @values Map of parameter name -> value.
        """, Category = "Session")]
    public static object SetProjectInfo(Dictionary<string, JsonNode> values)
    {
        var d = Ctx.RequireProject();
        var errs = Par.SetMany(d.ProjectInformation, values);
        return new JsonObject { ["errors"] = errs, ["project_information"] = Par.Values(d.ProjectInformation) };
    }
}
