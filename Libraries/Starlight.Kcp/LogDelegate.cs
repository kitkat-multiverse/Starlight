namespace Starlight.Kcp;

public enum LogLevel
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error
}

public delegate void LogDelegate(LogLevel level, string message, params object[] args);
