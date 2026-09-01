using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class ClearGlobalFloatValueArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_CLEAR_GLOBAL_FLOAT_VALUE)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        if (AbilityInvokeDecode.Try<AbilityString>(protocol, context.Invoke.AbilityData, out var key))
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("ClearGlobalFloatValueArgumentHandler: Clearing global float value {@AbilityData}",
                    context.Invoke.AbilityData.ToBase64());
            context.Source.ClearDynamicValue(AbilityProtocol.FromAbilityString(key));
        }

        return ValueTask.CompletedTask;
    }
}
