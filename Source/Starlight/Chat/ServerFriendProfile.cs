using Starlight.Protocol;

namespace Starlight.Chat;

public sealed class ServerFriendProfile
{
    public ServerFriendProfile(ChatConfig config)
    {
        var source = config.ServerFriend;

        if (source.Uid == 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Server friend UID must be non-zero.");

        Uid = source.Uid;
        Nickname = source.Nickname;
        Signature = source.Signature;
        AdventureRank = source.AdventureRank;
        WorldLevel = source.WorldLevel;
        AvatarId = source.AvatarId;
        NameCardId = source.NameCardId;
    }

    public uint Uid { get; }
    public string Nickname { get; }
    public string Signature { get; }
    public uint AdventureRank { get; }
    public uint WorldLevel { get; }
    public uint AvatarId { get; }
    public uint NameCardId { get; }

    public FriendBrief ToFriendBrief()
        => new() {
            Uid = Uid,
            Nickname = Nickname,
            Level = AdventureRank,
            WorldLevel = WorldLevel,
            Signature = Signature,
            OnlineState = FriendOnlineState.FRIEND_ONLINE_STATE_ONLINE,
            Param = 1,
            LastActiveTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            NameCardId = NameCardId,
            FriendEnterHomeOption = FriendEnterHomeOption.FRIEND_ENTER_HOME_OPTION_REFUSE,
            ProfilePicture = new ProfilePicture { AvatarId = AvatarId },
            IsGameSource = true,
            IsMpModeAvailable = false,
            PlatformType = PlatformType.PLATFORM_TYPE_PC
        };

    public SocialDetail ToSocialDetail()
        => new() {
            Uid = Uid,
            Nickname = Nickname,
            Level = AdventureRank,
            AvatarId = AvatarId,
            Signature = Signature,
            Birthday = new Birthday { Month = 12, Day = 31 },
            WorldLevel = WorldLevel,
            OnlineState = FriendOnlineState.FRIEND_ONLINE_STATE_ONLINE,
            Param = 1,
            IsFriend = true,
            IsMpModeAvailable = false,
            NameCardId = NameCardId,
            FriendEnterHomeOption = FriendEnterHomeOption.FRIEND_ENTER_HOME_OPTION_REFUSE,
            ProfilePicture = new ProfilePicture { AvatarId = AvatarId },
            PlatformType = PlatformType.PLATFORM_TYPE_PC,
            DataVersion = SocialDetail.Types.Version.V57
        };
}
