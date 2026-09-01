using Serilog;
using Starlight.Game.Resources;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class ModifierDurabilityChangeArgumentHandler(ProtocolRegistry protocol, GameData data)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_MODIFIER_DURABILITY_CHANGE)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var head = context.Invoke.Head ?? new AbilityInvokeEntryHead();

        if (!context.Source.TryGetModifier(head.InstancedModifierId, out var modifier) ||
            !AbilityInvokeDecode.Try<AbilityMetaModifierDurabilityChange>(protocol, context.Invoke.AbilityData,
                out var change))
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("Failed to decode AbilityMetaModifierDurabilityChange for modifier {@ModifierId}\n{@AbilityData}",
                    head.InstancedModifierId, context.Invoke.AbilityData);
            return ValueTask.CompletedTask;
        }

        modifier.HasDurability = true;
        modifier.ReduceRatio = change.ReduceDurability;
        modifier.RemainingDurability = change.RemainDurability;
        modifier.IsDurabilityZero = change.RemainDurability <= 0;

        return ValueTask.CompletedTask;
    }
}
