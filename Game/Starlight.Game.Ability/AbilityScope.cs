namespace Starlight.Game.Ability;

public static class AbilityEntityIds
{
    public const uint Scene = 0x02700001;
}

public sealed class AbilityScope
{
    private readonly Dictionary<uint, AbilityComponent> _components = [];
    private readonly Dictionary<uint, AbilityComponent> _sceneComponents = [];

    public IReadOnlyDictionary<uint, AbilityComponent> Components => _components;
    public IReadOnlyDictionary<uint, AbilityComponent> SceneComponents => _sceneComponents;

    public AbilityComponent Register(AbilityOwner owner, IEnumerable<string>? embryos = null)
    {
        if (_components.TryGetValue(owner.EntityId, out var existing))
        {
            existing.UpdateOwner(owner);

            if (embryos is not null && existing.Embryos.Count == 0)
                existing.ResetEmbryos(embryos);
            return existing;
        }

        var component = Create(owner, embryos);
        _components[owner.EntityId] = component;
        return component;
    }

    public AbilityComponent RegisterScene(uint sceneId, AbilityOwner owner, IEnumerable<string>? embryos = null)
    {
        if (owner.Type != AbilityOwnerType.Scene || owner.EntityId != AbilityEntityIds.Scene)
            throw new ArgumentException("Scene ability owners must use the scene pseudo-entity ID.", nameof(owner));

        if (_sceneComponents.TryGetValue(sceneId, out var existing))
        {
            existing.UpdateOwner(owner);

            if (embryos is not null && existing.Embryos.Count == 0)
                existing.ResetEmbryos(embryos);
            return existing;
        }

        var component = Create(owner, embryos);
        _sceneComponents[sceneId] = component;
        return component;
    }

    public AbilityComponent Replace(AbilityOwner owner, IEnumerable<string>? embryos = null)
    {
        _components.Remove(owner.EntityId);
        return Register(owner, embryos);
    }

    public AbilityComponent ReplaceScene(uint sceneId, AbilityOwner owner, IEnumerable<string>? embryos = null)
    {
        _sceneComponents.Remove(sceneId);
        return RegisterScene(sceneId, owner, embryos);
    }

    public bool TryGet(uint entityId, out AbilityComponent component) =>
        _components.TryGetValue(entityId, out component!);

    public bool TryGet(uint sceneId, uint entityId, out AbilityComponent component)
    {
        if (entityId == AbilityEntityIds.Scene)
            return _sceneComponents.TryGetValue(sceneId, out component!);

        return TryGet(entityId, out component);
    }

    public AbilityComponent Get(uint entityId) =>
        _components.TryGetValue(entityId, out var component) ?
            component :
            throw new KeyNotFoundException($"Ability component for entity {entityId} was not registered.");

    public AbilityComponent Get(uint sceneId, uint entityId) =>
        TryGet(sceneId, entityId, out var component) ?
            component :
            throw new KeyNotFoundException(
                $"Ability component for entity {entityId} was not registered in scene {sceneId}.");

    public bool Remove(uint entityId) => _components.Remove(entityId);
    public bool RemoveScene(uint sceneId) => _sceneComponents.Remove(sceneId);

    public int RemoveOwnedByPlayer(uint playerUid)
    {
        var ids = _components
            .Where(pair => pair.Value.Owner.PlayerUid == playerUid)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var id in ids)
        {
            _components.Remove(id);
        }
        return ids.Length;
    }

    public void Clear()
    {
        _components.Clear();
        _sceneComponents.Clear();
    }

    private static AbilityComponent Create(AbilityOwner owner, IEnumerable<string>? embryos)
    {
        var component = new AbilityComponent(owner);

        if (embryos is not null)
            component.ResetEmbryos(embryos);
        return component;
    }
}
