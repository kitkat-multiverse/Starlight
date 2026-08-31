using Starlight.Game.Ability;
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

        var abilities = player.Module<AbilityModule>();
        var inventory = player.Module<InventoryModule>();
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

            if (!abilities.TryGetComponent(entity.EntityId, out var avatarAbilities))
            {
                avatarAbilities = abilities.RegisterAvatar(
                    module.World.Abilities,
                    new AbilityOwner(entity.EntityId, AbilityOwnerType.Avatar, module.PeerId, player.Uid),
                    avatar.AvatarId,
                    avatar.SkillDepotId,
                    scene.Id,
                    AbilitySources(avatar, inventory));
            }

            if (!abilities.TryGetComponent(entity.WeaponEntityId, out var weaponAbilities))
            {
                weaponAbilities = abilities.RegisterWeapon(
                    module.World.Abilities,
                    new AbilityOwner(entity.WeaponEntityId, AbilityOwnerType.Weapon, module.PeerId, player.Uid),
                    avatar.WeaponGadgetId);
            }
            entity.Info.EntityAuthorityInfo!.AbilityInfo = AbilityProtocol.ToSyncState(avatarAbilities);

            var isCurrent = avatar.Guid == team.CurrentAvatarGuid;

            notification.SceneTeamAvatarList.Add(new SceneTeamAvatar {
                PlayerUid = player.Uid,
                SceneId = scene.Id,
                AvatarGuid = avatar.Guid,
                EntityId = entity.EntityId,
                WeaponGuid = avatar.WeaponGuid,
                WeaponEntityId = entity.WeaponEntityId,
                AbilityControlBlock = AbilityProtocol.ToControlBlock(avatarAbilities),
                AvatarAbilityInfo = AbilityProtocol.ToSyncState(avatarAbilities),
                WeaponAbilityInfo = AbilityProtocol.ToSyncState(weaponAbilities),
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
        {
            _teamEntities.Add(guid, entity);
        }

        _currentAvatarGuid = team.CurrentAvatarGuid;

        yield return notification;

        if (currentChanged && previous is not null)
        {
            yield return new SceneEntityDisappearNotify {
                DisappearType = VisionType.VISION_TYPE_REPLACE,
                EntityList = { previous.EntityId }
            };
        }

        if (currentChanged && previous is not null && current is not null)
        {
            yield return new SceneEntityAppearNotify {
                AppearType = VisionType.VISION_TYPE_REPLACE,
                Param = previous.EntityId,
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
        var module = player.Module<WorldModule>();
        var world = module.World;
        var scene = module.Scene!;
        var abilities = player.Module<AbilityModule>();
        var inventory = player.Module<InventoryModule>();
        var teamEntityId = world.TeamEntityIdOf(player);
        var levelEntityId = world.LevelEntityId;

        abilities.RegisterScene(
            world.Abilities,
            scene.Id,
            new AbilityOwner(AbilityEntityIds.Scene, AbilityOwnerType.Scene));

        var teamAbilities = abilities.RegisterTeam(
            world.Abilities,
            new AbilityOwner(teamEntityId, AbilityOwnerType.Team, module.PeerId, player.Uid),
            scene.Id);

        var level = abilities.RegisterMpLevel(
            world.Abilities,
            new AbilityOwner(levelEntityId, AbilityOwnerType.MpLevel, world.HostPeerId));

        var enterInfo = new PlayerEnterSceneInfoNotify {
            EnterSceneToken = msg.EnterSceneToken,
            TeamEnterInfo = new TeamEnterSceneInfo {
                AbilityControlBlock = AbilityProtocol.ToControlBlock(teamAbilities),
                TeamAbilityInfo = AbilityProtocol.ToSyncState(teamAbilities),
                TeamEntityId = teamEntityId
            },
            MpLevelEntityInfo = new MPLevelEntityInfo {
                EntityId = levelEntityId,
                AbilityInfo = AbilityProtocol.ToSyncState(level),
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

            var avatarAbilities = abilities.RegisterAvatar(
                world.Abilities,
                new AbilityOwner(entity.EntityId, AbilityOwnerType.Avatar, module.PeerId, player.Uid),
                avatar.AvatarId,
                avatar.SkillDepotId,
                scene.Id,
                AbilitySources(avatar, inventory));

            var weaponAbilities = abilities.RegisterWeapon(
                world.Abilities,
                new AbilityOwner(entity.WeaponEntityId, AbilityOwnerType.Weapon, module.PeerId, player.Uid),
                avatar.WeaponGadgetId);
            entity.Info.EntityAuthorityInfo!.AbilityInfo = AbilityProtocol.ToSyncState(avatarAbilities);

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
                WeaponEntityId = entity.WeaponEntityId,
                AvatarAbilityInfo = AbilityProtocol.ToSyncState(avatarAbilities),
                WeaponAbilityInfo = AbilityProtocol.ToSyncState(weaponAbilities)
            });

            teamUpdate.SceneTeamAvatarList.Add(new SceneTeamAvatar {
                PlayerUid = player.Uid,
                SceneId = scene.Id,
                AvatarGuid = avatar.Guid,
                EntityId = entity.EntityId,
                WeaponGuid = avatar.WeaponGuid,
                WeaponEntityId = entity.WeaponEntityId,
                AbilityControlBlock = AbilityProtocol.ToControlBlock(avatarAbilities),
                AvatarAbilityInfo = AbilityProtocol.ToSyncState(avatarAbilities),
                WeaponAbilityInfo = AbilityProtocol.ToSyncState(weaponAbilities),
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

    private static AvatarAbilitySources AbilitySources(Avatar avatar, InventoryModule inventory)
    {
        inventory.TryGetWeapon(avatar.WeaponGuid, out var weapon);

        return new AvatarAbilitySources(
            avatar.Talents,
            avatar.PromoteLevel,
            weapon?.AffixId ?? 0,
            weapon?.Refinement ?? 1);
    }

    private static PlayerEnterSceneNotify EnterScene() => new() {
        Type = EnterType.ENTER_TYPE_ENTER_SELF,
        SceneId = SpawnSceneId,
        Pos = SpawnPosition
    };
}
