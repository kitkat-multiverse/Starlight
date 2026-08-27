using Serilog;

namespace Starlight.Common;

public static class TaskExtensions
{
    private static readonly ILogger Logger = Log.ForContext(typeof(TaskExtensions));

    /// Fire-and-forget. Unlike a bare discard, a fault is logged where it happened instead of
    /// resurfacing later as an unobserved exception.
    public static void Defer(this Task task)
    {
        if (!task.IsCompletedSuccessfully)
            _ = Observe(task);
    }

    private static async Task Observe(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "A deferred task faulted.");
        }
    }
}
