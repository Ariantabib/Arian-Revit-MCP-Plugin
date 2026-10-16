var res = new List<string>();
// interior wall type -> function Interior
foreach (WallType wt in new FilteredElementCollector(doc).OfClass(typeof(WallType)))
    if (wt.Name == "Arian Interior 150") { try { wt.Function = WallFunction.Interior; } catch { } }
int flipped = 0;
foreach (Wall w in new FilteredElementCollector(doc).OfClass(typeof(Wall)))
{
    if (w.WallType.Kind != WallKind.Basic || w.WallType.Name == "Arian Interior 150") continue;
    var c = ((LocationCurve)w.Location).Curve; var m = c.Evaluate(0.5, true); var o = w.Orientation;
    var p = m + o * (1000 / 304.8); double x = p.X * 304.8, y = p.Y * 304.8;
    bool inside = x > 0 && x < 15000 && y > -1800 && y < 10200;
    if (inside) { w.Flip(); flipped++; }
}
res.Add("flipped " + flipped);
return res;
