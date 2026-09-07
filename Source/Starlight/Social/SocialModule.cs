using Starlight.Chat;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protocol;

namespace Starlight.Social;

public sealed class SocialModule(
    ProfileCosmeticService cosmetics,
    PlayerManager players,
    ServerFriendProfile serverFriend,
    IPlayer player
) : IModule
{
    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public Task OnLogin()
    {
        cosmetics.EnsureDefaults(player);
        return Task.CompletedTask;
    }

    [Lifecycle(LifecycleEvent.PlayerBorn)]
    public Task OnBorn()
    {
        cosmetics.EnsureDefaults(player);
        return Task.CompletedTask;
    }

    [Opcode]
    public GetPlayerSocialDetailRsp OnGetPlayerSocialDetail(GetPlayerSocialDetailReq msg)
    {
        if (msg.Uid == serverFriend.Uid)
        {
            return new GetPlayerSocialDetailRsp {
                Retcode = (int)Retcode.RETCODE_SUCC,
                DetailData = serverFriend.ToSocialDetail()
            };
        }

        if (!players.TryGet(msg.Uid, out var target))
            return new GetPlayerSocialDetailRsp { Retcode = (int)Retcode.RETCODE_PLAYER_NOT_EXIST };

        cosmetics.EnsureDefaults(target);

        return new GetPlayerSocialDetailRsp {
            Retcode = (int)Retcode.RETCODE_SUCC,
            DetailData = BuildSocialDetail(target)
        };
    }

    [Opcode]
    public GetAllUnlockNameCardRsp OnGetAllUnlockNameCard(GetAllUnlockNameCardReq _)
        => new() {
            Retcode = (int)Retcode.RETCODE_SUCC,
            NameCardList = cosmetics.GetUnlockedNameCards(player).ToList()
        };

    [Opcode]
    public SetNameCardRsp OnSetNameCard(SetNameCardReq msg)
    {
        var success = cosmetics.TrySetNameCard(player, msg.NameCardId);

        return new SetNameCardRsp {
            Retcode = success ? (int)Retcode.RETCODE_SUCC : (int)Retcode.RETCODE_NAME_CARD_NOT_UNLOCKED,
            NameCardId = success ? msg.NameCardId : player.Profile.NameCardId
        };
    }

    [Opcode]
    public SetPlayerHeadImageRsp OnSetPlayerHeadImage(SetPlayerHeadImageReq msg)
    {
        var success = cosmetics.TrySetProfilePicture(player, msg.AvatarId);

        return new SetPlayerHeadImageRsp {
            Retcode = success ? (int)Retcode.RETCODE_SUCC : (int)Retcode.RETCODE_PROFILE_PICTURE_NOT_UNLOCKED,
            ProfilePicture = cosmetics.ToProtocol(player)
        };
    }

    // TODO
    /*
    [Opcode]
    public GetProfilePictureDataRsp OnGetProfilePictureData(GetProfilePictureDataReq _)
        => new() {
            Retcode = (int)Retcode.RETCODE_SUCC,
            SpecialProfilePictureList = { cosmetics.GetSpecialProfilePictures(player) }
        };

    [Opcode]
    public SetPlayerProfilePictureRsp OnSetPlayerProfilePicture(SetPlayerProfilePictureReq msg)
    {
        var success = cosmetics.TrySetProfilePicture(player, msg.ProfilePictureId);

        return new SetPlayerProfilePictureRsp {
            Retcode = success ? (int)Retcode.RETCODE_SUCC : (int)Retcode.RETCODE_PROFILE_PICTURE_NOT_UNLOCKED,
            ProfilePicture = cosmetics.ToProtocol(player)
        };
    }
    */

    private SocialDetail BuildSocialDetail(IPlayer target)
        => new() {
            Uid = target.Uid,
            Nickname = target.Profile.Nickname,
            Signature = target.Profile.Signature,
            Birthday = new Birthday(),
            Level = 60,
            WorldLevel = 1,
            OnlineState = FriendOnlineState.FRIEND_ONLINE_STATE_ONLINE,
            IsFriend = false,
            IsMpModeAvailable = false,
            NameCardId = target.Profile.NameCardId,
            ProfilePicture = cosmetics.ToProtocol(target),
            FriendEnterHomeOption = FriendEnterHomeOption.FRIEND_ENTER_HOME_OPTION_REFUSE,
            PlatformType = PlatformType.PLATFORM_TYPE_PC,
            DataVersion = SocialDetail.Types.Version.V57
        };
}
