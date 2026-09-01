using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class ClearOverrideParamArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_CLEAR_OVERRIDE_PARAM)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var head = context.Invoke.Head ?? new AbilityInvokeEntryHead();

        if (!context.Source.TryGetAbility(head.InstancedAbilityId, out var ability))
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("ClearOverrideParamArgumentHandler: Could not find InstancedAbilityId {@InstancedAbilityId} | {@AbilityData}",
                    head.InstancedAbilityId, context.Invoke.AbilityData.ToBase64());

            return ValueTask.CompletedTask;
        }

        if (AbilityInvokeDecode.Try<AbilityString>(protocol, context.Invoke.AbilityData, out var key))
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("ClearGlobalFloatValueArgumentHandler: Clearing global float value {@AbilityData}",
                    context.Invoke.AbilityData.ToBase64());
            context.Source.ClearDynamicValue(AbilityProtocol.FromAbilityString(key));
        }

        ability.ClearOverride(AbilityProtocol.FromAbilityString(key));

        return ValueTask.CompletedTask;
    }
}
