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

        _spawned.Clear();

        foreach (var (slot, avatar) in player.Module<AvatarModule>().Team.Index())
        {
            var entity = AvatarEntity.Create(world, player.Uid, module.PeerId, avatar, SpawnPosition);

            var isCurrent = slot == 0;

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
