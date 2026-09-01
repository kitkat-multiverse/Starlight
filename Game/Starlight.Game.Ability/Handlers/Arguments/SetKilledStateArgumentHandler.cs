using Serilog;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class SetKilledStateArgumentHandler(ProtocolRegistry protocol)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_SET_KILLED_STATE)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        if (AbilityInvokeDecode.Try<AbilityMetaSetKilledState>(protocol, context.Invoke.AbilityData, out var state))
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("SetKilledStateArgumentHandler: Setting killed state {@KilledOrNot} | {@AbilityData}",
                    state.Killed,
                    context.Invoke.AbilityData.ToBase64());
            context.Source.SetKilled(state.Killed);
        }

        return ValueTask.CompletedTask;
    }
}
