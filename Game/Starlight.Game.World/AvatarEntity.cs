using Starlight.Game;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Protocol;

namespace Starlight.Game.World;

public sealed class AvatarEntity : SceneEntity
{
    private AvatarEntity(
        Scene scene,
        SceneEntityInfo info,
        FightPropertyStore fightProperties,
        Avatar avatar
    ) : base(scene, info, fightProperties)
    {
        Avatar = avatar;
    }

    public Avatar Avatar { get; }
    public uint WeaponEntityId => Avatar.Weapon?.WeaponEntity?.EntityId ?? 0;
    public override uint AuthorityPeerId => Info.Avatar?.PeerId ?? 0;
    public override bool RemoveFromSceneOnDeath => false;

    public static AvatarEntity Create(
        Scene scene,
        uint uid,
        uint peerId,
        Avatar avatar,
        Vector position,
        Vector? rotation = null,
        Vector? refPos = null
    )
    {
        var world = scene.World;
        var inventory = world.Owner.Module<InventoryModule>();

        var weaponItem = avatar.Weapon ?? inventory.Weapons.FirstOrDefault(w => w.Guid == avatar.WeaponGuid)
            ?? throw new InvalidOperationException(
                $"Weapon {avatar.WeaponItemId} with GUID {avatar.WeaponGuid} not found in inventory");

        if (weaponItem.WeaponEntity is not EntityWeapon weaponEntity || !ReferenceEquals(weaponEntity.Scene, scene))
        {
            weaponEntity = new EntityWeapon(scene, weaponItem);
            weaponItem.WeaponEntity = weaponEntity;
            scene.AddWeapon(weaponEntity);
        }

        var sceneAvatar = new SceneAvatarInfo {
            Uid = uid,
            AvatarId = avatar.AvatarId,
            Guid = avatar.Guid,
            PeerId = peerId,
            SkillDepotId = avatar.SkillDepotId,
            BornTime = avatar.BornTime,
            WearingFlycloakId = Avatar.DefaultFlycloak,
            EquipIdList = [weaponItem.ItemId],
            Weapon = weaponItem.ToSceneProtocol()
        };

        avatar.PopulateSceneProgression(sceneAvatar);

        var info = new SceneEntityInfo {
            EntityType = ProtEntityType.PROT_ENTITY_TYPE_AVATAR,
            EntityId = world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_AVATAR),
            LifeState = avatar.GetFightProperty(FightProperty.FIGHT_PROP_CUR_HP) <= 0 ? 2u : 1u,
            MotionInfo = new MotionInfo {
                Pos = CopyVector(position),
                Rot = CopyVector(rotation),
                Speed = new Vector(),
                RefPos = CopyVector(refPos),
                State = MotionState.MOTION_STATE_STANDBY
            },
            EntityClientData = new EntityClientData(),
            EntityAuthorityInfo = new EntityAuthorityInfo {
                AbilityInfo = new AbilitySyncStateInfo(),
                BornPos = new Vector(),
                ClientExtraInfo = new EntityClientExtraInfo { SkillAnchorPosition = new Vector() }
            },
            PropList = [
                new PropPair { Type = (uint)PlayerProperty.Level, PropValue = PlayerProperty.Level.Value(avatar.Level) }
            ],
            Avatar = sceneAvatar
        };

        return new AvatarEntity(scene, info, avatar.FightPropertyStore, avatar);
    }

    public void SyncWeapon()
    {
        if (Info.Avatar is not {} sceneAvatar || Avatar.Weapon is not {} weapon)
            return;

        sceneAvatar.EquipIdList.Clear();
        sceneAvatar.EquipIdList.Add(weapon.ItemId);
        sceneAvatar.Weapon = weapon.ToSceneProtocol();
    }

    private static Vector CopyVector(Vector? source) =>
        source is null ? new Vector() : new Vector { X = source.X, Y = source.Y, Z = source.Z };
}
