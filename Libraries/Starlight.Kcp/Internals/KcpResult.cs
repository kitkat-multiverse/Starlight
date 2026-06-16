namespace Starlight.Kcp.Internals;

public readonly struct KcpResult<T>
{
    private KcpResult(T value, Exception? exception)
    {
        Value = value;
        Exception = exception;
    }

    public T Value { get; }
    public Exception? Exception { get; }
    public bool IsFailure => Exception is not null;
    public bool IsSuccess => Exception is null;

    public static KcpResult<T> Success(T value) => new(value, exception: null);
    public static KcpResult<T> Failure(Exception exception) => new(default!, exception);
}
