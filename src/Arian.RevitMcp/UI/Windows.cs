using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Arian.RevitMcp.Core;
using Arian.RevitMcp.Server;

namespace Arian.RevitMcp.UI;

/// <summary>Helpers that build the plugin's small WPF windows in code.</summary>
static class Ui
{
    public static Window NewWindow(string title, double w, double h)
    {
        var win = new Window
        {
            Title = title,
            Width = w,
            Height = h,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            ResizeMode = ResizeMode.CanResizeWithGrip,
        };
        try { new System.Windows.Interop.WindowInteropHelper(win).Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
        return win;
    }

    public static TextBlock Label(string t, bool bold = false, double size = 13) =>
        new() { Text = t, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontSize = size, Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap };

    public static Button Btn(string text, RoutedEventHandler click, double width = 0)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        if (width > 0) b.MinWidth = width;
        b.Click += click;
        return b;
    }

    public static UIElement CopyRow(string title, string text)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        sp.Children.Add(Label(title, true));
        var tb = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(6),
            MinHeight = 28,
            MaxHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        sp.Children.Add(tb);
        var btn = Btn("Copy", (s, e) => { try { Clipboard.SetText(text); } catch { } });
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        btn.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(btn);
        return sp;
    }
}

public static class ConnectWindow
{
    public static string BridgePath()
    {
        var dir = Path.GetDirectoryName(typeof(ConnectWindow).Assembly.Location);
        return Path.Combine(dir, "bridge", "arian-revit-mcp-bridge.mjs");
    }

    public static string ClaudeCodeCommand()
    {
        var s = PluginSettings.Current;
        var url = $"http://127.0.0.1:{s.Port}/mcp";
        var hdr = string.IsNullOrEmpty(s.AuthToken) ? "" : $" --header \"Authorization: Bearer {s.AuthToken}\"";
        return $"claude mcp add --transport http arian-revit {url}{hdr}";
    }

    public static string HttpJson()
    {
        var s = PluginSettings.Current;
        var url = $"http://127.0.0.1:{s.Port}/mcp";
        if (string.IsNullOrEmpty(s.AuthToken))
            return "{\n  \"mcpServers\": {\n    \"arian-revit\": {\n      \"type\": \"http\",\n      \"url\": \"" + url + "\"\n    }\n  }\n}";
        return "{\n  \"mcpServers\": {\n    \"arian-revit\": {\n      \"type\": \"http\",\n      \"url\": \"" + url + "\",\n      \"headers\": { \"Authorization\": \"Bearer " + s.AuthToken + "\" }\n    }\n  }\n}";
    }

    public static string StdioJson()
    {
        var s = PluginSettings.Current;
        var bridge = BridgePath().Replace("\\", "\\\\");
        var env = $"\"ARIAN_REVIT_MCP_URL\": \"http://127.0.0.1:{s.Port}/mcp\"";
        if (!string.IsNullOrEmpty(s.AuthToken)) env += $", \"ARIAN_REVIT_MCP_TOKEN\": \"{s.AuthToken}\"";
        return "{\n  \"mcpServers\": {\n    \"arian-revit\": {\n      \"command\": \"node\",\n      \"args\": [\"" + bridge + "\"],\n      \"env\": { " + env + " }\n    }\n  }\n}";
    }

    /// <summary>Merges the 'arian-revit' stdio server into %APPDATA%\Claude\claude_desktop_config.json (a backup is kept).</summary>
    public static string InstallClaudeDesktop()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "claude_desktop_config.json");
            System.Text.Json.Nodes.JsonObject cfg;
            if (File.Exists(file))
            {
                File.Copy(file, file + ".arian-backup", true);
                try { cfg = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) as System.Text.Json.Nodes.JsonObject ?? new(); } catch { cfg = new(); }
            }
            else cfg = new();
            var servers = cfg["mcpServers"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            var s = PluginSettings.Current;
            var env = new System.Text.Json.Nodes.JsonObject { ["ARIAN_REVIT_MCP_URL"] = $"http://127.0.0.1:{s.Port}/mcp" };
            if (!string.IsNullOrEmpty(s.AuthToken)) env["ARIAN_REVIT_MCP_TOKEN"] = s.AuthToken;
            servers["arian-revit"] = new System.Text.Json.Nodes.JsonObject
            {
                ["command"] = "node",
                ["args"] = new System.Text.Json.Nodes.JsonArray(BridgePath()),
                ["env"] = env,
            };
            cfg["mcpServers"] = servers;
            File.WriteAllText(file, cfg.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return "Added 'arian-revit' to " + file + " — restart Claude Desktop. (Needs Node.js installed.)";
        }
        catch (Exception ex) { return "Failed: " + ex.Message; }
    }

    public static void Show()
    {
        var srv = McpHttpServer.Instance;
        var win = Ui.NewWindow("Arian Revit MCP — Connect", 720, 760);
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(Ui.Label("Arian Revit MCP Plugin", true, 20));
        var status = srv.Running
            ? $"● Server RUNNING at {srv.Url}   ({ToolRegistry.All.Count} tools, {srv.Requests} requests)"
            : "○ Server is STOPPED — click “Start Server” in the Arian tab.";
        var st = Ui.Label(status, true, 14);
        st.Foreground = new SolidColorBrush(srv.Running ? Icons.Green : Icons.Red);
        root.Children.Add(st);
        root.Children.Add(Ui.CopyRow("Claude Code (one command)", ClaudeCodeCommand()));
        root.Children.Add(Ui.CopyRow("Claude Desktop / any stdio client (uses the bundled Node bridge)", StdioJson()));
        root.Children.Add(Ui.CopyRow("Cursor / VS Code / other clients with native HTTP MCP support", HttpJson()));
        var installRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        var installStatus = Ui.Label("", false, 12);
        installRow.Children.Add(Ui.Btn("Add to Claude Desktop config", (a, b) => installStatus.Text = InstallClaudeDesktop()));
        root.Children.Add(installRow);
        root.Children.Add(installStatus);
        root.Children.Add(Ui.Label("Tip: keep Revit open with a project loaded. Lengths in the API are millimetres.", false, 12));
        win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        win.Show();
    }
}

public static class SettingsWindow
{
    static readonly string[] Categories = { "Session", "Query", "Modify", "Architecture", "Structure", "MEP", "Family", "Material", "View", "Manage", "Advanced", "Custom" };

    static readonly (string name, string cats)[] Profiles =
    {
        ("All tools", ""),
        ("Phase 1 - Architecture", "Session,Query,Modify,Architecture,View,Material,Manage"),
        ("Phase 2 - Structure", "Session,Query,Modify,Structure,Architecture,View,Material,Manage"),
        ("MEP", "Session,Query,Modify,MEP,View,Manage,Family"),
        ("Families & Materials", "Session,Query,Family,Material,View,Manage"),
        ("Documentation", "Session,Query,View,Manage"),
        ("Read-only explorer", "Session,Query,View,Manage"),
        ("Minimal (small context)", "Session,Query,View"),
    };

    public static void Show()
    {
        var s = PluginSettings.Current;
        var win = Ui.NewWindow("Arian Revit MCP — " + Loc.T("Settings"), 600, 760);
        if (Loc.IsFa) win.FlowDirection = FlowDirection.RightToLeft;
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(Ui.Label(Loc.T("Settings"), true, 18));

        var port = new System.Windows.Controls.TextBox { Text = s.Port.ToString(), Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(Ui.Label(Loc.T("Port (restart the server to apply)")));
        root.Children.Add(port);

        var auto = new CheckBox { Content = Loc.T("Start the MCP server automatically when Revit starts"), IsChecked = s.AutoStartServer, Margin = new Thickness(0, 10, 0, 0) };
        root.Children.Add(auto);
        var code = new CheckBox { Content = Loc.T("Allow the agent to run C# code inside Revit (execute_csharp)"), IsChecked = s.AllowCodeExecution, Margin = new Thickness(0, 6, 0, 0) };
        root.Children.Add(code);
        var dlg = new CheckBox { Content = Loc.T("Automatically answer Revit task dialogs (least destructive choice)"), IsChecked = s.AutoDismissDialogs, Margin = new Thickness(0, 6, 0, 0) };
        root.Children.Add(dlg);

        root.Children.Add(Ui.Label(Loc.T("Warnings / errors policy")));
        var pol = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        pol.Items.Add("Off — keep Revit's default behaviour");
        pol.Items.Add("Ignore warnings (delete them automatically)");
        pol.Items.Add("Ignore warnings + auto-resolve errors");
        pol.SelectedIndex = (int)s.WarningPolicy;
        root.Children.Add(pol);

        root.Children.Add(Ui.Label(Loc.T("Language")));
        var lang = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        lang.Items.Add("English"); lang.Items.Add("فارسی (Persian)");
        lang.SelectedIndex = Loc.IsFa ? 1 : 0;
        root.Children.Add(lang);

        root.Children.Add(Ui.Label(Loc.T("Optional access token (leave empty for none; loopback only anyway)")));
        var tok = new System.Windows.Controls.TextBox { Text = s.AuthToken ?? "" };
        root.Children.Add(tok);
        root.Children.Add(Ui.Label(Loc.T("Tool timeout (seconds)")));
        var to = new System.Windows.Controls.TextBox { Text = s.ToolTimeoutSeconds.ToString(), Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(to);

        // ---- tool profiles + per-category switches
        root.Children.Add(Ui.Label(Loc.T("Profile"), true));
        var prof = new ComboBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var p in Profiles) prof.Items.Add(p.name);
        prof.SelectedIndex = Math.Max(0, Array.FindIndex(Profiles, p => p.name == s.ToolProfile));
        root.Children.Add(prof);

        var enabledNow = string.IsNullOrWhiteSpace(s.EnabledCategories) ? null : new HashSet<string>(s.EnabledCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        var roNow = new HashSet<string>((s.ReadOnlyCategories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        root.Children.Add(Ui.Label(Loc.T("Tool categories (tick the ones the agent may see)") + "   |   " + Loc.T("Read-only categories (agent cannot change the model with these)").Split('(')[0].Trim()));
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        var enable = new Dictionary<string, CheckBox>(); var readOnly = new Dictionary<string, CheckBox>();
        grid.Children.Add(Cell(0, 0, new TextBlock { Text = "Category", FontWeight = FontWeights.SemiBold }));
        grid.Children.Add(Cell(1, 0, new TextBlock { Text = "Visible", FontWeight = FontWeights.SemiBold }));
        grid.Children.Add(Cell(2, 0, new TextBlock { Text = "Read-only (no changes)", FontWeight = FontWeights.SemiBold }));
        grid.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < Categories.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var c = Categories[i];
            grid.Children.Add(Cell(0, i + 1, new TextBlock { Text = c }));
            var e = new CheckBox { IsChecked = enabledNow == null || enabledNow.Contains(c) || c == "Session" }; enable[c] = e; grid.Children.Add(Cell(1, i + 1, e));
            var r = new CheckBox { IsChecked = roNow.Contains(c) }; readOnly[c] = r; grid.Children.Add(Cell(2, i + 1, r));
        }
        root.Children.Add(grid);
        prof.SelectionChanged += (a, b) =>
        {
            if (prof.SelectedIndex < 0) return;
            var set = Profiles[prof.SelectedIndex].cats;
            var ids = string.IsNullOrEmpty(set) ? null : new HashSet<string>(set.Split(','), StringComparer.OrdinalIgnoreCase);
            foreach (var c in Categories) enable[c].IsChecked = ids == null || ids.Contains(c);
            if (Profiles[prof.SelectedIndex].name == "Read-only explorer") foreach (var c in Categories) readOnly[c].IsChecked = c != "Session" && c != "Query" && c != "View" && c != "Manage" ? false : false;
        };

        if (Loc.IsFa || lang.SelectedIndex >= 0) root.Children.Add(Ui.Label(Loc.T("Restart Revit to refresh the ribbon language."), false, 11));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        row.Children.Add(Ui.Btn(Loc.T("Save"), (a, b) =>
        {
            if (int.TryParse(port.Text, out var p) && p > 1023 && p < 65536) s.Port = p;
            if (int.TryParse(to.Text, out var t) && t > 5) s.ToolTimeoutSeconds = t;
            s.AutoStartServer = auto.IsChecked == true;
            s.AllowCodeExecution = code.IsChecked == true;
            s.AutoDismissDialogs = dlg.IsChecked == true;
            s.WarningPolicy = (WarningPolicy)Math.Max(0, pol.SelectedIndex);
            s.AuthToken = tok.Text.Trim();
            s.Language = lang.SelectedIndex == 1 ? "fa" : "en";
            s.ToolProfile = prof.SelectedIndex >= 0 ? Profiles[prof.SelectedIndex].name : "All tools";
            bool all = Categories.All(c => enable[c].IsChecked == true);
            s.EnabledCategories = all ? "" : string.Join(",", Categories.Where(c => enable[c].IsChecked == true || c == "Session"));
            s.ReadOnlyCategories = string.Join(",", Categories.Where(c => readOnly[c].IsChecked == true));
            PluginSettings.Save();
            Ribbon.Refresh();
            win.Close();
        }, 80));
        row.Children.Add(Ui.Btn(Loc.T("Cancel"), (a, b) => win.Close(), 80));
        root.Children.Add(row);
        win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        win.Show();
    }

    static UIElement Cell(int col, int row, UIElement e)
    {
        Grid.SetColumn(e, col); Grid.SetRow(e, row);
        if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 2, 0, 2);
        return e;
    }
}
