var bl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First(l => l.Name == "Level 1");
var tl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First(l => l.Name == "Level 2");
var res = new List<string>();
double w = 1100 / 304.8;
XYZ s = new XYZ(30, 0, 0), e1 = new XYZ(30, 6, 0);
var cands = new Dictionary<string, (XYZ a, XYZ b)>
{
    ["L1 landing-edge start"] = (new XYZ(30 + w / 2, 6 + w / 2, 0), new XYZ(30 + w / 2 + 6, 6 + w / 2, 0)),
    ["L2 e1+n*w same y"] = (new XYZ(30 + w, 6, 0), new XYZ(30 + w + 6, 6, 0)),
    ["U1 side by side"] = (new XYZ(30 + w + 0.3, 6, 0), new XYZ(30 + w + 0.3, 0, 0)),
    ["U2 shifted by w"] = (new XYZ(30 + w + 0.3, 6 + w, 0), new XYZ(30 + w + 0.3, w, 0)),
};
foreach (var kv in cands)
{
    using (var tg = new TransactionGroup(doc, "try"))
    {
        tg.Start();
        try
        {
            using (var scope = new StairsEditScope(doc, "s"))
            {
                var sid = scope.Start(bl.Id, tl.Id);
                using (var t = new Transaction(doc, "r"))
                {
                    t.Start();
                    var st = (Stairs)doc.GetElement(sid);
                    try { st.DesiredRisersNumber = 19; } catch (Exception e0) { res.Add("desired: " + e0.Message); }
                    var r1 = StairsRun.CreateStraightRun(doc, sid, Line.CreateBound(s, e1), StairsRunJustification.Center);
                    var r2 = StairsRun.CreateStraightRun(doc, sid, Line.CreateBound(kv.Value.a, kv.Value.b), StairsRunJustification.Center);
                    res.Add(kv.Key + ": r1 base=" + Math.Round(r1.BaseElevation * 304.8) + " top=" + Math.Round(r1.TopElevation * 304.8) + " risers=" + r1.ActualRisersNumber + " | r2 base=" + Math.Round(r2.BaseElevation * 304.8) + " top=" + Math.Round(r2.TopElevation * 304.8));
                    try { double h2 = r2.TopElevation - r2.BaseElevation; r2.TopElevation = r1.TopElevation + h2; r2.BaseElevation = r1.TopElevation; res.Add("  set elevations ok base=" + Math.Round(r2.BaseElevation*304.8) + " top=" + Math.Round(r2.TopElevation*304.8)); } catch (Exception ex0) { res.Add("  baseElev: " + ex0.Message); }
                    try { StairsLanding.CreateAutomaticLanding(doc, r1.Id, r2.Id); res.Add("  LANDING OK"); } catch (Exception ex) { res.Add("  landing fail: " + ex.Message); }
                    t.Commit();
                }
                scope.Cancel();
            }
        }
        catch (Exception ex) { res.Add(kv.Key + " => scope err " + ex.Message); }
        tg.RollBack();
    }
}
return res;
