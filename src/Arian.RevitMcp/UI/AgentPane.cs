using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;
using Autodesk.Revit.UI;
using Button = System.Windows.Controls.Button;
using Page = System.Windows.Controls.Page;

namespace Arian.RevitMcp.UI;

/// <summary>Dockable panel inside Revit: server status, live tool-call activity, undo / cancel, image history and log.</summary>
public sealed class AgentPane : Page, IDockablePaneProvider
{
    public static readonly DockablePaneId PaneId = new(new Guid("6d3b4f0e-3f6e-4d6a-9d1f-4a0e5a1c7b21"));
    public static AgentPane Instance;

    readonly TextBlock _status = new() { FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _busy = new() { Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x60, 0x70)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
    readonly ListView _calls = new() { BorderThickness = new Thickness(0), FontSize = 12 };
    readonly WrapPanel _images = new();
    readonly System.Windows.Controls.TextBox _log = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) };
    readonly Button _start, _undo, _cancel;
    readonly TabControl _tabs = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    int _tick;

    public AgentPane()
    {
        Instance = this;
        FlowDirection = Loc.IsFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9));
        FontFamily = new FontFamily("Segoe UI");
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Loc.T("Arian Agent"), FontSize = 18, FontWeight = FontWeights.Bold });
        top.Children.Add(_status);
        top.Children.Add(_busy);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _start = Button(Loc.T("Start server"), (s, e) => ToggleServer());
        _undo = Button(Loc.T("Undo last change"), (s, e) => UndoLast());
        _cancel = Button(Loc.T("Cancel running task"), (s, e) => { int n = Activity.Cancel(); _busy.Text = $"Cancel requested ({n} queued dropped)"; });
        bar.Children.Add(_start); bar.Children.Add(_undo); bar.Children.Add(_cancel);
        bar.Children.Add(Button(Loc.T("Clear"), (s, e) => { Activity.Clear(); Refresh(); }));
        top.Children.Add(bar);
        Grid.SetRow(top, 0); root.Children.Add(top);

        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = Loc.T("Time"), Width = 62, DisplayMemberBinding = new System.Windows.Data.Binding("Time") });
        gv.Columns.Add(new GridViewColumn { Header = Loc.T("Tool"), Width = 190, DisplayMemberBinding = new System.Windows.Data.Binding("Tool") });
        gv.Columns.Add(new GridViewColumn { Header = "ms", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("Ms") });
        gv.Columns.Add(new GridViewColumn { Header = Loc.T("Status"), Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding("Status") });
        _calls.View = gv;
        _tabs.Items.Add(new TabItem { Header = Loc.T("Activity"), Content = _calls });
        _tabs.Items.Add(new TabItem { Header = Loc.T("Images"), Content = new ScrollViewer { Content = _images, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        _tabs.Items.Add(new TabItem { Header = Loc.T("Log"), Content = _log });
        Grid.SetRow(_tabs, 1); root.Children.Add(_tabs);
        Content = root;

        _timer.Tick += (s, e) => Refresh();
        Loaded += (s, e) => { _timer.Start(); Refresh(); };
        Unloaded += (s, e) => _timer.Stop();
    }

    static Button Button(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        b.Click += click; return b;
    }

    static void ToggleServer()
    {
        var srv = McpHttpServer.Instance;
        if (srv.Running) srv.Stop(); else srv.Start(out _);
        Ribbon.Refresh();
    }

    public static void UndoLast()
    {
        _ = RevitDispatcher.Instance.RunAsync("undo (panel)", () =>
        {
            var id = Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(Autodesk.Revit.UI.PostableCommand.Undo);
            Ctx.UiApp.PostCommand(id);
            return null;
        }, 30);
    }

    void Refresh()
    {
        try
        {
            var srv = McpHttpServer.Instance;
            _status.Text = srv.Running
                ? $"● {Loc.T("Server running")} — {srv.Url}  ({ToolRegistry.All.Count} {Loc.T("tools")}, {Activity.Calls} {Loc.T("calls")}, {Activity.Failures} {Loc.T("failed")})"
                : $"○ {Loc.T("Server stopped")}";
            _status.Foreground = new SolidColorBrush(srv.Running ? Icons.Green : Icons.Red);
            _start.Content = srv.Running ? Loc.T("Stop server") : Loc.T("Start server");
            var cur = RevitDispatcher.CurrentTool;
            _busy.Text = cur != null ? $"⏳ {Loc.T("Running")}: {cur}  ({(DateTime.Now - (RevitDispatcher.BusySince ?? DateTime.Now)).TotalSeconds:0}s, {RevitDispatcher.Pending} queued)" : Loc.T("Idle");
            _undo.ToolTip = Activity.LastMutatingTool != null ? $"Last agent change: {Activity.LastMutatingTool} ({Activity.LastMutatingTime:HH:mm:ss}). Posts Revit's Undo." : "Posts Revit's Undo command.";
            _undo.IsEnabled = true;
            _cancel.IsEnabled = cur != null || RevitDispatcher.Pending > 0;
            _calls.ItemsSource = Activity.Snapshot(120).Select(r => new
            {
                Time = r.Time.ToString("HH:mm:ss"), r.Tool, Ms = r.Status == "running" ? "…" : r.Ms.ToString(),
                Status = r.Status == "ok" ? "✓" + (r.Mutating ? " (changed model)" : "") : r.Status == "failed" ? "✗ " + (r.Error ?? "").Substring(0, Math.Min(70, (r.Error ?? "").Length)) : "⏳"
            }).ToList();
            if (_tabs.SelectedIndex == 2) { _log.Text = string.Join("\n", Log.Tail(150)); _log.ScrollToEnd(); }
            if (_tabs.SelectedIndex == 1 && _tick++ % 4 == 0) RefreshImages();
        }
        catch (Exception ex) { Log.Warn("Agent pane refresh: " + ex.Message); }
    }

    void RefreshImages()
    {
        var files = new List<FileInfo>();
        try { files.AddRange(new DirectoryInfo(Path.GetTempPath()).GetFiles("arian-image-*.png")); } catch { }
        try
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArianRenders");
            if (Directory.Exists(d)) { files.AddRange(new DirectoryInfo(d).GetFiles("*.png")); files.AddRange(new DirectoryInfo(d).GetFiles("*.jpg")); }
        }
        catch { }
        var latest = files.OrderByDescending(f => f.LastWriteTime).Take(12).ToList();
        _images.Children.Clear();
        foreach (var f in latest)
        {
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 220; bi.UriSource = new Uri(f.FullName); bi.EndInit(); bi.Freeze();
                var img = new Image { Source = bi, Width = 220, Margin = new Thickness(4), ToolTip = f.Name + "\n" + f.LastWriteTime };
                var path = f.FullName;
                img.MouseLeftButtonUp += (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { } };
                _images.Children.Add(img);
            }
            catch { }
        }
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
    }
}
