var bl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First(l => l.Name == "Ground Floor");
var tl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First(l => l.Name == "First Floor");
var res = new List<string>();
double w = 1150 / 304.8, g = 100 / 304.8, l1 = 2520 / 304.8;
foreach (double k in new[] { 0.0, 0.5, 1.0 })
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
                    st.DesiredRisersNumber = 19;
                    XYZ s = new XYZ(60, 0, 0), e1 = new XYZ(60, l1, 0);
                    var r1 = StairsRun.CreateStraightRun(doc, sid, Line.CreateBound(s, e1), StairsRunJustification.Center);
                    XYZ s2 = new XYZ(60 + w + g, l1 + k * w, 0), e2 = new XYZ(60 + w + g, l1 + k * w - 2240 / 304.8, 0);
                    var r2 = StairsRun.CreateStraightRun(doc, sid, Line.CreateBound(s2, e2), StairsRunJustification.Center);
                    double h2 = r2.TopElevation - r2.BaseElevation; r2.TopElevation = r1.TopElevation + h2; r2.BaseElevation = r1.TopElevation;
                    try { var ld = StairsLanding.CreateAutomaticLanding(doc, r1.Id, r2.Id); var bb = doc.GetElement(ld[0]).get_BoundingBox(null); res.Add("k=" + k + " landing y " + Math.Round(bb.Min.Y*304.8) + ".." + Math.Round(bb.Max.Y*304.8) + " | r1 " + Math.Round(r1.get_BoundingBox(null).Max.Y*304.8) + " r2 " + Math.Round(r2.get_BoundingBox(null).Min.Y*304.8) + ".." + Math.Round(r2.get_BoundingBox(null).Max.Y*304.8)); }
                    catch (Exception ex) { res.Add("k=" + k + " landing fail " + ex.Message); }
                    t.Commit();
                }
                scope.Cancel();
            }
        }
        catch (Exception ex) { res.Add("k=" + k + " err " + ex.Message); }
        tg.RollBack();
    }
}
return res;
