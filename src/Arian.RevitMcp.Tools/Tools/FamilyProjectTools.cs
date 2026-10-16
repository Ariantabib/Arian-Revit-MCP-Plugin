using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

public sealed class FamilyLoadOptions : IFamilyLoadOptions
{
    readonly bool _overwrite;
    public FamilyLoadOptions(bool overwrite) { _overwrite = overwrite; }
    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = _overwrite; return _overwrite || !familyInUse; }
    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
    { source = FamilySource.Family; overwriteParameterValues = _overwrite; return true; }
}

public static class FamilyProjectTools
{
    static IEnumerable<string> LibraryRoots()
    {
        yield return @"C:\ProgramData\Autodesk\RVT 2027\Libraries";
        yield return @"C:\Program Files\Autodesk\Revit 2027\Samples";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianFamilies");
        foreach (var d in Arian.RevitMcp.Core.PluginSettings.Current.FamilyLibraryDirs) yield return d;
    }

    [Tool("search_family_library", """
        Searches .rfa family files on disk (Autodesk content library, plus extra folders) by words in the file path.
        Example: query='door single flush' or 'chair'. Then load with load_family.
        @query Words that must all appear in the path (case-insensitive).
        @extra_dirs Additional folders to search (e.g. the user's own library).
        @limit Max results.
        """, Category = "Family", NoDocument = true, ReadOnly = true)]
    public static object SearchFamilyLibrary(string query, string[] extraDirs = null, int limit = 40)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var roots = LibraryRoots().Concat(extraDirs ?? Array.Empty<string>()).Where(Directory.Exists).Distinct().ToList();
        var arr = new JsonArray();
        foreach (var r in roots)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(r, "*.rfa", SearchOption.AllDirectories); } catch { continue; }
            foreach (var f in files)
            {
                var rel = f.Substring(r.Length).ToLowerInvariant();
                if (words.All(w => rel.Contains(w.ToLowerInvariant())))
                {
                    arr.Add(new JsonObject { ["path"] = f, ["name"] = Path.GetFileNameWithoutExtension(f), ["folder"] = Path.GetDirectoryName(f).Substring(r.Length).Trim('\\') });
                    if (arr.Count >= limit) return new JsonObject { ["count"] = arr.Count, ["files"] = arr, ["truncated"] = true };
                }
            }
        }
        return new JsonObject { ["count"] = arr.Count, ["files"] = arr };
    }

    [Tool("load_family", """
        Loads a family (.rfa) into the project. Returns the family and its types, ready for place_family_instance.
        @path Full path to an .rfa file (see search_family_library).
        @overwrite Overwrite an already loaded family and its parameter values.
        """, Category = "Family", NoTransaction = true)]
    public static object LoadFamily(string path, bool overwrite = true)
    {
        var doc = Ctx.RequireProject();
        if (!File.Exists(path)) throw new ToolException("File not found: " + path);
        using var t = new Transaction(doc, "Arian MCP: load family");
        t.Start();
        if (!doc.LoadFamily(path, new FamilyLoadOptions(overwrite), out var fam) || fam == null)
        {
            // already loaded and not overwritten
            var name = Path.GetFileNameWithoutExtension(path);
            fam = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (fam == null) { t.RollBack(); throw new ToolException("Revit could not load the family (wrong category/version or file corrupt)."); }
        }
        t.Commit();
        var types = new JsonArray();
        foreach (var id in fam.GetFamilySymbolIds()) { var s = doc.GetElement(id) as FamilySymbol; if (s != null) types.Add(new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name }); }
        return new JsonObject { ["family"] = fam.Name, ["id"] = fam.Id.Value, ["category"] = fam.FamilyCategory?.Name, ["types"] = types };
    }

    [Tool("get_family_info", """
        Describes a loaded family: category, placement type, types, and the parameters available on its types and instances (read from the first type / an instance).
        @family Family name or id.
        """, Category = "Family", ReadOnly = true)]
    public static object GetFamilyInfo(string family)
    {
        var doc = Ctx.RequireProject();
        var f = Find.Family(doc, family);
        var o = new JsonObject
        {
            ["id"] = f.Id.Value,
            ["name"] = f.Name,
            ["category"] = f.FamilyCategory?.Name,
            ["placement_type"] = f.FamilyPlacementType.ToString(),
            ["in_place"] = f.IsInPlace,
            ["editable"] = f.IsEditable,
        };
        var types = new JsonArray();
        FamilySymbol first = null;
        foreach (var id in f.GetFamilySymbolIds())
        {
            var s = (FamilySymbol)doc.GetElement(id);
            first ??= s;
            types.Add(new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name, ["active"] = s.IsActive });
        }
        o["types"] = types;
        if (first != null) o["type_parameters"] = Par.DescribeAll(first);
        var inst = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().FirstOrDefault(i => i.Symbol?.Family?.Id == f.Id);
        if (inst != null) { o["instance_parameters_from"] = inst.Id.Value; o["instance_parameters"] = Par.DescribeAll(inst); }
        else o["note"] = "No instance placed yet: only type parameters are shown. Place one to see instance parameters.";
        return o;
    }

    [Tool("delete_family", "Deletes a loaded family (and all its instances!).", Category = "Family", Destructive = true)]
    public static object DeleteFamily(string family)
    {
        var doc = Ctx.RequireProject();
        var f = Find.Family(doc, family);
        var deleted = doc.Delete(f.Id);
        return new JsonObject { ["deleted_elements"] = deleted.Count };
    }

    [Tool("edit_family", """
        Opens a LOADED family for editing (like 'Edit Family' in Revit). Returns the family document title; pass it as 'document' to the family_* tools, then call family_load_into_project to push changes back.
        @family Family name in the project.
        """, Category = "Family", NoTransaction = true)]
    public static object EditFamily(string family)
    {
        var doc = Ctx.RequireProject();
        var f = Find.Family(doc, family);
        if (!f.IsEditable) throw new ToolException("This family is not editable (system or in-place family).");
        var fd = doc.EditFamily(f);
        return new JsonObject { ["document"] = fd.Title, ["category"] = fd.OwnerFamily.FamilyCategory?.Name, ["hint"] = "Use family_* tools with document='" + fd.Title + "', then family_load_into_project." };
    }

    [Tool("export_family", "Saves a loaded family out to an .rfa file.", Category = "Family", NoTransaction = true)]
    public static object ExportFamily(string family, string path)
    {
        var doc = Ctx.RequireProject();
        var f = Find.Family(doc, family);
        var fd = doc.EditFamily(f);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        fd.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        fd.Close(false);
        return new JsonObject { ["saved"] = path };
    }
}
