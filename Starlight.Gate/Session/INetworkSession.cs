namespace Starlight.Gate.Session;

public interface INetworkSession
{
     #region Lifecycle

    void OnClose(uint reason) { }

    #endregion
}
