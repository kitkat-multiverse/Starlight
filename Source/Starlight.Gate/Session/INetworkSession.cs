namespace Starlight.Gate.Session;

public interface INetworkSession
{
    #region Lifecycle

    Task HandlePacket(byte[] data);

    void OnClose(uint reason)
    {
    }

    #endregion
}
