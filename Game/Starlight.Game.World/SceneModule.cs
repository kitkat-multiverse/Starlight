using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protobuf.Core;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.World;

public sealed class SceneModule(IPlayer player) : IModule
{
    #region Beach Simulator

    // These constants are here until we get a permanent solution
    // scaffolded out and properly implemented.

    private const uint SpawnSceneId = 3;
    private static readonly Vector SpawnPosition = new() { X = 2747, Y = 194, Z = -1719 };

    private readonly List<SceneEntityInfo> _spawned = [];
    private readonly Dictionary<ulong, AvatarEntity> _teamEntities = [];
    private ulong _currentAvatarGuid;

    #endregion

    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public PlayerEnterSceneNotify? OnLogin()
        => player.State.BornState == NetPlayerState.Types.PlayerBornState.Pending ? null : EnterScene();

    [Lifecycle(LifecycleEvent.PlayerBorn)]
    public PlayerEnterSceneNotify OnBorn()
    {
        player.Module<WorldModule>().EnterOwnWorld();
        return EnterScene();
    }

    [Lifecycle(LifecycleEvent.PlayerTeamChanged)]
    public IEnumerable<IMessage> OnTeamChanged()
    {
        var module = player.Module<WorldModule>();
        var scene = module.Scene;

        if (scene is null)
            yield break;

        var team = player.Module<TeamModule>().Current;
        var notification = new SceneTeamUpdateNotify();
        var nextEntities = new Dictionary<ulong, AvatarEntity>();

        foreach (var avatar in team.Avatars)
        {
            if (!_teamEntities.TryGetValue(avatar.Guid, out var entity))
            {
                entity = AvatarEntity.Create(
                    module.World,
                    player.Uid,
                    module.PeerId,
                    avatar,
                    SpawnPosition);
            }

            nextEntities.Add(avatar.Guid, entity);

            var isCurrent = avatar.Guid == team.CurrentAvatarGuid;

            notification.SceneTeamAvatarList.Add(new SceneTeamAvatar {
                PlayerUid = player.Uid,
                SceneId = scene.Id,
                AvatarGuid = avatar.Guid,
                EntityId = entity.EntityId,
                WeaponGuid = avatar.WeaponGuid,
                WeaponEntityId = entity.WeaponEntityId,
                AbilityControlBlock = avatar.ControlBlock(),
                AvatarAbilityInfo = new AbilitySyncStateInfo(),
                WeaponAbilityInfo = new AbilitySyncStateInfo(),
                SceneEntityInfo = entity.Info,
                IsOnScene = isCurrent,
                IsPlayerCurAvatar = isCurrent
            });
        }

        _teamEntities.TryGetValue(_currentAvatarGuid, out var previous);
        nextEntities.TryGetValue(team.CurrentAvatarGuid, out var current);
        var currentChanged = _currentAvatarGuid != team.CurrentAvatarGuid;

        _teamEntities.Clear();

        foreach (var (guid, entity) in nextEntities)
            _teamEntities.Add(guid, entity);

        _currentAvatarGuid = team.CurrentAvatarGuid;

        yield return notification;

        if (currentChanged && previous is not null)
        {
            yield return new SceneEntityDisappearNotify {
                DisappearType = VisionType.VISION_TYPE_REPLACE,
                EntityList = { previous.EntityId }
            };
        }

        if (currentChanged && current is not null)
        {
            yield return new SceneEntityAppearNotify {
                AppearType = VisionType.VISION_TYPE_REPLACE,
                EntityList = { current.Info }
            };
        }
    }

    [Opcode]
    public IEnumerable<IMessage> OnEnterSceneReady(EnterSceneReadyReq msg)
    {
        // TODO: Validate `enter_scene_token`.

        var module = player.Module<WorldModule>();

        // TODO: Fetch player's last scene.
        var scene = module.Scene = module.World.GetScene(SpawnSceneId);

        yield return new EnterScenePeerNotify {
            DestSceneId = scene.Id,
            HostPeerId = module.World.HostPeerId,
            PeerId = module.PeerId,
            EnterSceneToken = msg.EnterSceneToken
        };

        yield return new EnterSceneReadyRsp { EnterSceneToken = msg.EnterSceneToken };
    }

    [Opcode]
    public IEnumerable<IMessage> OnSceneInit(SceneInitFinishReq msg)
    {
        // TODO: Validate `enter_scene_token`.

        var module = player.Module<WorldModule>();
        var world = module.World;
        var scene = module.Scene!;

        var enterInfo = new PlayerEnterSceneInfoNotify {
            EnterSceneToken = msg.EnterSceneToken,
            TeamEnterInfo = new TeamEnterSceneInfo {
                AbilityControlBlock = new AbilityControlBlock(),
                TeamAbilityInfo = new AbilitySyncStateInfo(),
                TeamEntityId = world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_TEAM)
            },
            MpLevelEntityInfo = new MPLevelEntityInfo {
                EntityId = world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_MP_LEVEL),
                AbilityInfo = new AbilitySyncStateInfo(),
                AuthorityPeerId = world.HostPeerId
            }
        };

        var teamUpdate = new SceneTeamUpdateNotify();
        var team = player.Module<TeamModule>().Current;

        _spawned.Clear();
        _teamEntities.Clear();
        _currentAvatarGuid = team.CurrentAvatarGuid;

        foreach (var avatar in team.Avatars)
        {
            var entity = AvatarEntity.Create(world, player.Uid, module.PeerId, avatar, SpawnPosition);
            _teamEntities.Add(avatar.Guid, entity);

            var isCurrent = avatar.Guid == team.CurrentAvatarGuid;

            if (isCurrent)
            {
                enterInfo.CurAvatarEntityId = entity.EntityId;
                _spawned.Add(entity.Info);
            }

            enterInfo.AvatarEnterInfo.Add(new AvatarEnterSceneInfo {
                AvatarGuid = avatar.Guid,
                AvatarEntityId = entity.EntityId,
                WeaponGuid = avatar.WeaponGuid,
                WeaponEntityId = entity.WeaponEntityId
            });

            teamUpdate.SceneTeamAvatarList.Add(new SceneTeamAvatar {
                PlayerUid = player.Uid,
                SceneId = scene.Id,
                AvatarGuid = avatar.Guid,
                EntityId = entity.EntityId,
                WeaponGuid = avatar.WeaponGuid,
                WeaponEntityId = entity.WeaponEntityId,
                AbilityControlBlock = avatar.ControlBlock(),
                AvatarAbilityInfo = new AbilitySyncStateInfo(),
                WeaponAbilityInfo = new AbilitySyncStateInfo(),
                SceneEntityInfo = entity.Info,
                IsOnScene = isCurrent,
                IsPlayerCurAvatar = isCurrent
            });
        }

        yield return enterInfo;
        yield return teamUpdate;
        yield return new SceneInitFinishRsp { EnterSceneToken = msg.EnterSceneToken };
    }

    [Opcode]
    public IEnumerable<IMessage> OnEnterSceneDone(EnterSceneDoneReq msg)
    {
        // TODO: Validate `enter_scene_token`.

        yield return new SceneEntityAppearNotify {
            AppearType = VisionType.VISION_TYPE_BORN,
            EntityList = [.. _spawned]
        };

        yield return new EnterSceneDoneRsp { EnterSceneToken = msg.EnterSceneToken };
    }

    [Opcode]
    public PostEnterSceneRsp OnPostEnterScene(PostEnterSceneReq msg) =>
        // TODO: Validate `enter_scene_token`.
        new() { EnterSceneToken = msg.EnterSceneToken };

    private static PlayerEnterSceneNotify EnterScene() => new() {
        Type = EnterType.ENTER_TYPE_ENTER_SELF,
        SceneId = SpawnSceneId,
        Pos = SpawnPosition
    };
}
