using Starlight.Game.Modules;
using Starlight.Game.Resources;
using Starlight.Protocol;

namespace Starlight.Game.Player;

public sealed class AvatarModule(IPlayer player, GameData data) : IModule
{
    #region Beach Simulator

    private const uint TeamId = 1;
    private static readonly uint[] TeamAvatarIds = [10000005];

    #endregion

    private Avatar[] _team = [];

    /// The avatars the player walks in with, in slot order.
    public IReadOnlyList<Avatar> Team => _team;

    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public AvatarDataNotify OnLogin()
    {
        // Guids only have to be unique to one player, and each avatar takes two of them: its own
        // and its weapon's.
        var guids = (ulong)player.Uid << 32;

        _team = TeamAvatarIds
            .Select((id, slot) => Avatar.Create(data, id, guids + (ulong)(slot * 2 + 1)))
            .ToArray();

        return new AvatarDataNotify {
            CurAvatarTeamId = TeamId,
            ChooseAvatarGuid = _team[0].Guid,
            OwnedFlycloakList = [Avatar.DefaultFlycloak],
            AvatarList = [.. _team.Select(avatar => avatar.Info())],
            AvatarTeamMap = {
                [TeamId] = new AvatarTeam {
                    TeamName = $"Team {TeamId}",
                    AvatarGuidList = [.._team.Select(avatar => avatar.Guid)]
                }
            }
        };
    }
}
