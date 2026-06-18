namespace Starlight.Rpc.Tunnel;

public abstract class TunnelException(string message) : Exception(message);

public sealed class TunnelRequestTimeoutException(string frequency, TimeSpan period)
    : TunnelException($"Requested data on tunnel frequency '{frequency}', but received no reply after {period}.");

public sealed class TunnelClosedException()
    : TunnelException("The tunnel is closed.");

public sealed class TunnelHandshakeException(string message)
    : TunnelException(message);
