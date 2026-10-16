using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Core;

/// <summary>Ambient Revit context. Only valid on the Revit API thread while a tool is executing.</summary>
public static class Ctx
{
    public static UIApplication UiApp { get; set; }
    public static Autodesk.Revit.ApplicationServices.Application App => UiApp?.Application;
    public static UIDocument UiDoc => UiApp?.ActiveUIDocument;

    /// <summary>The document the current tool call targets (the "document" argument or the active document).</summary>
    public static Document Doc { get; set; }

    public static Document RequireDoc()
    {
        if (Doc == null)
            throw new ToolException("No Revit document is open. Use create_project (new project from a template) or open_document first.");
        return Doc;
    }

    public static bool IsFamilyDoc => Doc != null && Doc.IsFamilyDocument;

    public static Document RequireProject()
    {
        var d = RequireDoc();
        if (d.IsFamilyDocument) throw new ToolException("This tool needs a project document, but the target document is a family document (" + d.Title + ").");
        return d;
    }

    public static Document RequireFamilyDoc()
    {
        var d = RequireDoc();
        if (!d.IsFamilyDocument) throw new ToolException("This tool needs a family document. Use family_create / family_open / family_edit_loaded first and pass its title in the 'document' argument.");
        return d;
    }

    public static Document FindDocument(string titleOrPath)
    {
        if (string.IsNullOrWhiteSpace(titleOrPath)) return UiApp?.ActiveUIDocument?.Document;
        Document hit = null;
        foreach (Document d in App.Documents)
        {
            if (d.IsLinked) continue;
            if (string.Equals(d.Title, titleOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.PathName, titleOrPath, StringComparison.OrdinalIgnoreCase)) return d;
            if (hit == null && (d.Title.Contains(titleOrPath, StringComparison.OrdinalIgnoreCase) ||
                                (d.PathName ?? "").Contains(titleOrPath, StringComparison.OrdinalIgnoreCase))) hit = d;
        }
        if (hit == null) throw new ToolException($"No open document matches '{titleOrPath}'. Use list_documents.");
        return hit;
    }
}

/// <summary>An error whose message is shown verbatim to the AI agent (no stack trace).</summary>
public class ToolException : Exception
{
    public ToolException(string message) : base(message) { }
}
