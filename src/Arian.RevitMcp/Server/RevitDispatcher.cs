using System.Collections.Concurrent;
using Arian.RevitMcp.Core;
using Autodesk.Revit.UI;

namespace Arian.RevitMcp.Server;

/// <summary>
/// Marshals work from HTTP threads onto the Revit API thread through an ExternalEvent.
/// </summary>
public sealed class RevitDispatcher : IExternalEventHandler
{
    sealed class Job
    {
        public Func<object> Fn;
        public TaskCompletionSource<object> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool Abandoned;
        public string Name;
    }

    public static RevitDispatcher Instance { get; private set; }
    readonly ConcurrentQueue<Job> _queue = new();
    ExternalEvent _event;

    public static string CurrentTool { get; private set; }
    public static DateTime? BusySince { get; private set; }
    public static long CompletedCalls;
    public static int Pending => Instance?._queue.Count ?? 0;

    /// <summary>Must be called from a valid Revit API context (IExternalApplication.OnStartup).</summary>
    public static void Init()
    {
        Instance = new RevitDispatcher();
        Instance._event = ExternalEvent.Create(Instance);
    }

    public async Task<object> RunAsync(string name, Func<object> fn, int timeoutSeconds)
    {
        var job = new Job { Fn = fn, Name = name };
        _queue.Enqueue(job);
        _event.Raise();
        var done = await Task.WhenAny(job.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (done != job.Tcs.Task)
        {
            job.Abandoned = true;
            throw new ToolException(
                $"Timed out after {timeoutSeconds}s waiting for Revit to run '{name}'. Revit may be busy, showing a modal dialog or an error window. " +
                "Try the 'dismiss_dialogs' tool, check 'get_dialog_log', or look at the Revit window.");
        }
        return await job.Tcs.Task;
    }

    public void Execute(UIApplication app)
    {
        Ctx.UiApp = app;
        while (_queue.TryDequeue(out var job))
        {
            if (job.Abandoned) continue;
            CurrentTool = job.Name;
            BusySince = DateTime.Now;
            try { job.Tcs.TrySetResult(job.Fn()); }
            catch (Exception ex) { job.Tcs.TrySetException(ex); }
            finally { CurrentTool = null; BusySince = null; Interlocked.Increment(ref CompletedCalls); }
        }
    }

    /// <summary>Marks every queued (not yet running) job as abandoned; returns how many were dropped.</summary>
    public int CancelPending()
    {
        int n = 0;
        while (_queue.TryDequeue(out var job)) { job.Abandoned = true; job.Tcs.TrySetException(new ToolException("Cancelled by the user.")); n++; }
        return n;
    }

    public string GetName() => "Arian Revit MCP";
}
