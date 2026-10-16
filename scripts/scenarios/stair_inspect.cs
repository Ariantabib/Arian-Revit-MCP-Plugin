var res = new List<string>();
foreach (Stairs st in new FilteredElementCollector(doc).OfClass(typeof(Stairs)))
{
    res.Add("stairs " + st.Id + " actual risers=" + st.ActualRisersNumber + " desired=" + st.DesiredRisersNumber + " riser=" + Math.Round(st.ActualRiserHeight * 304.8, 1));
    foreach (var rid in st.GetStairsRuns())
    {
        var r = (StairsRun)doc.GetElement(rid);
        
        res.Add("  run " + rid + " risers=" + r.ActualRisersNumber + " base=" + Math.Round(r.BaseElevation * 304.8) + " top=" + Math.Round(r.TopElevation * 304.8) + " pathLen=" + Math.Round(r.GetFootprintBoundary().GetExactLength() * 304.8));
    }
    foreach (var lid in st.GetStairsLandings()) res.Add("  landing " + lid);
}
return res;
