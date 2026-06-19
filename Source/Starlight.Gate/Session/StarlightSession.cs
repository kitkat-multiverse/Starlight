using Starlight.Kcp;

namespace Starlight.Gate.Session;

public sealed class StarlightSession(KcpConnection connection) : INetworkSession
{
}
