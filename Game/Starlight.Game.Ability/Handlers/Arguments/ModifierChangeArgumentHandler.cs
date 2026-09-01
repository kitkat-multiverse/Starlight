using Serilog;
using Starlight.Game.Ability.HpDebts;
using Starlight.Game.Resources;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class ModifierChangeArgumentHandler(
    ProtocolRegistry protocol,
    GameData data,
    HpDebtService hpDebts
)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_MODIFIER_CHANGE)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var invoke = context.Invoke;
        var head = invoke.Head ?? new AbilityInvokeEntryHead();

        if (!AbilityInvokeDecode.Try<AbilityMetaModifierChange>(protocol, invoke.AbilityData, out var change))
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("ModifierChangeArgumentHandler: Invalid ability data {@AbilityData}", context.Invoke.AbilityData.ToBase64());
            return ValueTask.CompletedTask;
        }

        if (change.Action == ModifierAction.MODIFIER_ACTION_REMOVED)
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("ModifierChangeArgumentHandler: Removing modifier {@AbilityData}", context.Invoke.AbilityData.ToBase64());
            context.Source.RemoveModifier(head.InstancedModifierId);
            return ValueTask.CompletedTask;
        }

        // No parent ability id is a big no-no.
        if (change.Action != ModifierAction.MODIFIER_ACTION_ADDED || head.InstancedAbilityId == 0)
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("ModifierChangeArgumentHandler: Invalid modifier change action {@AbilityData} (no parent ability id)",
                    context.Invoke.AbilityData.ToBase64());
            return ValueTask.CompletedTask;
        }

        var parentComponent = context.Source;
        AbilityInstance? parentAbility = null;

        // First try the target's instanced ability, then the source's
        if (head.TargetId != 0 && context.World.TryGet(head.TargetId, out var target) &&
            target.TryGetAbility(head.InstancedAbilityId, out var targetAbility))
        {
            parentComponent = target;
            parentAbility = targetAbility;
        } else
        {
            context.Source.TryGetAbility(head.InstancedAbilityId, out parentAbility!);
        }

        var parentName = parentAbility?.Name ?? AbilityProtocol.FromAbilityString(change.ParentAbilityName);
        var parentOverride = parentAbility?.Override ?? AbilityProtocol.FromAbilityString(change.ParentAbilityOverride);
        var definition = parentAbility?.Definition ?? ResolveAbility(data, parentName);

        // Create a fallback ability if the referenced one is not present.
        if (parentAbility is null && parentName != AbilityKey.Default)
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("ModifierChangeArgumentHandler: Creating fallback ability {@AbilityData}", context.Invoke.AbilityData.ToBase64());

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

        if (context.LogAbilitiesEnabled)
            Log.Information("ModifierChangeArgumentHandler: Upserting modifier {@Modifier}", modifier);

        context.Source.UpsertModifier(modifier);
        hpDebts.OnModifierAdded(context, context.Source, modifier, parentAbility);
        return ValueTask.CompletedTask;
    }

    private static Resources.Binary.AbilityConfig? ResolveAbility(GameData data, AbilityKey key) =>
        key.Name is not null ? data.ResolveAbility(key.Name) ?? data.ResolveAbility(key.Hash) : data.ResolveAbility(key.Hash);
}
