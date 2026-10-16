var res = new List<string>();
foreach (Wall w in new FilteredElementCollector(doc).OfClass(typeof(Wall)))
{
    if (w.WallType.Kind != WallKind.Basic) continue;
    if (w.WallType.Function != WallFunction.Exterior) continue;
    var c = ((LocationCurve)w.Location).Curve; var m = c.Evaluate(0.5, true);
    var o = w.Orientation;
    // outward test: move a bit along orientation from the midpoint; if it is inside the house footprint (0..15000 x -1800..10200) the wall is flipped
    var p = m + o * (1000 / 304.8);
    double x = p.X * 304.8, y = p.Y * 304.8;
    bool inside = x > 0 && x < 15000 && y > -1800 && y < 10200;
    res.Add(w.Id + " " + w.WallType.Name + " mid " + Math.Round(m.X*304.8) + "," + Math.Round(m.Y*304.8) + " orient " + Math.Round(o.X,1) + "," + Math.Round(o.Y,1) + (inside ? "  FLIPPED" : "  ok"));
}
return res;
