using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class SetModifierApplyEntityArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_SET_MODIFIER_APPLY_ENTITY)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var head = context.Invoke.Head ?? new AbilityInvokeEntryHead();

        if (context.Source.TryGetModifier(head.InstancedModifierId, out var modifier) &&
            AbilityInvokeDecode.Try<AbilityMetaSetModifierApplyEntityId>(protocol, context.Invoke.AbilityData, out var change))
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("SetModifierApplyEntityArgumentHandler: Setting apply entity ID for modifier {@Modifier} to {@ApplyEntityId}",
                    modifier, change.ApplyEntityId);
            modifier.ApplyEntityId = change.ApplyEntityId;
        }

        return ValueTask.CompletedTask;
    }
}
