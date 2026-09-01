using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class OverrideParamArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_OVERRIDE_PARAM)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var head = context.Invoke.Head ?? new AbilityInvokeEntryHead();

        if (context.Source.TryGetAbility(head.InstancedAbilityId, out var ability) &&
            AbilityInvokeDecode.Try<AbilityScalarValueEntry>(protocol, context.Invoke.AbilityData, out var value) &&
            value.Key is not null)
        {
            if (context.LogAbilitiesEnabled)
                Log.Information($"Overriding ability param {value.Key} with value {System.Text.Json.JsonSerializer.Serialize(value)}");
            ability.SetOverride(AbilityProtocol.FromAbilityString(value.Key), AbilityProtocol.FromScalarEntry(value));
        }

        return ValueTask.CompletedTask;
    }
}
