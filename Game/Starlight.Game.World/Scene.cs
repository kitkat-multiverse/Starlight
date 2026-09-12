using Starlight.Protocol;

namespace Starlight.Game.World;

/// <summary>One loaded scene inside a <see cref="World" />.</summary>
public sealed class Scene(World world, uint sceneId)
{
    private readonly Dictionary<uint, SceneEntity> _entities = [];
    private readonly Dictionary<uint, GadgetEntity> _gadgets = [];
    private readonly Dictionary<uint, MonsterEntity> _monsters = [];
    private readonly Dictionary<uint, EntityWeapon> _weaponEntities = [];

    /// The world that loaded this scene and allocates its entity IDs.
    public World World => world;

    public uint Id => sceneId;
    public IReadOnlyDictionary<uint, SceneEntity> Entities => _entities;
    public IReadOnlyDictionary<uint, MonsterEntity> Monsters => _monsters;
    public IReadOnlyDictionary<uint, EntityWeapon> WeaponEntities => _weaponEntities;
    public IReadOnlyDictionary<uint, GadgetEntity> Gadgets => _gadgets;

    public void AddEntity(SceneEntity entity)
    {
        if (!ReferenceEquals(entity.Scene, this))
            throw new ArgumentException("Entity belongs to a different scene.", nameof(entity));

        if (_entities.TryGetValue(entity.EntityId, out var previous) && !ReferenceEquals(previous, entity))
        {
            previous.Detach();

            if (previous is MonsterEntity)
                _monsters.Remove(entity.EntityId);

            if (previous is GadgetEntity)
                _gadgets.Remove(entity.EntityId);
        }

        _entities[entity.EntityId] = entity;

        if (entity is MonsterEntity monster)
            _monsters[entity.EntityId] = monster;

        if (entity is GadgetEntity gadget)
            _gadgets[entity.EntityId] = gadget;
    }

    public void AddMonster(MonsterEntity monster) => AddEntity(monster);
    public void AddGadget(GadgetEntity gadget) => AddEntity(gadget);

    public void AddWeapon(EntityWeapon weapon)
    {
        if (!ReferenceEquals(weapon.Scene, this))
            throw new ArgumentException("Weapon belongs to a different scene.", nameof(weapon));

        _weaponEntities[weapon.EntityId] = weapon;
    }

    public bool RemoveWeapon(uint entityId)
    {
        if (!_weaponEntities.Remove(entityId))
            return false;

        World.Abilities.Remove(entityId);
        return true;
    }

    public bool TryGetEntity(uint entityId, out SceneEntity entity) =>
        _entities.TryGetValue(entityId, out entity!);

    public SceneEntity? GetEntity(uint entityId) =>
        _entities.GetValueOrDefault(entityId);

    public SceneAttackResult HandleAttack(AttackResult attack)
    {
        if (attack.DefenseId == 0 || !TryGetEntity(attack.DefenseId, out var target))
            return default;

        // Temporary godmode until avatar lifesycle is implemented.
        if (target is AvatarEntity)
            return default;

        var damage = target.Damage(attack.Damage);
        return new SceneAttackResult(target, damage, attack.AttackerId);
    }

    public bool RemoveEntity(uint entityId)
    {
        _monsters.Remove(entityId);
        _gadgets.Remove(entityId);

        if (!_entities.Remove(entityId, out var entity))
            return false;

        entity.Detach();
        return true;
    }

    public bool RemoveMonster(uint entityId) => RemoveEntity(entityId);
}

public readonly record struct SceneAttackResult(
    SceneEntity? Target,
    EntityDamageResult Damage,
    uint AttackerId
)
{
    public bool Handled => Target is not null && Damage.Applied;
}
