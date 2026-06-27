using Microsoft.Extensions.Logging;
using Starlight.Game.Modules;
using Starlight.Kcp;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

/// <summary>
/// Primary data operations module used across everything.
/// </summary>
public sealed class PlayerModule(RpcTransport rpc, ILogger<PlayerModule> logger, IPlayer player) : IModule
{
    /// This handler serves to authenticate & fetch the player's initial data.
    /// <br/>
    /// It should be called before all login handlers.
    [Opcode(Priority = 1000)]
    public async Task OnStartLogin(PlayerLoginReq msg)
    {
        try
        {
            // Fetch the player's full data from the database gateway.
            var request = new FetchPlayerReq { AccountUid = msg.AccountUid, Create = true };
            var response = await rpc.Request<FetchPlayerReq, FetchPlayerRsp>(GameSubjects.FetchPlayer, request);

            if (response is not { Player: var data, Retcode: StarlightRetcode.Success })
            {
                logger.LogError("Failed to fetch player '{AccountId}': {Response}", msg.AccountUid, response.Retcode);

                throw new KickException(DisconnectReason.ServerKick,
                    new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST });
            }

            // Set player properties.
            player.Uid = data.Uid;

            logger.LogInformation("Player '{PlayerId}' logged in.", player.Uid);
        }
        catch (OperationCanceledException)
        {
            throw new KickException(DisconnectReason.ServerKick,
                new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_ACCOUNT_VEIRFY_ERROR });
        }
    }

    /// This handler serves only to finish the login flow.
    /// <br/>
    /// It should be called after all login handlers.
    [Opcode(typeof(PlayerLoginReq), Priority = -1000)]
    public PlayerLoginRsp OnFinishLogin() => new() {
        IsUseAbilityHash = true,
        AbilityHashCode = 1844674,
        GameBiz = "hk4e_global",
        CountryCode = "US"
    };
}
