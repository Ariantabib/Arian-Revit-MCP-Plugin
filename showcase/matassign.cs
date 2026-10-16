var res = new List<string>();
Material M(string contains) => new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name.Contains(contains));
var map = new Dictionary<string, Dictionary<string, string>>
{
    ["Arian table"] = new() { ["Top Material"] = "Beech Timber", ["Leg Material"] = "Stainless" },
    ["Arian chair"] = new() { ["Chair Material"] = "Spruce Timber" },
    ["Arian bed"] = new() { ["Frame Material"] = "Beech Timber", ["Mattress Material"] = "Paint Cream" },
    ["Arian sofa"] = new() { ["Sofa Material"] = "Paint Grey" },
    ["Arian wardrobe"] = new() { ["Body Material"] = "Paint White", ["Door Material"] = "Beech Timber" },
    ["Arian kitchen base cabinet"] = new() { ["Cabinet Material"] = "Paint White", ["Counter Material"] = "Granite" },
    ["Arian wc"] = new() { ["Ceramic"] = "Ceramic Tile" },
    ["Arian washbasin"] = new() { ["Ceramic"] = "Ceramic Tile" },
    ["Arian bathtub"] = new() { ["Acrylic"] = "Paint White" },
    ["Arian ceiling light"] = new() { ["Housing"] = "Aluminium" },
    ["Arian Timber Fin"] = new() { ["Timber"] = "Fin Timber" },
};
foreach (FamilySymbol fs in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
{
    if (!map.TryGetValue(fs.FamilyName, out var pm)) continue;
    foreach (var kv in pm)
    {
        var p = fs.LookupParameter(kv.Key); var m = M(kv.Value);
        if (p != null && m != null && !p.IsReadOnly) { p.Set(m.Id); }
    }
    res.Add(fs.FamilyName + ": " + fs.Name);
}
// round columns: structural material
var conc = M("Reinforced Concrete C25");
foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType())
{ var p = fi.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM); if (p != null && !p.IsReadOnly && conc != null) p.Set(conc.Id); }
return res.Count;
