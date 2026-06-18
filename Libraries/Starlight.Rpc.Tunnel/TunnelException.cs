namespace Starlight.Rpc.Tunnel;

public abstract class TunnelException(string message) : Exception(message);

public sealed class TunnelRequestTimeoutException(string id, TimeSpan period)
    : TunnelException($"Requested data on tunnel ID '{id}', but received no reply after {period}.");

public sealed class TunnelClosedException()
    : TunnelException("The tunnel is closed.");

public sealed class TunnelDecodeException(string message)
    : TunnelException(message);

public sealed class TunnelHandshakeException(string message)
    : TunnelException(message);
