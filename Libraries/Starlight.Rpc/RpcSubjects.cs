namespace Starlight.Rpc;

public static class SdkSubjects
{
    public const string ValidateAccount = "sdk.account.validate";
}

public static class GameSubjects
{
    public const string FetchPlayer = "game.player.fetch";
}

public static class GateSubjects
{
    public const string ServerHeartbeat = "dispatch.heartbeat";
}

public static class TunnelSubjects
{
    public const string NewTunnel = "rpc.tunnel";
}
