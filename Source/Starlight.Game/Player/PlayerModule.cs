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
public sealed class PlayerModule(
    RpcTransport rpc,
    PlayerManager players,
    ILogger<PlayerModule> logger,
    IPlayer player
) : IModule
{
    /// <summary>
    /// Authenticates the player and loads their data, then hands off to every
    /// <see cref="LifecycleEvent.PlayerLogin"/> handler before answering the client.
    /// </summary>
    [Opcode]
    public async Task<PlayerLoginRsp> OnLogin(PlayerLoginReq msg)
    {
        string nickname;

        try
        {
            // Fetch the player's full data from the database gateway.
            var request = new FetchPlayerReq { AccountUid = player.AccountUid, Create = true };
            var response = await rpc.Request<FetchPlayerReq, FetchPlayerRsp>(GameSubjects.FetchPlayer, request);

            if (response is not { Player: {} data, Retcode: StarlightRetcode.Success })
            {
                logger.LogError("Failed to fetch player '{AccountId}': {Response}", player.AccountUid, response.Retcode);

                throw new KickException(DisconnectReason.ServerKick,
                    new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST });
            }

            // Set player properties.
            player.Uid = data.Uid;
            player.State = data.State ?? new NetPlayerState();
            nickname = data.Profile?.Nickname ?? string.Empty;

            if (!players.Add(player))
            {
                logger.LogWarning("Rejected a second session for player '{PlayerId}'.", player.Uid);

                throw new KickException(DisconnectReason.ServerKick,
                    new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_REPEAT_LOGIN });
            }

            logger.LogInformation("Player '{PlayerId}' logged in.", player.Uid);
        }
        catch (OperationCanceledException)
        {
            throw new KickException(DisconnectReason.ServerKick,
                new PlayerLoginRsp { Retcode = (int)Retcode.RETCODE_ACCOUNT_VEIRFY_ERROR });
        }

        await player.Send(OpenStates());

        await player.Send(new PlayerDataNotify {
            NickName = nickname,
            ServerTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PropMap = {
                [(uint)PlayerProperty.IsFlyable] = PlayerProperty.IsFlyable.Value(1),
                [(uint)PlayerProperty.IsTransferable] = PlayerProperty.IsTransferable.Value(1),
                [(uint)PlayerProperty.IsDiveable] = PlayerProperty.IsDiveable.Value(1),
                [(uint)PlayerProperty.Level] = PlayerProperty.PlayerLevel.Value(60),
                [(uint)PlayerProperty.Exp] = PlayerProperty.PlayerExp.Value(0),
                [(uint)PlayerProperty.CurPersistStamina] = PlayerProperty.CurPersistStamina.Value(24000),
                [(uint)PlayerProperty.MaxStamina] = PlayerProperty.MaxStamina.Value(24000),
                [(uint)PlayerProperty.PlayerWorldLevel] = PlayerProperty.PlayerWorldLevel.Value(1)
            }
        });

        // Everything that has to be in place before the client is told it is logged in.
        await player.Emit(LifecycleEvent.PlayerLogin);

        return new PlayerLoginRsp {
            IsUseAbilityHash = true,
            AbilityHashCode = 1844674,
            GameBiz = "hk4e_global",
            CountryCode = "US"
        };
    }

    [Lifecycle(LifecycleEvent.PlayerDisconnect)]
    public async Task OnDisconnect()
    {
        // A rejected duplicate session must not overwrite the live session's state.
        if (!players.Remove(player))
            return;

        try
        {
            var response = await rpc.Request<SavePlayerReq, SavePlayerRsp>(
                GameSubjects.SavePlayer,
                new SavePlayerReq { Uid = player.Uid, State = player.State });

            if (response.Retcode != StarlightRetcode.Success)
                logger.LogError("Failed to save player '{PlayerId}': {Retcode}", player.Uid, response.Retcode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save player '{PlayerId}' during disconnect", player.Uid);
        }

        logger.LogInformation("Player '{PlayerId}' logged out.", player.Uid);
    }

    /// <summary>Unlocks everything the client gates behind an open state.</summary>
    private static OpenStateUpdateNotify OpenStates()
    {
        var notify = new OpenStateUpdateNotify();

        for (var state = 1u; state <= 10000; state++)
        {
            notify.OpenStateMap[state] = 1;
        }

        return notify;
    }
}
