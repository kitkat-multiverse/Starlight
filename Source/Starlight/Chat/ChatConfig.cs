namespace Starlight.Chat;

public sealed class ChatConfig
{
    public ServerFriendConfig ServerFriend { get; set; } = new();
    public int HistoryLimit { get; set; } = 100;
    public int DefaultPullCount { get; set; } = 50;
    public int MaxPullCount { get; set; } = 100;
    public int MaxMessageLength { get; set; } = 1024;
}

public sealed class ServerFriendConfig
{
    public uint Uid { get; set; } = 69;
    public string Nickname { get; set; } = "Starlight";
    public string Signature { get; set; } = "Starlight Server";
    public uint AdventureRank { get; set; } = 60;
    public uint WorldLevel { get; set; } = 8;
    public uint AvatarId { get; set; } = 10000007;
    public uint NameCardId { get; set; } = 210001;
}
