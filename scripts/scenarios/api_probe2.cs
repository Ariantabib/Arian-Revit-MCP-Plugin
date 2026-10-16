var res = new List<string>();
foreach (var p in new FilteredElementCollector(doc).OfClass(typeof(PropertySetElement)).Cast<PropertySetElement>().Where(x => x.Name.Contains("Gypsum") || x.Name.Contains("Concrete C25")).Take(10))
    res.Add(p.Name);
return res;
