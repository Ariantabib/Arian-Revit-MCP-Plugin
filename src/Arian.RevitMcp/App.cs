using System.Reflection;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Arian.RevitMcp.UI;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp;

public class App : IExternalApplication
{
    public static UIControlledApplication Ui;

    public Result OnStartup(UIControlledApplication application)
    {
        Ui = application;
        try
        {
            // make sure private dependencies (Roslyn etc.) next to this dll resolve
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPluginDir;

            PluginSettings.Load();
            Log.Info("Arian Revit MCP Plugin starting (Revit " + application.ControlledApplication.VersionNumber + ")");
            RevitDispatcher.Init();
            try { ToolLoader.Load(); } catch (Exception ex) { Log.Error("Tools assembly failed to load: " + ex); ToolRegistry.Build(); }
            Automation.Hook(application);
            Ribbon.Create(application);
            try { application.RegisterDockablePane(AgentPane.PaneId, Loc.T("Arian Agent"), new AgentPane()); } catch (Exception ex) { Log.Warn("Dockable pane: " + ex.Message); }

            application.ControlledApplication.ApplicationInitialized += (s, e) =>
            {
                if (PluginSettings.Current.AutoStartServer)
                {
                    McpHttpServer.Instance.Start(out _);
                    Ribbon.Refresh();
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed: " + ex);
            TaskDialog.Show("Arian MCP", "Plugin failed to start:\n" + ex);
            return Result.Failed;
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try { McpHttpServer.Instance.Stop(); } catch { }
        try { Automation.Unhook(application); } catch { }
        return Result.Succeeded;
    }

    static Assembly ResolveFromPluginDir(object sender, ResolveEventArgs args)
    {
        try
        {
            var name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources")) return null;
            var dir = System.IO.Path.GetDirectoryName(typeof(App).Assembly.Location);
            var path = System.IO.Path.Combine(dir, name + ".dll");
            if (System.IO.File.Exists(path)) return Assembly.LoadFrom(path);
        }
        catch { }
        return null;
    }
}
