using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class GlobalFloatValueArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_GLOBAL_FLOAT_VALUE)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        if (!AbilityInvokeDecode.Try<AbilityScalarValueEntry>(protocol, context.Invoke.AbilityData, out var entry) ||
            entry.Key is null)
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("GlobalFloatValueArgumentHandler: Invalid ability data {@AbilityData}", context.Invoke.AbilityData.ToBase64());
            return ValueTask.CompletedTask;
        }

        // Grasscutter accepts both string and hash keys and salvages undeclared scalar types.
        var value = AbilityProtocol.FromScalarEntry(entry);

        if (value.Kind == AbilityScalarKind.Float && !float.IsFinite(value.FloatValue))
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("GlobalFloatValueArgumentHandler: Invalid float value {@AbilityData}", context.Invoke.AbilityData.ToBase64());
            return ValueTask.CompletedTask;
        }

        if (context.LogAbilitiesEnabled)
            Log.Information("GlobalFloatValueArgumentHandler: Setting global float value {@AbilityData}",
                context.Invoke.AbilityData.ToBase64());

        context.Source.SetDynamicValue(AbilityProtocol.FromAbilityString(entry.Key), value);
        return ValueTask.CompletedTask;
    }
}
