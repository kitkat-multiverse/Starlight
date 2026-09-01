using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class ReinitOverrideMapArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_REINIT_OVERRIDEMAP)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var head = context.Invoke.Head ?? new AbilityInvokeEntryHead();

        if (!context.Source.TryGetAbility(head.InstancedAbilityId, out var ability) ||
            !AbilityInvokeDecode.Try<AbilityMetaReInitOverrideMap>(protocol, context.Invoke.AbilityData, out var reinit))
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("ReinitOverrideMapArgumentHandler: Failed to decode ability meta reinit override map {@AbilityData}",
                    context.Invoke.AbilityData.ToBase64());
            return ValueTask.CompletedTask;
        }

        ability.ReinitializeOverrides(reinit.OverrideMap
            .Where(value => value.Key is not null)
            .Select(value => new KeyValuePair<AbilityKey, AbilityScalarValue>(
                AbilityProtocol.FromAbilityString(value.Key),
                AbilityProtocol.FromScalarEntry(value))));

        if (context.LogAbilitiesEnabled)
            Log.Information("ReinitOverrideMapArgumentHandler: Reinitialized overrides for ability {@Ability}", ability.ToString());

        return ValueTask.CompletedTask;
    }
}
