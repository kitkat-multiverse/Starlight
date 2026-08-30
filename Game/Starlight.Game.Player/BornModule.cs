using Starlight.Game.Modules;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

public sealed class BornModule(IPlayer player) : IModule
{
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    [Opcode]
    public async Task OnSetPlayerBornData(
        SetPlayerBornDataReq message
    )
    {
        await _gate.WaitAsync();

        try
        {
            await SetPlayerBornData(message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SetPlayerBornData(SetPlayerBornDataReq message)
    {
        bool alreadyBorn;

        lock (player.StateLock)
        {
            alreadyBorn = player.State.BornState !=
                          NetPlayerState.Types.PlayerBornState.Pending;
        }

        if (alreadyBorn)
        {
            await player.Send(new SetPlayerBornDataRsp {
                Retcode = (int)Retcode.RETCODE_REPEAT_SET_PLAYER_BORN_DATA
            });
            return;
        }

        if (message.AvatarId is not 10000005 and not 10000007)
        {
            await player.Send(new SetPlayerBornDataRsp {
                Retcode = (int)Retcode.RETCODE_AVATAR_ID_ERROR
            });
            return;
        }

        var nickname = message.NickName.Trim();

        if (nickname.Length == 0)
        {
            await player.Send(new SetPlayerBornDataRsp {
                Retcode = (int)Retcode.RETCODE_NICKNAME_IS_EMPTY
            });
            return;
        }

        if (nickname.Length > 16)
        {
            await player.Send(new SetPlayerBornDataRsp {
                Retcode = (int)Retcode.RETCODE_NICKNAME_TOO_LONG
            });
            return;
        }

        var avatar = await player.Module<AvatarModule>()
            .InitializeTraveler(message.AvatarId);

        if (avatar is null)
        {
            await player.Send(new SetPlayerBornDataRsp {
                Retcode = (int)Retcode.RETCODE_AVATAR_ID_ERROR
            });
            return;
        }

        lock (player.StateLock)
        {
            player.Profile.Nickname = nickname;
        }

        await player.Send(new SetPlayerBornDataRsp());

        await player.Send(new PlayerNicknameNotify {
            Nickname = nickname
        });
        await player.Emit(LifecycleEvent.PlayerBorn);
    }
}
