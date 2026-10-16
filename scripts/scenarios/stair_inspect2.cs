var res = new List<string>();
foreach (Stairs st in new FilteredElementCollector(doc).OfClass(typeof(Stairs)))
{
    foreach (var rid in st.GetStairsRuns()) { var bb = doc.GetElement(rid).get_BoundingBox(null); res.Add("run " + Math.Round(bb.Min.X*304.8) + "," + Math.Round(bb.Min.Y*304.8) + " -> " + Math.Round(bb.Max.X*304.8) + "," + Math.Round(bb.Max.Y*304.8) + " z " + Math.Round(bb.Min.Z*304.8) + ".." + Math.Round(bb.Max.Z*304.8)); }
    foreach (var lid in st.GetStairsLandings()) { var bb = doc.GetElement(lid).get_BoundingBox(null); res.Add("landing " + Math.Round(bb.Min.X*304.8) + "," + Math.Round(bb.Min.Y*304.8) + " -> " + Math.Round(bb.Max.X*304.8) + "," + Math.Round(bb.Max.Y*304.8)+ " z " + Math.Round(bb.Min.Z*304.8) + ".." + Math.Round(bb.Max.Z*304.8)); }
}
return res;
