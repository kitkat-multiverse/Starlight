using Serilog;
using Starlight.Game.Resources;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class RemoveAbilityArgumentHandler(ProtocolRegistry protocol, GameData data)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_REMOVE_ABILITY)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var head = context.Invoke.Head ?? new AbilityInvokeEntryHead();

        if (head.InstancedAbilityId != 0)
        {
            if (context.LogAbilitiesEnabled)
                Log.Information("RemoveAbilityArgumentHandler: Removing InstancedAbilityId {@InstancedAbilityId} {@AbilityData}",
                    head.InstancedAbilityId,
                    context.Invoke.AbilityData.ToBase64());
            context.Source.RemoveAbility(head.InstancedAbilityId);
        }

        return ValueTask.CompletedTask;
    }
}
