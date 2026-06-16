namespace Starlight.Kcp;

public interface IKcpServerHandler
{
    void OnConnected(KcpConnection conn);
    void OnDisconnected(KcpConnection conn);
    void OnReceive(KcpConnection conn, byte[] data);
}