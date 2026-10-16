lv = FilteredElementCollector(doc).OfClass(Level).ToElements()
l0 = [l for l in lv if l.Name == "Level 1"][0]
wt = FilteredElementCollector(doc).OfClass(WallType).FirstElement()
created = []
for i in range(5):
    x = mm(i * 1500)
    line = Line.CreateBound(XYZ(x, 0, 0), XYZ(x, mm(4000), 0))
    w = Wall.Create(doc, line, wt.Id, l0.Id, mm(3000), 0, False, False)
    created.append(w.Id.Value)
result = {"created_walls": created, "level": l0.Name}
