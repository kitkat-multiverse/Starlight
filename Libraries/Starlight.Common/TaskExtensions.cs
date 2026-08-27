using Serilog;

namespace Starlight.Common;

public static class TaskExtensions
{
    private static readonly ILogger Logger = Log.ForContext(typeof(TaskExtensions));

    /// <summary>
    /// Fire-and-forget: lets <paramref name="task"/> run on without awaiting it. Unlike a bare
    /// discard, a fault is logged where it happened rather than resurfacing later as an
    /// unobserved exception. Only use this where the caller genuinely has no ordering or
    /// failure interest in the result.
    /// </summary>
    public static void Defer(this Task task)
    {
        if (task.IsCompletedSuccessfully)
            return;

        _ = Observe(task);
        return;

        static async Task Observe(Task pending)
        {
            try
            {
                await pending;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "A deferred task faulted.");
            }
        }
    }
}
