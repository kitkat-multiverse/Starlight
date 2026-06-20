using Starlight.SDK.Common;

namespace Starlight.SDK.Http;

/// <summary>
/// Non-generic envelope for SDK responses that carry no payload (errors,
/// ack-only responses). Endpoints that do return a payload should use
/// <see cref="ApiResponse{T}"/> so the payload shape is compile-checked.
/// </summary>
public sealed class ApiResponse
{
    public int Retcode { get; init; }
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }

    /// <summary>Build a response from a <see cref="Common.Retcode"/> using the message table.</summary>
    public static ApiResponse From(Retcode code, object? data = null) => new() {
        Retcode = (int)code,
        Message = RetcodeMessages.Get(code),
        Data = data
    };

    /// <summary>Build a response with an explicit override message.</summary>
    public static ApiResponse From(Retcode code, string message, object? data = null) => new() {
        Retcode = (int)code,
        Message = message,
        Data = data
    };

    public static ApiResponse Ok(object? data = null) => From(Common.Retcode.Success, data);
}

/// <summary>
/// Generic, fully-typed envelope for SDK responses that carry a payload.
/// Use this in place of <see cref="ApiResponse"/> whenever the endpoint
/// returns a known payload shape; the wire format (a top-level
/// <c>retcode</c> / <c>message</c> / <c>data</c> triple) is identical.
/// </summary>
/// <typeparam name="T">The compile-time type of the response payload.</typeparam>
public sealed class ApiResponse<T>
{
    public int Retcode { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }

    /// <summary>Build a typed response from a <see cref="Common.Retcode"/> using the message table.</summary>
    public static ApiResponse<T> From(Retcode code, T? data = default) => new() {
        Retcode = (int)code,
        Message = RetcodeMessages.Get(code),
        Data = data
    };

    /// <summary>Build a typed response with an explicit override message.</summary>
    public static ApiResponse<T> From(Retcode code, string message, T? data = default) => new() {
        Retcode = (int)code,
        Message = message,
        Data = data
    };

    public static ApiResponse<T> Ok(T data) => From(Common.Retcode.Success, data);
}
