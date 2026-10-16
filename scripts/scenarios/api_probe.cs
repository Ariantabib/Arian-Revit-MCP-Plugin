int n = 0;
foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Planting).WhereElementIsNotElementType().ToList())
{ var p = ((LocationPoint)fi.Location).Point; double x = p.X * 304.8, y = p.Y * 304.8; if (Math.Abs(x - 23500) < 1500 && Math.Abs(y + 3500) < 1500) { doc.Delete(fi.Id); n++; } }
return n;
