using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Tools;

public static class ViewExtraTools
{
    [Tool("snapshot_3d", """
        One-call 3D picture of the whole model (or of selected elements) from a chosen direction — the quickest way to LOOK at what you built. Creates/reuses a 3D view named 'Arian Snapshot', frames the model, renders shaded and returns the PNG.
        @direction iso_se (default; camera south-east of the model) | iso_sw | iso_ne | iso_nw | top | front (looking north) | back | left | right.
        @element_ids Optional: frame only these elements. @width_px Image width. @hide_categories Categories to hide in the snapshot (e.g. ['Roofs'] to see inside; 'Grids'/'Levels' are hidden by default).
        """, Category = "View", NoTransaction = true)]
    public static object Snapshot3D(string direction = "iso_se", long[] elementIds = null, int widthPx = 1400, string[] hideCategories = null)
    {
        var doc = Ctx.RequireProject();
        BoundingBoxXYZ total = null;
        IEnumerable<Element> els = elementIds != null && elementIds.Length > 0
            ? Find.Elements(doc, elementIds)
            : new FilteredElementCollector(doc).WhereElementIsNotElementType().ToList().Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model
                    && e.Category.Id.Value != (long)BuiltInCategory.OST_Levels && e.Category.Id.Value != (long)BuiltInCategory.OST_Cameras);
        foreach (var e in els)
        {
            BoundingBoxXYZ bb = null;
            try { bb = e.get_BoundingBox(null); } catch { }
            if (bb == null) continue;
            if (total == null) total = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max };
            else
            {
                total.Min = new XYZ(Math.Min(total.Min.X, bb.Min.X), Math.Min(total.Min.Y, bb.Min.Y), Math.Min(total.Min.Z, bb.Min.Z));
                total.Max = new XYZ(Math.Max(total.Max.X, bb.Max.X), Math.Max(total.Max.Y, bb.Max.Y), Math.Max(total.Max.Z, bb.Max.Z));
            }
        }
        if (total == null) throw new ToolException("The model has no geometry to show yet.");
        var center = (total.Min + total.Max) / 2;
        double size = Math.Max((total.Max - total.Min).GetLength(), U.Mm(3000));

        XYZ dir = direction.ToLowerInvariant() switch
        {
            "iso_sw" => new XYZ(-1, -1, 0.8),
            "iso_ne" => new XYZ(1, 1, 0.8),
            "iso_nw" => new XYZ(-1, 1, 0.8),
            "top" => new XYZ(0, -0.0001, 1),
            "front" => new XYZ(0, -1, 0),
            "back" => new XYZ(0, 1, 0),
            "left" => new XYZ(-1, 0, 0),
            "right" => new XYZ(1, 0, 0),
            _ => new XYZ(1, -1, 0.8),
        };
        dir = dir.Normalize();
        var eye = center + dir * size * 1.5;
        var fwd = (center - eye).Normalize();
        var right = fwd.CrossProduct(XYZ.BasisZ);
        if (right.GetLength() < 1e-6) right = XYZ.BasisX;
        var up = right.CrossProduct(fwd).Normalize();

        const string Name = "Arian Snapshot";
        using (var t = new Transaction(doc, "Arian MCP: snapshot view"))
        {
            t.Start();
            var v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && x.Name == Name);
            if (v == null)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                v = View3D.CreateIsometric(doc, vft.Id);
                v.Name = Name;
            }
            v.SetOrientation(new ViewOrientation3D(eye, up, fwd));
            try { v.DisplayStyle = DisplayStyle.Shading; } catch { }
            try { v.DetailLevel = ViewDetailLevel.Medium; } catch { }
            // hide annotation clutter
            var hide = new List<string> { "Grids", "Levels", "Elevations", "Sections", "Cameras" };
            if (hideCategories != null) hide.AddRange(hideCategories);
            foreach (var c in hide)
            {
                try { var cat = Find.Category(doc, c); if (v.CanCategoryBeHidden(cat.Id)) v.SetCategoryHidden(cat.Id, true); } catch { }
            }
            if (hideCategories == null || hideCategories.Length == 0) { try { v.UnhideElements(new List<ElementId>()); } catch { } }
            t.Commit();
        }
        return ViewTools.GetViewImage(Name, widthPx, true, true, true);
    }
}
