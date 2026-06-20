namespace Starlight.Kcp;

public interface IKcpServerHandler
{
    void OnConnected(KcpConnection conn);
    void OnDisconnected(KcpConnection conn, uint reason);
    void OnReceive(KcpConnection conn, byte[] data);
}
