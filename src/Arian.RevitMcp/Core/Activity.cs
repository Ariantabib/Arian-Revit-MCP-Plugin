using Arian.RevitMcp.Server;

namespace Arian.RevitMcp.Core;

public sealed class ActivityRecord
{
    public DateTime Time;
    public string Tool, Category, Status = "running", Error;
    public long Ms;
    public bool Mutating;
}

/// <summary>Live record of agent tool calls (shown in the Arian panel), plus cooperative cancellation.</summary>
public static class Activity
{
    static readonly object Gate = new();
    static readonly List<ActivityRecord> Records = new();
    public static volatile bool CancelRequested;
    public static string LastMutatingTool;
    public static DateTime? LastMutatingTime;
    public static long Calls, Failures;

    public static ActivityRecord Begin(string tool, string category, bool mutating)
    {
        var r = new ActivityRecord { Time = DateTime.Now, Tool = tool, Category = category, Mutating = mutating };
        lock (Gate) { Records.Add(r); while (Records.Count > 300) Records.RemoveAt(0); }
        return r;
    }

    public static void End(ActivityRecord r, bool ok, long ms, string error = null)
    {
        r.Ms = ms; r.Status = ok ? "ok" : "failed"; r.Error = error;
        Interlocked.Increment(ref Calls);
        if (!ok) Interlocked.Increment(ref Failures);
        if (ok && r.Mutating) { LastMutatingTool = r.Tool; LastMutatingTime = DateTime.Now; }
    }

    public static List<ActivityRecord> Snapshot(int n = 200) { lock (Gate) return Records.Skip(Math.Max(0, Records.Count - n)).Reverse().ToList(); }
    public static void Clear() { lock (Gate) Records.Clear(); }

    /// <summary>Cancels queued requests and asks the running tool to stop at its next checkpoint.</summary>
    public static int Cancel()
    {
        CancelRequested = true;
        return RevitDispatcher.Instance?.CancelPending() ?? 0;
    }

    /// <summary>Long-running tools call this inside loops: throws when the user pressed Cancel.</summary>
    public static void Check()
    {
        if (CancelRequested) throw new ToolException("Cancelled by the user (Arian panel → Cancel).");
    }
}
