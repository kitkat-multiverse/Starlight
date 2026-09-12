using System.Text.Json;
using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Ability.HpDebts;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class SetOverrideMapValueHandler : AbilityActionHandler
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null || context.Ability is null)
            return ValueTask.CompletedTask;

        var action = context.Action;
        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context);
        var key = AbilityRuntimeHelpers.GetString(action, "overrideMapKey");

        if (string.IsNullOrEmpty(key))
            return ValueTask.CompletedTask;

        var value = AbilityDynamicFloat.Get(context, action, "value", owner);

        if (action.Values.TryGetValue("useLimitRange", out var useLimitRange) &&
            useLimitRange.ValueKind == JsonValueKind.True)
        {
            var min = action.Values.ContainsKey("minValue") ?
                AbilityDynamicFloat.Get(context, action, "minValue", owner, float.NegativeInfinity) :
                float.NegativeInfinity;

            var max = action.Values.ContainsKey("maxValue") ?
                AbilityDynamicFloat.Get(context, action, "maxValue", owner, float.PositiveInfinity) :
                float.PositiveInfinity;

            value = Math.Clamp(value, min, max);
        }

        var targetAbility = ResolveAbility(context, owner, action);
        targetAbility?.SetOverride(AbilityKey.FromName(key), AbilityScalarValue.FromFloat(value));

        return ValueTask.CompletedTask;
    }

    private static AbilityInstance? ResolveAbility(
        AbilityContext context,
        AbilityComponent owner,
        AbilityConfigNode action
    )
    {
        var abilityName = AbilityRuntimeHelpers.GetString(action, "abilityName");

        if (string.IsNullOrEmpty(abilityName))
            return context.Ability;

        var key = AbilityKey.FromName(abilityName);
        return owner.AppliedAbilities.Values.FirstOrDefault(ability => ability.Name == key);
    }
}
