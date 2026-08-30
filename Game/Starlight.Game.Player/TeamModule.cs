using Starlight.Game.Modules;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

public sealed class TeamModule(IPlayer player) : IModule
{
    private const uint DefaultTeamId = 1;
    private const uint MaxTeamCount = 4;
    private const int MaxTeamSize = 4;

    private readonly Dictionary<uint, PlayerTeam> _teams = [];
    private readonly Dictionary<uint, NetAvatarTeam> _teamState = [];
    private uint _currentTeamId;
    private bool _loaded;

    public PlayerTeam Current
    {
        get
        {
            lock (player.StateLock)
            {
                LoadState();

                if (!_teams.TryGetValue(_currentTeamId, out var team))
                    throw new InvalidOperationException("The player does not have an avatar team yet.");

                return Snapshot(team);
            }
        }
    }

    public IReadOnlyDictionary<uint, PlayerTeam> Teams
    {
        get
        {
            lock (player.StateLock)
            {
                LoadState();
                return _teams.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value));
            }
        }
    }

    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public void OnLogin() => LoadState();

    [Opcode]
    public async Task<SetUpAvatarTeamRsp> OnSetUpAvatarTeam(SetUpAvatarTeamReq msg)
    {
        var response = new SetUpAvatarTeamRsp {
            TeamId = msg.TeamId,
            CurAvatarGuid = msg.CurAvatarGuid,
            AvatarTeamGuidList = [.. msg.AvatarTeamGuidList]
        };

        AvatarTeamUpdateNotify notification;
        bool activeTeamChanged;

        lock (player.StateLock)
        {
            LoadState();

            if (msg.TeamId is 0 or > MaxTeamCount)
            {
                response.Retcode = (int)Retcode.RETCODE_CAN_NOT_FIND_TEAM;
                return response;
            }

            if (msg.AvatarTeamGuidList.Count == 0)
            {
                response.Retcode = (int)Retcode.RETCODE_AVATAR_NOT_EXIST_IN_TEAM;
                return response;
            }

            if (msg.AvatarTeamGuidList.Count > MaxTeamSize)
            {
                response.Retcode = (int)Retcode.RETCODE_TEAM_COST_EXCEED_LIMIT;
                return response;
            }

            if (msg.AvatarTeamGuidList.Distinct().Count() != msg.AvatarTeamGuidList.Count)
            {
                response.Retcode = (int)Retcode.RETCODE_DUPLICATE_AVATAR;
                return response;
            }

            var owned = player.Module<AvatarModule>().Avatars.Values
                .ToDictionary(avatar => avatar.Guid);

            if (msg.AvatarTeamGuidList.Any(guid => !owned.ContainsKey(guid)))
            {
                response.Retcode = (int)Retcode.RETCODE_CAN_NOT_FIND_AVATAR;
                return response;
            }

            var members = msg.AvatarTeamGuidList.Select(guid => owned[guid]).ToArray();
            var currentAvatarGuid = msg.AvatarTeamGuidList.Contains(msg.CurAvatarGuid)
                ? msg.CurAvatarGuid
                : members[0].Guid;

            response.CurAvatarGuid = currentAvatarGuid;

            if (!_teams.TryGetValue(msg.TeamId, out var team))
            {
                team = new PlayerTeam {
                    Id = msg.TeamId,
                    Name = $"Team {msg.TeamId}",
                    Avatars = members,
                    CurrentAvatarGuid = currentAvatarGuid
                };

                var newState = new NetAvatarTeam {
                    TeamId = team.Id,
                    Name = team.Name,
                    CurrentAvatarGuid = team.CurrentAvatarGuid
                };
                newState.AvatarGuids.Add(msg.AvatarTeamGuidList);

                _teams.Add(team.Id, team);
                _teamState.Add(team.Id, newState);
                player.State.AvatarTeams.Add(newState);
            }
            else
            {
                team.Avatars = members;
                team.CurrentAvatarGuid = currentAvatarGuid;

                var state = _teamState[team.Id];
                state.AvatarGuids.Clear();
                state.AvatarGuids.Add(msg.AvatarTeamGuidList);
                state.CurrentAvatarGuid = team.CurrentAvatarGuid;
            }

            notification = new AvatarTeamUpdateNotify();

            foreach (var savedTeam in _teams.Values)
                notification.AvatarTeamMap.Add(savedTeam.Id, savedTeam.Info());

            activeTeamChanged = team.Id == _currentTeamId;
        }

        await player.Send(notification);

        if (activeTeamChanged)
            await player.Emit(LifecycleEvent.PlayerTeamChanged);

        return response;
    }

    [Opcode]
    public async Task<ChooseCurAvatarTeamRsp> OnChooseCurAvatarTeam(ChooseCurAvatarTeamReq msg)
    {
        var response = new ChooseCurAvatarTeamRsp { CurTeamId = msg.TeamId };
        bool changed;

        lock (player.StateLock)
        {
            LoadState();

            if (!_teams.TryGetValue(msg.TeamId, out var team))
            {
                response.Retcode = (int)Retcode.RETCODE_CAN_NOT_FIND_TEAM;
                return response;
            }

            if (team.Avatars.Length == 0)
            {
                response.Retcode = (int)Retcode.RETCODE_AVATAR_NOT_EXIST_IN_TEAM;
                return response;
            }

            changed = _currentTeamId != team.Id;
            _currentTeamId = team.Id;
            player.State.CurrentAvatarTeamId = team.Id;
        }

        if (changed)
            await player.Emit(LifecycleEvent.PlayerTeamChanged);

        return response;
    }

    [Opcode]
    public async Task<ChangeAvatarRsp> OnChangeAvatar(ChangeAvatarReq msg)
    {
        var response = new ChangeAvatarRsp {
            CurGuid = msg.Guid,
            SkillId = msg.SkillId
        };

        lock (player.StateLock)
        {
            LoadState();

            if (!_teams.TryGetValue(_currentTeamId, out var team))
            {
                response.Retcode = (int)Retcode.RETCODE_CAN_NOT_FIND_CUR_TEAM;
                return response;
            }

            if (team.Avatars.All(avatar => avatar.Guid != msg.Guid))
            {
                response.Retcode = (int)Retcode.RETCODE_AVATAR_NOT_EXIST_IN_TEAM;
                return response;
            }

            if (team.CurrentAvatarGuid == msg.Guid)
            {
                response.Retcode = (int)Retcode.RETCODE_AVATAR_IS_SAME_ONE;
                return response;
            }

            team.CurrentAvatarGuid = msg.Guid;
            _teamState[team.Id].CurrentAvatarGuid = msg.Guid;
        }

        await player.Emit(LifecycleEvent.PlayerTeamChanged);
        return response;
    }

    internal void Initialize(Avatar avatar)
    {
        lock (player.StateLock)
        {
            LoadState();
            EnsureTeamSlots(avatar);
            SelectUsableTeam();
            PersistTeams();
        }
    }

    internal void LoadState()
    {
        lock (player.StateLock)
        {
            if (_loaded)
                return;

            _loaded = true;

            var avatars = player.Module<AvatarModule>().Avatars.Values
                .ToDictionary(avatar => avatar.Guid);

            foreach (var state in player.State.AvatarTeams.Take((int)MaxTeamCount))
            {
                if (state.TeamId is 0 or > MaxTeamCount || _teams.ContainsKey(state.TeamId))
                    continue;

                var members = state.AvatarGuids
                    .Distinct()
                    .Where(avatars.ContainsKey)
                    .Take(MaxTeamSize)
                    .Select(guid => avatars[guid])
                    .ToArray();

                state.AvatarGuids.Clear();
                state.AvatarGuids.Add(members.Select(avatar => avatar.Guid));

                if (members.Length == 0)
                    state.CurrentAvatarGuid = 0;
                else if (members.All(avatar => avatar.Guid != state.CurrentAvatarGuid))
                    state.CurrentAvatarGuid = members[0].Guid;

                if (string.IsNullOrWhiteSpace(state.Name))
                    state.Name = $"Team {state.TeamId}";

                var team = new PlayerTeam {
                    Id = state.TeamId,
                    Name = state.Name,
                    Avatars = members,
                    CurrentAvatarGuid = state.CurrentAvatarGuid
                };

                _teams.Add(team.Id, team);
                _teamState.Add(team.Id, state);
            }

            if (player.State.BornState != NetPlayerState.Types.PlayerBornState.Pending)
            {
                var starter = avatars.Values.FirstOrDefault(avatar =>
                                  avatar.AvatarId == player.State.BornAvatarId)
                              ?? avatars.Values.FirstOrDefault();

                EnsureTeamSlots(starter);
            }

            _currentTeamId = player.State.CurrentAvatarTeamId;
            SelectUsableTeam();
            PersistTeams();
        }
    }

    private void EnsureTeamSlots(Avatar? starter)
    {
        for (uint teamId = 1; teamId <= MaxTeamCount; teamId++)
        {
            if (_teams.ContainsKey(teamId))
                continue;

            Avatar[] members = teamId == DefaultTeamId && starter is not null
                ? new[] { starter }
                : [];

            AddTeam(teamId, members);
        }
    }

    private void AddTeam(uint teamId, Avatar[] members)
    {
        var currentAvatarGuid = members.FirstOrDefault()?.Guid ?? 0;
        var state = new NetAvatarTeam {
            TeamId = teamId,
            Name = $"Team {teamId}",
            CurrentAvatarGuid = currentAvatarGuid
        };
        state.AvatarGuids.Add(members.Select(avatar => avatar.Guid));

        var team = new PlayerTeam {
            Id = state.TeamId,
            Name = state.Name,
            Avatars = members,
            CurrentAvatarGuid = state.CurrentAvatarGuid
        };

        _teams.Add(team.Id, team);
        _teamState.Add(team.Id, state);
        player.State.AvatarTeams.Add(state);
    }

    private void SelectUsableTeam()
    {
        if (!_teams.TryGetValue(_currentTeamId, out var current) || current.Avatars.Length == 0)
        {
            _currentTeamId = _teams.Values
                .FirstOrDefault(team => team.Avatars.Length > 0)?.Id ?? 0;
        }

        player.State.CurrentAvatarTeamId = _currentTeamId;
    }

    private void PersistTeams()
    {
        player.State.AvatarTeams.Clear();
        player.State.AvatarTeams.Add(
            _teamState.OrderBy(pair => pair.Key).Select(pair => pair.Value));
    }

    private static PlayerTeam Snapshot(PlayerTeam team) => new() {
        Id = team.Id,
        Name = team.Name,
        Avatars = [.. team.Avatars],
        CurrentAvatarGuid = team.CurrentAvatarGuid
    };
}
