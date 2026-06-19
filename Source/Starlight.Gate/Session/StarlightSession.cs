using Starlight.Kcp;

namespace Starlight.Gate.Session;

public sealed class StarlightSession : INetworkSession
{
    private readonly GateServerService _server;
    private readonly KcpConnection _connection;

    private byte[] _xorKey;

    public StarlightSession(GateServerService server, KcpConnection connection)
    {
        _server = server;
        _connection = connection;

        _xorKey = server.ServerKey;
    }

    public async Task HandlePacket(byte[] data)
    {

    }
}
