using Serilog.Core;

#pragma warning disable CA1050
public static class Debug
{
#pragma warning restore CA1050
    /// <summary>
    /// Logs a message at the 'Debug' level.
    /// Only occurs in Debug builds.
    /// </summary>
    [MessageTemplateFormatMethod("msg")]
    public static void Log(string msg, params object?[] args)
    {
#if DEBUG && !SURPRESS_DEBUG_LOGS
#pragma warning disable CA2254
        Serilog.Log.Debug(msg, args);
#pragma warning restore CA2254
#endif
    }

    /// <summary>
    /// Logs a message at the 'Info' level.
    /// Only occurs in Debug builds.
    /// </summary>
    [MessageTemplateFormatMethod("msg")]
    public static void Info(string msg, params object?[] args)
    {
#if DEBUG && !SURPRESS_DEBUG_LOGS
#pragma warning disable CA2254
        Serilog.Log.Information(msg, args);
#pragma warning restore CA2254
#endif
    }

    /// <summary>
    /// Logs a message at the 'Debug' level.
    /// Only occurs in Debug builds.
    /// </summary>
    [MessageTemplateFormatMethod("msg")]
    public static void Verbose(string msg, params object?[] args)
    {
#if DEBUG && !SURPRESS_DEBUG_LOGS
#pragma warning disable CA2254
        Serilog.Log.Verbose(msg, args);
#pragma warning restore CA2254
#endif
    }
}
