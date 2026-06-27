using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protobuf.Core;
using Starlight.Protocol;

namespace Starlight.Game.Avatar;

public sealed class AvatarModule(IPlayer player) : IModule
{
    [Opcode(typeof(PlayerLoginReq))]
    public async Task<IMessage> OnLogin()
    {
        // TODO: Query avatar data from database gateway.
        var avatarCount = 0;

        if (avatarCount == 0)
        {
            // Player has no avatars; have them select their traveler.
            return new DoSetPlayerBornDataNotify();
        }

        return new AvatarDataNotify {
            AvatarList = {}
        };
    }

    [Opcode]
    public async Task<SetPlayerBornDataRsp> OnSetBornData(SetPlayerBornDataReq msg) =>
        // TODO: Check if nickname is valid.
        // TODO: Initialize player data asynchronously.
        // TODO: Save selected traveler choice.
        new() {
            Retcode = (int)Retcode.RETCODE_NICKNAME_WORD_ILLEGAL
        };
}
