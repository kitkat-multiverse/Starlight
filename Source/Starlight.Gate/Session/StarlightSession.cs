using Google.Protobuf;
using Serilog;
using Starlight.Gate.Crypto;
using Starlight.Gate.Network;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;

namespace Starlight.Gate.Session;

public sealed class StarlightSession : INetworkSession
{
    private static readonly ILogger Logger = Log.ForContext<StarlightSession>();

    private readonly GateServerService _server;
    private readonly KcpConnection _connection;

    private ProtocolRegistry? _registry;
    private byte[] _xorpad;

    public StarlightSession(GateServerService server, KcpConnection connection)
    {
        _server = server;
        _connection = connection;

        _xorpad = server.ServerKey;
    }

    public async Task HandlePacket(byte[] data)
    {
        #region Pre-process the packet

        CryptoHelper.Xor(data, _xorpad);

        var packet = new GamePacket(data);

        #endregion

        #region Registry Check & Lookup

        _registry ??= _server.Registry.ResolveByFirstPacket(packet.CmdId)
                      ?? throw new MissingRegistryException(packet.CmdId);

        using var stream = new CodedInputStream(packet.Body);
        var message = _registry.Deserialize(packet.CmdId, stream);

        #endregion

        if (_server.Config.Connections.LogPackets)
        {
            Logger.Debug("C>S | Packet: {Message} [{CmdId}] ({Length} bytes)",
                message.GetType().Name, packet.CmdId, packet.Body.Length);
        }
    }
}
