using Starlight.Game.Ability;
using Starlight.Game.Player;
using Starlight.Protocol;

namespace Starlight.Game.World;

public sealed class EntityWeapon : IWeaponEntity
{
    private readonly AbilityComponent _abilities;

    public EntityWeapon(Scene scene, WeaponItem item)
    {
        Scene = scene;
        Item = item;
        EntityId = scene.World.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_WEAPON);

        var player = scene.World.Owner;

        _abilities = player.Module<AbilityModule>().RegisterWeapon(
            scene.World.Abilities,
            new AbilityOwner(
                EntityId,
                AbilityOwnerType.Weapon,
                scene.World.PeerIdOf(player),
                player.Uid),
            item.GadgetId);
    }

    public Scene Scene { get; }
    public WeaponItem Item { get; }
    public uint EntityId { get; }
    public AbilitySyncStateInfo AbilityInfo => AbilityProtocol.ToSyncState(_abilities);
}

public sealed class WeaponEntityService : IWeaponEntityService
{
    public void Equip(IPlayer player, Avatar avatar, WeaponItem weapon, bool refresh = false)
    {
        var scene = player.Module<WorldModule>().Scene;

        if (scene is null)
            return;

        var entity = weapon.WeaponEntity as EntityWeapon;

        if (entity is null || ReferenceEquals(entity.Scene, scene) || refresh)
        {
            if (entity is not null)
                entity.Scene.RemoveWeapon(entity.EntityId);

            entity = new EntityWeapon(scene, weapon);
            weapon.WeaponEntity = entity;
            scene.AddWeapon(entity);
        }

        var avatarEntity = scene.Entities.Values
            .OfType<AvatarEntity>()
            .FirstOrDefault(candidate => candidate.Avatar.Guid == avatar.Guid);

        if (avatarEntity is null)
            return;

        avatarEntity.SyncWeapon();

        if (player.Module<AbilityModule>().TryGetComponent(entity.EntityId, out var weaponAbilities))
        {
            weaponAbilities.UpdateOwner(new AbilityOwner(
                entity.EntityId,
                AbilityOwnerType.Weapon,
                scene.World.PeerIdOf(player),
                player.Uid,
                OwnerEntityId: avatarEntity.EntityId));
        }
    }

    public AbilityChangeNotify? RefreshAvatarAbilities(IPlayer player, Avatar avatar)
    {
        var scene = player.Module<WorldModule>().Scene;

        if (scene is null)
            return null;

        var avatarEntity = scene.Entities.Values
            .OfType<AvatarEntity>()
            .FirstOrDefault(candidate => candidate.Avatar.Guid == avatar.Guid);

        if (avatarEntity is null)
            return null;

        var abilities = player.Module<AbilityModule>();

        if (!abilities.TryGetComponent(avatarEntity.EntityId, out var component))
            return null;

        var previousEmbryos = component.Embryos
            .Select(embryo => (Name: embryo.Name.Hash, Override: embryo.Override.Hash))
            .ToArray();
        var weapon = avatar.Weapon;

        component = abilities.RegisterAvatar(
            scene.World.Abilities,
            new AbilityOwner(
                avatarEntity.EntityId,
                AbilityOwnerType.Avatar,
                scene.World.PeerIdOf(player),
                player.Uid),
            avatar.AvatarId,
            avatar.SkillDepotId,
            scene.Id,
            new AvatarAbilitySources(
                avatar.Talents,
                avatar.PromoteLevel,
                weapon?.AffixId ?? 0,
                weapon?.Refinement ?? 1),
            fightProperties: avatarEntity.FightProperties);

        var currentEmbryos = component.Embryos
            .Select(embryo => (Name: embryo.Name.Hash, Override: embryo.Override.Hash));

        if (previousEmbryos.SequenceEqual(currentEmbryos))
            return null;

        component.ResetClientState();

        return new AbilityChangeNotify {
            EntityId = avatarEntity.EntityId,
            AbilityControlBlock = AbilityProtocol.ToControlBlock(component)
        };
    }
}
