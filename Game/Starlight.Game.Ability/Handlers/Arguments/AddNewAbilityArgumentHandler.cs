using Serilog;
using Starlight.Game.Resources;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Arguments;

public sealed class AddNewAbilityArgumentHandler(ProtocolRegistry protocol, GameData data)
    : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_ADD_NEW_ABILITY)
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        if (!AbilityInvokeDecode.Try<AbilityMetaAddAbility>(protocol, context.Invoke.AbilityData, out var add) ||
            add.Ability is not { InstancedAbilityId: not 0 } applied ||
            applied.AbilityName is null)
        {
            if (context.LogAbilitiesEnabled)
                Log.Warning("AddNewAbilityArgumentHandler: Invalid ability data {@AbilityData}", context.Invoke.AbilityData.ToBase64());
            return ValueTask.CompletedTask;
        }

        var name = AbilityProtocol.FromAbilityString(applied.AbilityName);
        var @override = AbilityProtocol.FromAbilityString(applied.AbilityOverride);

        var definition = name.Name is not null ?
            data.ResolveAbility(name.Name) ?? data.ResolveAbility(name.Hash) :
            data.ResolveAbility(name.Hash);

        if (context.LogAbilitiesEnabled)
            Log.Information("AddNewAbilityArgumentHandler: Adding new ability {@AbilityData}", context.Invoke.AbilityData.ToBase64());

        var ability = context.Source.UpsertAbility(applied.InstancedAbilityId, name, @override, definition);

        if (context.LogAbilitiesEnabled)
            Log.Information("AddNewAbilityArgumentHandler: Ability resolved:\n{@Ability}", ability.ToString());

        ability.ReinitializeOverrides(applied.OverrideMap
            .Where(value => value.Key is not null)
            .Select(value => new KeyValuePair<AbilityKey, AbilityScalarValue>(
                AbilityProtocol.FromAbilityString(value.Key),
                AbilityProtocol.FromScalarEntry(value))));

        return ValueTask.CompletedTask;
    }
}
