using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arian.RevitMcp.Core;

namespace Arian.RevitMcp.Server;

/// <summary>
/// Local MCP server (Streamable HTTP, JSON responses). Loopback only, with Host/Origin validation
/// to protect against browser-based (CSRF / DNS-rebinding) access.
/// </summary>
public sealed class McpHttpServer
{
    public static McpHttpServer Instance { get; } = new();
    HttpListener _listener;
    CancellationTokenSource _cts;
    public bool Running => _listener?.IsListening == true;
    public int Port { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public long Requests;
    public string LastError;

    public string Url => $"http://127.0.0.1:{Port}/mcp";

    public bool Start(out string error)
    {
        error = null;
        if (Running) return true;
        try
        {
            Port = PluginSettings.Current.Port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            StartedAt = DateTime.Now;
            _ = Task.Run(() => AcceptLoop(_cts.Token));
            Log.Info($"MCP server listening on {Url}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            LastError = ex.Message;
            Log.Error("Server start failed: " + ex);
            try { _listener?.Close(); } catch { }
            _listener = null;
            return false;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        StartedAt = null;
        Log.Info("MCP server stopped.");
    }

    async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleSafe(ctx));
        }
    }

    async Task HandleSafe(HttpListenerContext ctx)
    {
        try { await Handle(ctx); }
        catch (Exception ex)
        {
            Log.Error("HTTP handler error: " + ex.Message);
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        host = host.Trim();
        int colon = host.LastIndexOf(':');
        if (colon > 0 && !host.EndsWith("]")) host = host.Substring(0, colon);
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
    }

    async Task Handle(HttpListenerContext ctx)
    {
        Interlocked.Increment(ref Requests);
        var rq = ctx.Request;
        var rs = ctx.Response;
        string path = rq.Url.AbsolutePath.TrimEnd('/');
        if (path == "") path = "/";

        // --- anti browser-attack checks ---
        if (!IsLoopbackHost(rq.Headers["Host"])) { await Send(rs, 403, "text/plain", "Forbidden host"); return; }
        string origin = rq.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin))
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var ou) || !IsLoopbackHost(ou.Host))
            { await Send(rs, 403, "text/plain", "Forbidden origin"); return; }
        }
        var token = PluginSettings.Current.AuthToken;
        if (!string.IsNullOrEmpty(token) && path != "/health" && path != "/")
        {
            var auth = rq.Headers["Authorization"] ?? "";
            if (auth != "Bearer " + token && rq.Headers["X-Arian-Token"] != token)
            { await Send(rs, 401, "text/plain", "Unauthorized"); return; }
        }

        if (path == "/health")
        {
            var info = new JsonObject
            {
                ["ok"] = true,
                ["server"] = McpProtocol.ServerName,
                ["version"] = McpProtocol.Version,
                ["tools"] = ToolRegistry.All.Count,
                ["busy_with"] = RevitDispatcher.CurrentTool,
                ["pending"] = RevitDispatcher.Pending,
            };
            await Send(rs, 200, "application/json", info.ToJsonString());
            return;
        }
        if (path == "/")
        {
            await Send(rs, 200, "text/html; charset=utf-8", LandingPage());
            return;
        }
        if (path != "/mcp" && path != "/rpc") { await Send(rs, 404, "text/plain", "Not found"); return; }

        if (rq.HttpMethod == "DELETE") { await Send(rs, 200, "text/plain", ""); return; }
        if (rq.HttpMethod != "POST")
        {
            rs.AddHeader("Allow", "POST, DELETE");
            await Send(rs, 405, "text/plain", "Use POST with a JSON-RPC body");
            return;
        }

        string body;
        using (var sr = new StreamReader(rq.InputStream, Encoding.UTF8)) body = await sr.ReadToEndAsync();
        JsonNode parsed;
        try { parsed = JsonNode.Parse(body); }
        catch
        {
            await Send(rs, 400, "application/json", "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error\"}}");
            return;
        }

        HashSet<string> cats = null;
        var q = rq.QueryString["categories"];
        if (!string.IsNullOrWhiteSpace(q))
            cats = new HashSet<string>(q.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        if (parsed is JsonArray batch)
        {
            var outs = new JsonArray();
            foreach (var m in batch)
            {
                var r = await McpProtocol.HandleAsync(m, cats);
                if (r != null) outs.Add(r);
            }
            if (outs.Count == 0) { await Send(rs, 202, "text/plain", ""); return; }
            await Send(rs, 200, "application/json", outs.ToJsonString(J.Opts));
            return;
        }

        var resp = await McpProtocol.HandleAsync(parsed, cats);
        if (resp == null) { await Send(rs, 202, "text/plain", ""); return; }
        await Send(rs, 200, "application/json", resp.ToJsonString(J.Opts));
    }

    static async Task Send(HttpListenerResponse rs, int code, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body ?? "");
        rs.StatusCode = code;
        rs.ContentType = contentType;
        rs.ContentLength64 = bytes.Length;
        if (bytes.Length > 0) await rs.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        rs.Close();
    }

    string LandingPage() => $@"<!doctype html><meta charset=utf-8><title>Arian Revit MCP</title>
<body style=""font-family:Segoe UI,sans-serif;max-width:720px;margin:40px auto;color:#222"">
<h2>Arian Revit MCP Plugin</h2>
<p>The MCP server is running. Endpoint: <code>{Url}</code> ({ToolRegistry.All.Count} tools).</p>
<p>Claude Code: <code>claude mcp add --transport http arian-revit {Url}</code></p>
<p>Other clients: use the stdio bridge <code>node arian-revit-mcp-bridge.mjs</code> (see README).</p>
</body>";
}
