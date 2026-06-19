using Starlight.Kcp;

namespace Starlight.Gate.Session;

public sealed class NetworkSession(KcpConnection connection) : INetworkSession
{
}
