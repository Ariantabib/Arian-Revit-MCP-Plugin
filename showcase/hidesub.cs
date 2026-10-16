int n = 0;
var hide = new List<Category>();
var topo = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Topography);
foreach (Category c in topo.SubCategories) if (c.Name != "<Hidden Lines>") hide.Add(c);
var site = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Site);
foreach (Category c in site.SubCategories) if (new[] { "Internal Origin", "Project Base Point", "Survey Point", "Property Lines", "Property Line Segments", "Utilities", "Stripe" }.Contains(c.Name)) hide.Add(c);
foreach (View3D v in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(x => !x.IsTemplate))
    foreach (var c in hide) { try { if (v.CanCategoryBeHidden(c.Id)) { v.SetCategoryHidden(c.Id, true); n++; } } catch { } }
return n;
