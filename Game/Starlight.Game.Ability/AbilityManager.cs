using Google.Protobuf;
using Starlight.Game.Ability.Handlers;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability;

public sealed class AbilityModule(
    IPlayer player,
    AbilityInitializer initializer,
    GameData data,
    ProtocolRegistry protocol,
    AbilityRuntimeConfig config,
    AbilityInvokeHandlerRegistry handlers,
    IAbilityScopeResolver scopes,
    IInvokeForwarder forwarder
) : IModule
{
    private const int RuntimeInvokeLimit = 49;
    private const int CombinedEntityLimit = 50;
    private const int CombinedInvokeLimit = 50;
    private const int AbilityChangeInvokeLimit = 500;

    public event Func<AbilityContext, ValueTask>? Invocation;

    public AbilityComponent RegisterScene(
        AbilityScope scope,
        uint sceneId,
        AbilityOwner owner,
        IEnumerable<string>? additionalLevelConfigs = null
    ) =>
        initializer.RegisterScene(scope, sceneId, owner, additionalLevelConfigs);

    public AbilityComponent RegisterTeam(
        AbilityScope scope,
        AbilityOwner owner,
        uint sceneId,
        IEnumerable<string>? abilityGroups = null,
        IEnumerable<string>? additionalLevelConfigs = null
    ) =>
        initializer.RegisterTeam(scope, owner, sceneId, abilityGroups, additionalLevelConfigs);

    public AbilityComponent RegisterMpLevel(AbilityScope scope, AbilityOwner owner) =>
        initializer.RegisterMpLevel(scope, owner);

    public AbilityComponent RegisterAvatar(
        AbilityScope scope,
        AbilityOwner owner,
        uint avatarId,
        uint skillDepotId,
        uint sceneId,
        AvatarAbilitySources? sources = null,
        IEnumerable<AbilityEmbryoSeed>? additional = null,
        IEnumerable<string>? additionalLevelConfigs = null
    ) =>
        initializer.RegisterAvatar(
            scope, owner, avatarId, skillDepotId, sceneId, sources, additional, additionalLevelConfigs);

    public AbilityComponent RegisterWeapon(AbilityScope scope, AbilityOwner owner, uint gadgetId) =>
        initializer.RegisterWeapon(scope, owner, gadgetId);

    public AbilityComponent RegisterGadget(AbilityScope scope, AbilityOwner owner, uint gadgetId) =>
        initializer.RegisterGadget(scope, owner, gadgetId);

    public AbilityComponent RegisterClientGadget(AbilityScope scope, AbilityOwner owner, uint gadgetId) =>
        initializer.RegisterClientGadget(scope, owner, gadgetId);

    public AbilityComponent RegisterMonster(
        AbilityScope scope,
        AbilityOwner owner,
        uint monsterId,
        uint sceneId,
        IEnumerable<uint>? groupAffixes = null,
        bool isElite = false,
        bool isLightConfig = false,
        IEnumerable<string>? additionalLevelConfigs = null
    ) =>
        initializer.RegisterMonster(
            scope, owner, monsterId, sceneId, groupAffixes, isElite, isLightConfig, additionalLevelConfigs);

    public void Append(AbilityComponent component, IEnumerable<AbilityEmbryoSeed> abilities) =>
        initializer.Append(component, abilities);

    public AbilityScope CurrentScope => ResolveScope().Scope;

    public bool TryGetComponent(uint entityId, out AbilityComponent component)
    {
        component = null!;
        return scopes.TryResolve(player, out var context) && context.TryGet(entityId, out component);
    }

    public bool TryGetComponent(uint sceneId, uint entityId, out AbilityComponent component)
    {
        component = null!;

        return scopes.TryResolve(player, out var context) &&
               context.Scope.TryGet(sceneId, entityId, out component);
    }

    public AbilityComponent GetComponent(uint entityId)
    {
        var context = ResolveScope();

        if (context.TryGet(entityId, out var component))
            return component;

        throw new KeyNotFoundException(
            $"Ability component for entity {entityId} is not registered in scene {context.SceneId}.");
    }

    public AbilityComponent GetComponent(uint sceneId, uint entityId)
    {
        var context = ResolveScope();

        if (context.Scope.TryGet(sceneId, entityId, out var component))
            return component;

        throw new KeyNotFoundException(
            $"Ability component for entity {entityId} is not registered in scene {sceneId}.");
    }

    public AbilityControlBlock GetControlBlock(uint entityId) =>
        AbilityProtocol.ToControlBlock(GetComponent(entityId));

    public AbilityControlBlock GetControlBlock(uint sceneId, uint entityId) =>
        AbilityProtocol.ToControlBlock(GetComponent(sceneId, entityId));

    public AbilitySyncStateInfo GetSyncState(uint entityId) =>
        AbilityProtocol.ToSyncState(GetComponent(entityId));

    public AbilitySyncStateInfo GetSyncState(uint sceneId, uint entityId) =>
        AbilityProtocol.ToSyncState(GetComponent(sceneId, entityId));

    [Opcode]
    public async ValueTask OnAbilityInvocations(AbilityInvocationsNotify notify)
    {
        if (notify.Invokes.Count > RuntimeInvokeLimit || !scopes.TryResolve(player, out var world))
            return;

        foreach (var invoke in notify.Invokes)
        {
            await Dispatch(world, invoke);
        }

        await ForwardRuntime(notify.Invokes);
    }

    [Opcode]
    public async ValueTask OnClientAbilityInitFinish(ClientAbilityInitFinishNotify notify)
    {
        if (!scopes.TryResolve(player, out var world) || !world.TryGet(notify.EntityId, out var main))
            return;

        if (!CanHandle(main, world.PeerId, ignoreAuth: false) || notify.Invokes.Count > main.Owner.EffectiveClientInitInvokeLimit)
            return;

        foreach (var invoke in notify.Invokes)
        {
            await Dispatch(world, invoke, notify.EntityId);
        }

        main.MarkClientInitialized();
        await ForwardInitFinish(notify.EntityId, notify.Invokes);
    }

    [Opcode]
    public async ValueTask OnClientAbilitiesInitFinishCombine(ClientAbilitiesInitFinishCombineNotify notify)
    {
        if (notify.EntityInvokeList.Count > CombinedEntityLimit || !scopes.TryResolve(player, out var world))
            return;

        foreach (var entityInvoke in notify.EntityInvokeList)
        {
            if (entityInvoke.Invokes.Count > CombinedInvokeLimit ||
                !world.TryGet(entityInvoke.EntityId, out var component) ||
                !CanHandle(component, world.PeerId, ignoreAuth: false))
                continue;

            foreach (var invoke in entityInvoke.Invokes)
            {
                await Dispatch(world, invoke, entityInvoke.EntityId);
            }

            component.MarkClientInitialized();
        }

        await ForwardCombined(notify.EntityInvokeList);
    }

    [Opcode]
    public async ValueTask OnClientAbilityChange(ClientAbilityChangeNotify notify)
    {
        if (!scopes.TryResolve(player, out var world) || !world.TryGet(notify.EntityId, out var component))
            return;

        if (component.Owner.Type is not (AbilityOwnerType.Avatar or AbilityOwnerType.Team) ||
            !CanHandle(component, world.PeerId, ignoreAuth: false) ||
            notify.Invokes.Count > AbilityChangeInvokeLimit)
            return;

        foreach (var invoke in notify.Invokes)
        {
            await Dispatch(world, invoke, notify.EntityId);
        }

        component.MarkClientInitialized();
        await ForwardAbilityChange(notify.EntityId, notify.IsInitHash, notify.Invokes);
    }

    public async ValueTask Dispatch(
        AbilityScopeContext world,
        AbilityInvokeEntry invoke,
        uint fallbackEntityId = 0
    )
    {
        var entityId = invoke.EntityId != 0 ? invoke.EntityId : fallbackEntityId;

        if (entityId == 0 || !world.TryGet(entityId, out var source) || !CanHandle(source, world.PeerId, invoke.IsIgnoreAuth))
            return;

        var head = invoke.Head ?? new AbilityInvokeEntryHead();
        var target = source;

        if (head.TargetId != 0 && world.TryGet(head.TargetId, out var resolvedTarget))
            target = resolvedTarget;

        AbilityInstance? ability = null;
        AbilityModifierInstance? modifier = null;

        if (head.InstancedModifierId != 0 && source.TryGetModifier(head.InstancedModifierId, out modifier!))
        {
            if (modifier.InstancedAbilityId != 0)
                source.TryGetAbility(modifier.InstancedAbilityId, out ability!);
        }

        if (ability is null && head.InstancedAbilityId != 0)
        {
            source.TryGetAbility(head.InstancedAbilityId, out ability!);

            if (target != source)
                target.TryGetAbility(head.InstancedAbilityId, out ability!);
        }

        // Moved to abstract handlers, kept as comment for the reference
        // DO NOT DELETE
        /*
        if (head.LocalId == 0)
        {
            switch (invoke.ArgumentType)
            {
                case AbilityInvokeArgument.ABILITY_META_MODIFIER_CHANGE:
                    modifier = HandleModifierChange(world, source, invoke, head);
                    break;
                case AbilityInvokeArgument.ABILITY_META_OVERRIDE_PARAM:
                    HandleOverrideParam(source, invoke, head);
                    break;
                case AbilityInvokeArgument.ABILITY_META_CLEAR_OVERRIDE_PARAM:
                    HandleClearOverrideParam(source, invoke, head);
                    break;
                case AbilityInvokeArgument.ABILITY_META_REINIT_OVERRIDEMAP:
                    HandleReinitializeOverrideMap(source, invoke, head);
                    break;
                case AbilityInvokeArgument.ABILITY_META_GLOBAL_FLOAT_VALUE:
                    HandleGlobalValue(source, invoke);
                    break;
                case AbilityInvokeArgument.ABILITY_META_CLEAR_GLOBAL_FLOAT_VALUE:
                    HandleClearGlobalValue(source, invoke);
                    break;
                case AbilityInvokeArgument.ABILITY_META_SET_KILLED_STATE:
                    HandleKilledState(source, invoke);
                    break;
                case AbilityInvokeArgument.ABILITY_META_ADD_NEW_ABILITY:
                    ability = HandleAddAbility(source, invoke);
                    break;
                case AbilityInvokeArgument.ABILITY_META_REMOVE_ABILITY:
                    if (head.InstancedAbilityId != 0)
                        source.RemoveAbility(head.InstancedAbilityId);
                    break;
                case AbilityInvokeArgument.ABILITY_META_SET_MODIFIER_APPLY_ENTITY:
                    HandleSetModifierApplyEntity(source, invoke, head);
                    break;
                case AbilityInvokeArgument.ABILITY_META_MODIFIER_DURABILITY_CHANGE:
                    HandleModifierDurability(source, invoke, head);
                    break;
            }
        }
        */

        ability ??= head.InstancedAbilityId != 0 && source.TryGetAbility(head.InstancedAbilityId, out var current) ? current : null;

        modifier ??= head.InstancedModifierId != 0 && source.TryGetModifier(head.InstancedModifierId, out var currentModifier) ?
            currentModifier :
            null;

        if (ability is not null && ability.Definition is null)
            ability.Definition = ResolveAbility(ability.Name);

        var definition = ability?.Definition;
        var action = head.LocalId == 0 ? null : definition?.ResolveAction(head.LocalId);
        var mixin = head.LocalId == 0 || action is not null ? null : definition?.ResolveMixin(head.LocalId);

        var context = new AbilityContext(
            player,
            world,
            config,
            invoke,
            source,
            target,
            ability,
            modifier,
            definition,
            action,
            mixin);

        await handlers.DispatchAsync(context);

        ability = head.InstancedAbilityId != 0 && source.TryGetAbility(head.InstancedAbilityId, out var dispatchedAbility) ?
            dispatchedAbility :
            ability;

        modifier = head.InstancedModifierId != 0 && source.TryGetModifier(head.InstancedModifierId, out var dispatchedModifier) ?
            dispatchedModifier :
            modifier;

        await Publish(context with { Ability = ability, Modifier = modifier });
    }

    #region DO NOT DELETE, KEPT AS COMMENT FOR REFERENCE

    /*
    private AbilityModifierInstance? HandleModifierChange(
        AbilityScopeContext world,
        AbilityComponent source,
        AbilityInvokeEntry invoke,
        AbilityInvokeEntryHead head
    )
    {
        if (!TryDecode<AbilityMetaModifierChange>(invoke.AbilityData, out var change))
            return null;

        if (change.Action == ModifierAction.MODIFIER_ACTION_REMOVED)
        {
            source.RemoveModifier(head.InstancedModifierId);
            return null;
        }

        if (head.InstancedModifierId == 0 || head.InstancedAbilityId == 0 && head.ServerBuffUid == 0)
            return null;

        var parentComponent = source;
        AbilityInstance? parentAbility = null;

        if (head.TargetId != 0 && world.TryGet(head.TargetId, out var target) &&
            target.TryGetAbility(head.InstancedAbilityId, out var targetAbility))
        {
            parentComponent = target;
            parentAbility = targetAbility;
        } else if (head.InstancedAbilityId != 0)
        {
            source.TryGetAbility(head.InstancedAbilityId, out parentAbility!);
        }

        var parentName = parentAbility?.Name ?? AbilityProtocol.FromAbilityString(change.ParentAbilityName);
        var parentOverride = parentAbility?.Override ?? AbilityProtocol.FromAbilityString(change.ParentAbilityOverride);
        var definition = parentAbility?.Definition ?? ResolveAbility(parentName);

        if (parentAbility is null && head.InstancedAbilityId != 0 && parentName != AbilityKey.Default)
        {
            parentAbility = parentComponent.UpsertAbility(
                head.InstancedAbilityId,
                parentName,
                parentOverride,
                definition);
        }

        var modifier = new AbilityModifierInstance(
            head.InstancedModifierId,
            head.InstancedAbilityId,
            change.ModifierLocalId,
            parentComponent.Owner.EntityId,
            parentName,
            parentOverride) {
            ModifierName = definition?.ModifierName(change.ModifierLocalId),
            ApplyEntityId = change.ApplyEntityId,
            IsAttachedParentAbility = change.IsAttachedParentAbility,
            IsDurabilityZero = change.IsDurabilityZero,
            HasDurability = change.IsDurabilityZero,
            RemainingDurability = 0,
            ServerBuffUid = head.ServerBuffUid != 0 ? head.ServerBuffUid : change.ServerBuffUid,
            IsServerBuffModifier = head.ServerBuffUid != 0 || change.ServerBuffUid != 0
        };

        if (change.AttachedInstancedModifier is {} attached)
        {
            modifier.HasAttachedModifier = true;
            modifier.AttachedModifierInvalid = attached.IsInvalid;
            modifier.AttachedModifierOwnerEntityId = attached.OwnerEntityId;
            modifier.AttachedInstancedModifierId = attached.InstancedModifierId;
            modifier.AttachedIsServerBuffModifier = attached.IsServerbuffModifier;
            modifier.AttachedNameHash = attached.AttachNameHash;
        }

        return source.UpsertModifier(modifier);
    }

    private void HandleOverrideParam(AbilityComponent source, AbilityInvokeEntry invoke, AbilityInvokeEntryHead head)
    {
        if (!source.TryGetAbility(head.InstancedAbilityId, out var ability) ||
            !TryDecode<AbilityScalarValueEntry>(invoke.AbilityData, out var value) ||
            value.Key is null)
            return;

        ability.SetOverride(AbilityProtocol.FromAbilityString(value.Key), AbilityProtocol.FromScalarEntry(value));
    }

    private void HandleClearOverrideParam(AbilityComponent source, AbilityInvokeEntry invoke, AbilityInvokeEntryHead head)
    {
        if (!source.TryGetAbility(head.InstancedAbilityId, out var ability) ||
            !TryDecode<AbilityString>(invoke.AbilityData, out var key))
            return;

        ability.ClearOverride(AbilityProtocol.FromAbilityString(key));
    }

    private void HandleReinitializeOverrideMap(AbilityComponent source, AbilityInvokeEntry invoke, AbilityInvokeEntryHead head)
    {
        if (!source.TryGetAbility(head.InstancedAbilityId, out var ability) ||
            !TryDecode<AbilityMetaReInitOverrideMap>(invoke.AbilityData, out var reinitialize))
            return;

        ability.ReinitializeOverrides(reinitialize.OverrideMap
            .Where(value => value.Key is not null)
            .Select(value => new KeyValuePair<AbilityKey, AbilityScalarValue>(
                AbilityProtocol.FromAbilityString(value.Key),
                AbilityProtocol.FromScalarEntry(value))));
    }

    private void HandleGlobalValue(AbilityComponent source, AbilityInvokeEntry invoke)
    {
        if (!TryDecode<AbilityScalarValueEntry>(invoke.AbilityData, out var entry) || entry.Key is null)
            return;

        var key = AbilityProtocol.FromAbilityString(entry.Key);

        if (IsServerGlobalValue(key))
            return;

        var value = AbilityProtocol.FromScalarEntry(entry);

        if (value.Kind == AbilityScalarKind.Float && !float.IsFinite(value.FloatValue))
            return;

        source.SetDynamicValue(key, value);
    }

    private void HandleClearGlobalValue(AbilityComponent source, AbilityInvokeEntry invoke)
    {
        if (!TryDecode<AbilityString>(invoke.AbilityData, out var proto))
            return;

        var key = AbilityProtocol.FromAbilityString(proto);

        if (!IsServerGlobalValue(key))
            source.ClearDynamicValue(key);
    }

    private void HandleKilledState(AbilityComponent source, AbilityInvokeEntry invoke)
    {
        if (TryDecode<AbilityMetaSetKilledState>(invoke.AbilityData, out var state))
            source.SetKilled(state.Killed);
    }

    private AbilityInstance? HandleAddAbility(AbilityComponent source, AbilityInvokeEntry invoke)
    {
        if (!TryDecode<AbilityMetaAddAbility>(invoke.AbilityData, out var add) ||
            add.Ability is not { InstancedAbilityId: not 0 } applied ||
            applied.AbilityName is null)
            return null;

        var name = AbilityProtocol.FromAbilityString(applied.AbilityName);
        var @override = AbilityProtocol.FromAbilityString(applied.AbilityOverride);

        var ability = source.UpsertAbility(
            applied.InstancedAbilityId,
            name,
            @override,
            ResolveAbility(name));

        ability.ReinitializeOverrides(applied.OverrideMap
            .Where(value => value.Key is not null)
            .Select(value => new KeyValuePair<AbilityKey, AbilityScalarValue>(
                AbilityProtocol.FromAbilityString(value.Key),
                AbilityProtocol.FromScalarEntry(value))));

        return ability;
    }

    private void HandleSetModifierApplyEntity(
        AbilityComponent source,
        AbilityInvokeEntry invoke,
        AbilityInvokeEntryHead head
    )
    {
        if (!source.TryGetModifier(head.InstancedModifierId, out var modifier) ||
            !TryDecode<AbilityMetaSetModifierApplyEntityId>(invoke.AbilityData, out var change))
            return;

        modifier.ApplyEntityId = change.ApplyEntityId;
    }

    private void HandleModifierDurability(
        AbilityComponent source,
        AbilityInvokeEntry invoke,
        AbilityInvokeEntryHead head
    )
    {
        if (!source.TryGetModifier(head.InstancedModifierId, out var modifier) ||
            !TryDecode<AbilityMetaModifierDurabilityChange>(invoke.AbilityData, out var change))
            return;

        modifier.HasDurability = true;
        modifier.ReduceRatio = change.ReduceDurability;
        modifier.RemainingDurability = change.RemainDurability;
        modifier.IsDurabilityZero = change.RemainDurability <= 0;
    }
    */

    #endregion

    private Resources.Binary.AbilityConfig? ResolveAbility(AbilityKey key) =>
        key.Name is not null ? data.ResolveAbility(key.Name) ?? data.ResolveAbility(key.Hash) : data.ResolveAbility(key.Hash);

    private bool IsServerGlobalValue(AbilityKey key) =>
        key.Name?.StartsWith("SGV_", StringComparison.Ordinal) == true ||
        data.ServerGlobalValueHashes.Contains(key.Hash);

    private bool TryDecode<T>(ByteString data, out T message)
        where T : class, Starlight.Protobuf.Core.IMessage, new()
    {
        message = new T();

        try
        {
            using var input = data.CreateCodedInput();
            protocol.Deserialize(message, input);
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            message = null!;
            return false;
        }
    }

    private AbilityScopeContext ResolveScope()
    {
        if (scopes.TryResolve(player, out var context))
            return context;

        throw new InvalidOperationException("Player is not attached to an ability scope.");
    }

    private async ValueTask Publish(AbilityContext context)
    {
        if (Invocation is null)
            return;

        foreach (var handler in Invocation.GetInvocationList().Cast<Func<AbilityContext, ValueTask>>())
        {
            await handler(context);
        }
    }

    private async Task ForwardRuntime(IEnumerable<AbilityInvokeEntry> invokes)
    {
        foreach (var group in GroupForForwarding(invokes))
        {
            await forwarder.Forward(
                player,
                group.Key.Type,
                new AbilityInvocationsNotify { Invokes = [.. group] },
                group.Key.Peer);
        }
    }

    private async Task ForwardInitFinish(uint entityId, IEnumerable<AbilityInvokeEntry> invokes)
    {
        foreach (var group in GroupForForwarding(invokes))
        {
            await forwarder.Forward(
                player,
                group.Key.Type,
                new ClientAbilityInitFinishNotify { EntityId = entityId, Invokes = [.. group] },
                group.Key.Peer);
        }
    }

    private async Task ForwardCombined(IEnumerable<EntityAbilityInvokeEntry> entries)
    {
        var grouped = new Dictionary<ForwardKey, List<EntityAbilityInvokeEntry>>();

        foreach (var entity in entries)
        {
            foreach (var invokeGroup in GroupForForwarding(entity.Invokes))
            {
                if (!grouped.TryGetValue(invokeGroup.Key, out var list))
                    grouped[invokeGroup.Key] = list = [];

                list.Add(new EntityAbilityInvokeEntry {
                    EntityId = entity.EntityId,
                    Invokes = [.. invokeGroup]
                });
            }
        }

        foreach (var (key, entityInvokes) in grouped)
        {
            await forwarder.Forward(
                player,
                key.Type,
                new ClientAbilitiesInitFinishCombineNotify { EntityInvokeList = entityInvokes },
                key.Peer);
        }
    }

    private async Task ForwardAbilityChange(
        uint entityId,
        bool isInitHash,
        IEnumerable<AbilityInvokeEntry> invokes
    )
    {
        foreach (var group in GroupForForwarding(invokes))
        {
            await forwarder.Forward(
                player,
                group.Key.Type,
                new ClientAbilityChangeNotify {
                    EntityId = entityId,
                    IsInitHash = isInitHash,
                    Invokes = [.. group]
                },
                group.Key.Peer);
        }
    }

    private static bool CanHandle(AbilityComponent component, uint peerId, bool ignoreAuth) =>
        ignoreAuth ||
        component.Owner.Type is AbilityOwnerType.Scene or AbilityOwnerType.Team ||
        component.Owner.AuthorityPeerId == 0 ||
        component.Owner.AuthorityPeerId == peerId;

    private static IEnumerable<IGrouping<ForwardKey, AbilityInvokeEntry>> GroupForForwarding(
        IEnumerable<AbilityInvokeEntry> invokes
    ) =>
        invokes
            .Where(invoke => invoke.ForwardType is not ForwardType.FORWARD_TYPE_LOCAL and
                not ForwardType.FORWARD_TYPE_ONLY_SERVER)
            .GroupBy(invoke => new ForwardKey(invoke.ForwardType, invoke.ForwardPeer));

    private readonly record struct ForwardKey(ForwardType Type, uint Peer);
}
