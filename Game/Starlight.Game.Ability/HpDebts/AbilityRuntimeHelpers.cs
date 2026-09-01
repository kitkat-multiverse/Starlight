using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability.HpDebts;

internal static class AbilityRuntimeHelpers
{
    public static AbilityComponent AbilityOwnerOf(AbilityContext context)
    {
        if (context.Ability is null)
            return context.Source;

        var id = context.Ability.InstancedAbilityId;

        if (context.Source.TryGetAbility(id, out var sourceAbility) && ReferenceEquals(sourceAbility, context.Ability))
            return context.Source;

        if (context.Target is not null &&
            context.Target.TryGetAbility(id, out var targetAbility) &&
            ReferenceEquals(targetAbility, context.Ability))
            return context.Target;

        return context.Source;
    }

    public static string GetString(AbilityConfigNode node, string field, params string[] aliases)
    {
        if (TryGetString(node, field, out var value))
            return value;

        foreach (var alias in aliases)
        {
            if (TryGetString(node, alias, out value))
                return value;
        }

        return string.Empty;
    }

    private static bool TryGetString(AbilityConfigNode node, string field, out string value)
    {
        value = string.Empty;

        if (!node.Values.TryGetValue(field, out var element) ||
            element.ValueKind != System.Text.Json.JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return true;
    }
}
