using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Core;

/// <summary>Lookup helpers: resolve names / ids supplied by the agent to Revit objects.</summary>
public static class Find
{
    public static bool TryId(string s, out ElementId id)
    {
        id = null;
        if (long.TryParse(s?.Trim(), out var v)) { id = new ElementId(v); return true; }
        return false;
    }

    public static Element Element(Document doc, long id)
    {
        var e = doc.GetElement(new ElementId(id));
        if (e == null) throw new ToolException($"Element {id} not found in '{doc.Title}'.");
        return e;
    }

    public static T Element<T>(Document doc, long id) where T : Element
    {
        var e = Element(doc, id);
        if (e is T t) return t;
        throw new ToolException($"Element {id} is a {e.GetType().Name} ({e.Category?.Name}), expected {typeof(T).Name}.");
    }

    public static List<Element> Elements(Document doc, IEnumerable<long> ids)
    {
        var list = new List<Element>();
        foreach (var i in ids ?? Enumerable.Empty<long>())
        {
            var e = doc.GetElement(new ElementId(i));
            if (e == null) throw new ToolException($"Element {i} not found.");
            list.Add(e);
        }
        return list;
    }

    public static List<ElementId> Ids(IEnumerable<long> ids) => (ids ?? Enumerable.Empty<long>()).Select(i => new ElementId(i)).ToList();

    // ---------------- levels ----------------
    public static Level Level(Document doc, string nameOrId, bool nullable = false)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            if (nullable) return null;
            throw new ToolException("A level name or id is required. Use list_levels.");
        }
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        if (TryId(nameOrId, out var id)) { var l = levels.FirstOrDefault(x => x.Id == id); if (l != null) return l; }
        var hit = levels.FirstOrDefault(l => string.Equals(l.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase))
                  ?? levels.FirstOrDefault(l => l.Name.Contains(nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hit != null) return hit;
        if (nullable) return null;
        throw new ToolException($"Level '{nameOrId}' not found. Available: {string.Join(", ", levels.OrderBy(l => l.Elevation).Select(l => l.Name))}");
    }

    /// <summary>The level closest below or at the given z (internal feet).</summary>
    public static Level NearestLevel(Document doc, double zFeet)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        if (levels.Count == 0) throw new ToolException("The project has no levels. Create one with create_level.");
        return levels.LastOrDefault(l => l.Elevation <= zFeet + 1e-6) ?? levels.First();
    }

    // ---------------- categories ----------------
    public static BuiltInCategory? BuiltIn(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (Enum.TryParse<BuiltInCategory>(s, true, out var bic) && Enum.IsDefined(typeof(BuiltInCategory), bic)) return bic;
        var squashed = s.Replace(" ", "").Replace("_", "").Replace("-", "");
        foreach (BuiltInCategory c in Enum.GetValues(typeof(BuiltInCategory)))
        {
            var n = c.ToString();
            if (n.StartsWith("OST_", StringComparison.Ordinal)) n = n.Substring(4);
            if (string.Equals(n.Replace("_", ""), squashed, StringComparison.OrdinalIgnoreCase)) return c;
        }
        return null;
    }

    public static Category Category(Document doc, string s)
    {
        var bic = BuiltIn(s);
        if (bic.HasValue)
        {
            try { var c = Autodesk.Revit.DB.Category.GetCategory(doc, bic.Value); if (c != null) return c; } catch { }
        }
        foreach (Category c in doc.Settings.Categories)
            if (string.Equals(c.Name, s?.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
        throw new ToolException($"Category '{s}' not found. Use list_categories, or a BuiltInCategory name like OST_Walls / 'Walls' / 'Structural Columns'.");
    }

    // ---------------- types ----------------
    /// <summary>Find an ElementType by id, "Family: Type", "Type", or substring; optionally restricted to a category.</summary>
    public static ElementType Type(Document doc, string nameOrId, BuiltInCategory? cat = null, Type cls = null, bool nullable = false)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            if (nullable) return null;
            throw new ToolException("A type name or id is required.");
        }
        var col = new FilteredElementCollector(doc).WhereElementIsElementType();
        if (cat.HasValue) col = col.OfCategory(cat.Value);
        if (cls != null) col = col.OfClass(cls);
        var all = col.Cast<ElementType>().ToList();
        if (TryId(nameOrId, out var id))
        {
            var e = all.FirstOrDefault(t => t.Id == id) ?? doc.GetElement(id) as ElementType;
            if (e != null) return e;
        }
        string q = nameOrId.Trim();
        string Full(ElementType t) => string.IsNullOrEmpty(t.FamilyName) ? t.Name : t.FamilyName + ": " + t.Name;
        var hit = all.FirstOrDefault(t => string.Equals(Full(t), q, StringComparison.OrdinalIgnoreCase))
                  ?? all.FirstOrDefault(t => string.Equals(t.Name, q, StringComparison.OrdinalIgnoreCase))
                  ?? all.FirstOrDefault(t => Full(t).Contains(q, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return hit;
        if (nullable) return null;
        var words = q.Split(new[] { ' ', ':', '-', '_', 'x', 'X', 'x' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
        var near = all.Where(t => words.Any(w => Full(t).Contains(w, StringComparison.OrdinalIgnoreCase))).Take(15).Select(Full).ToList();
        var sample = string.Join("; ", (near.Count > 0 ? near : all.Take(15).Select(Full)));
        throw new ToolException($"Type '{nameOrId}' not found{(cat.HasValue ? " in " + cat.Value : "")}{(cls != null ? " (" + cls.Name + ")" : "")}. {all.Count} candidates; {(near.Count > 0 ? "closest" : "examples")}: {sample}. Use list_element_types.");
    }

    public static T Type<T>(Document doc, string nameOrId, bool nullable = false) where T : ElementType
        => (T)Type(doc, nameOrId, null, typeof(T), nullable);

    public static FamilySymbol Symbol(Document doc, string nameOrId, BuiltInCategory? cat = null)
    {
        var s = (FamilySymbol)Type(doc, nameOrId, cat, typeof(FamilySymbol));
        return s;
    }

    // ---------------- views & other named elements ----------------
    public static View View(Document doc, string nameOrId, bool nullable = false)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            if (nullable) return null;
            throw new ToolException("A view name or id is required. Use list_views.");
        }
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
        if (TryId(nameOrId, out var id)) { var v = doc.GetElement(id) as View; if (v != null) return v; }
        var hit = views.FirstOrDefault(v => string.Equals(v.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase))
                  ?? views.FirstOrDefault(v => v.Name.Contains(nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hit != null) return hit;
        if (nullable) return null;
        throw new ToolException($"View '{nameOrId}' not found. Use list_views.");
    }

    public static View ViewTemplate(Document doc, string nameOrId)
    {
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate).ToList();
        if (TryId(nameOrId, out var id)) { var v = views.FirstOrDefault(x => x.Id == id); if (v != null) return v; }
        var hit = views.FirstOrDefault(v => string.Equals(v.Name, nameOrId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return hit ?? throw new ToolException($"View template '{nameOrId}' not found. Use list_view_templates.");
    }

    public static Material Material(Document doc, string nameOrId, bool nullable = false)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            if (nullable) return null;
            throw new ToolException("A material name or id is required.");
        }
        var mats = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().ToList();
        if (TryId(nameOrId, out var id)) { var m = mats.FirstOrDefault(x => x.Id == id); if (m != null) return m; }
        var hit = mats.FirstOrDefault(m => string.Equals(m.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hit != null) return hit;
        if (nullable) return null;
        throw new ToolException($"Material '{nameOrId}' not found. Use list_materials or create_material.");
    }

    public static T Named<T>(Document doc, string name, bool nullable = false) where T : Element
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList();
        if (TryId(name, out var id)) { var e = all.FirstOrDefault(x => x.Id == id); if (e != null) return e; }
        var hit = all.FirstOrDefault(x => string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hit != null || nullable) return hit;
        throw new ToolException($"{typeof(T).Name} '{name}' not found. Available: {string.Join(", ", all.Take(25).Select(x => x.Name))}");
    }

    public static Family Family(Document doc, string nameOrId)
    {
        var fams = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
        if (TryId(nameOrId, out var id)) { var f = fams.FirstOrDefault(x => x.Id == id); if (f != null) return f; }
        var hit = fams.FirstOrDefault(f => string.Equals(f.Name, nameOrId?.Trim(), StringComparison.OrdinalIgnoreCase))
                  ?? fams.FirstOrDefault(f => f.Name.Contains(nameOrId?.Trim() ?? "~", StringComparison.OrdinalIgnoreCase));
        return hit ?? throw new ToolException($"Family '{nameOrId}' not found. Use list_families.");
    }

    public static Type RevitClass(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var asm = typeof(Autodesk.Revit.DB.Element).Assembly;
        var t = asm.GetType("Autodesk.Revit.DB." + name.Trim(), false, true)
                ?? asm.GetType("Autodesk.Revit.DB.Architecture." + name.Trim(), false, true)
                ?? asm.GetType("Autodesk.Revit.DB.Structure." + name.Trim(), false, true)
                ?? asm.GetType("Autodesk.Revit.DB.Mechanical." + name.Trim(), false, true)
                ?? asm.GetType("Autodesk.Revit.DB.Plumbing." + name.Trim(), false, true)
                ?? asm.GetType("Autodesk.Revit.DB.Electrical." + name.Trim(), false, true);
        if (t == null) throw new ToolException($"Unknown Revit class '{name}' (examples: Wall, Floor, FamilyInstance, Level, Grid, Room, Duct, Pipe).");
        return t;
    }
}
