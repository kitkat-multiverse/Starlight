using Starlight.Game.Resources.Binary;
using System.Text.Json;

namespace Starlight.Game.Ability.HpDebts;

internal static class AbilityPredicateEvaluator
{
    public static bool Check(AbilityConfigNode node, string healTag)
    {
        if (!node.Values.TryGetValue("predicates", out var predicates) ||
            predicates.ValueKind != JsonValueKind.Array)
            return true;

        return CheckAll(predicates, healTag);
    }

    private static bool CheckAll(JsonElement predicates, string healTag)
    {
        foreach (var predicate in predicates.EnumerateArray())
        {
            if (!CheckOne(predicate, healTag))
                return false;
        }
        return true;
    }

    private static bool CheckOne(JsonElement predicate, string healTag)
    {
        if (predicate.ValueKind != JsonValueKind.Object)
            return true;

        var type = predicate.TryGetProperty("$type", out var typeElement) &&
                   typeElement.ValueKind == JsonValueKind.String ?
            typeElement.GetString() ?? string.Empty :
            string.Empty;

        if (string.Equals(type, "ByNot", StringComparison.Ordinal))
        {
            if (!predicate.TryGetProperty("predicates", out var nested) || nested.ValueKind != JsonValueKind.Array)
                return false;

            return !CheckAll(nested, healTag);
        }

        if (string.Equals(type, "ByAny", StringComparison.Ordinal))
        {
            if (!predicate.TryGetProperty("predicates", out var nested) || nested.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var child in nested.EnumerateArray())
            {
                if (CheckOne(child, healTag))
                    return true;
            }
            return false;
        }

        // Usually this is a "ByHealTag" predicate, 
        // but this way we can save ourselves from having to make sure this predicate $type is deobfuscated.
        if (predicate.TryGetProperty("healTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String &&
                    string.Equals(tag.GetString(), healTag, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        return true;
    }
}
