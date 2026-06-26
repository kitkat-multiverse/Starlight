using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protocol;

namespace Starlight.Game.Social;

public sealed class SocialModule(IPlayer player) : IModule
{
    [Opcode]
    public async Task<GetPlayerSocialDetailRsp> OnDetailFetch(GetPlayerSocialDetailReq msg)
    {
        return new GetPlayerSocialDetailRsp {
            Retcode = (int)Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST
        };
    }
}
