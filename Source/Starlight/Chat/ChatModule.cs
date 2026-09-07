using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protocol;

namespace Starlight.Chat;

public sealed class ChatModule(
    ChatService chat,
    ServerFriendProfile serverFriend,
    IPlayer player
) : IModule
{
    [Opcode]
    public GetPlayerFriendListRsp OnGetPlayerFriendList(GetPlayerFriendListReq _)
        => new() {
            Retcode = (int)Retcode.RETCODE_SUCC,
            FriendList = { serverFriend.ToFriendBrief() }
        };

    [Opcode]
    public GetPlayerAskFriendListRsp OnGetPlayerAskFriendList(GetPlayerAskFriendListReq _)
        => new() { Retcode = (int)Retcode.RETCODE_SUCC };

    [Opcode]
    public GetPlayerBlacklistRsp OnGetPlayerBlacklist(GetPlayerBlacklistReq _)
        => new() { Retcode = (int)Retcode.RETCODE_SUCC };

    [Opcode]
    public GetRecentMpPlayerListRsp OnGetRecentMpPlayers(GetRecentMpPlayerListReq _)
        => new() { Retcode = (int)Retcode.RETCODE_SUCC };

    [Opcode]
    public GetChatEmojiCollectionRsp OnGetChatEmojiCollection(GetChatEmojiCollectionReq _)
        => new() {
            Retcode = (int)Retcode.RETCODE_SUCC,
            ChatEmojiCollectionData = new ChatEmojiCollectionData()
        };

    [Opcode]
    public SetChatEmojiCollectionRsp OnSetChatEmojiCollection(SetChatEmojiCollectionReq _)
        => new() { Retcode = (int)Retcode.RETCODE_SUCC };

    [Opcode]
    public async Task<PrivateChatRsp> OnPrivateChat(PrivateChatReq msg)
    {
        var retcode = msg.ContentCase switch {
            PrivateChatReq.ContentOneofCase.Text => await chat.SendTextAsync(player, msg.TargetUid, msg.Text),
            PrivateChatReq.ContentOneofCase.Icon => await chat.SendIconAsync(player, msg.TargetUid, msg.Icon),
            _ => (int)Retcode.RETCODE_RPIVATE_CHAT_INVALID_CONTENT_TYPE
        };

        return new PrivateChatRsp { Retcode = retcode };
    }

    [Opcode]
    public PullPrivateChatRsp OnPullPrivateChat(PullPrivateChatReq msg)
    {
        var response = new PullPrivateChatRsp { Retcode = (int)Retcode.RETCODE_SUCC };

        response.ChatInfo.AddRange(chat.PullPrivateChat(
            player.Uid,
            msg.TargetUid,
            msg.FromSequence,
            msg.PullNum));
        return response;
    }

    [Opcode]
    public PullRecentChatRsp OnPullRecentChat(PullRecentChatReq msg)
    {
        var response = new PullRecentChatRsp { Retcode = (int)Retcode.RETCODE_SUCC };

        response.ChatInfo.AddRange(chat.PullRecentChat(
            player.Uid,
            msg.PullNum,
            msg.BeginSequence));
        return response;
    }

    [Opcode]
    public ReadPrivateChatRsp OnReadPrivateChat(ReadPrivateChatReq _)
        => new() { Retcode = (int)Retcode.RETCODE_SUCC };
}
