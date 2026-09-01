using Starlight.Game.Resources.Binary;
using System.Text.Json;

namespace Starlight.Game.Ability.DynamicProps;

internal static class AbilityDynamicFloat
{
    public static float Get(AbilityContext context, string field, AbilityComponent owner, float defaultValue = 0f) =>
        context.Action is null ? defaultValue : Get(context, context.Action, field, owner, defaultValue);

    public static float Get(
        AbilityContext context,
        AbilityConfigNode node,
        string field,
        AbilityComponent owner,
        float defaultValue = 0f
    )
    {
        if (!node.Values.TryGetValue(field, out var value))
            return defaultValue;

        return Evaluate(context, value, owner, defaultValue);
    }

    public static float Get(AbilityContext context, JsonElement value, AbilityComponent owner, float defaultValue = 0f) =>
        Evaluate(context, value, owner, defaultValue);

    private static float Evaluate(AbilityContext context, JsonElement value, AbilityComponent owner, float defaultValue)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetSingle(out var single) ? single : (float)value.GetDouble();

            case JsonValueKind.String:
                return Resolve(context, value.GetString() ?? string.Empty, owner, defaultValue);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return 0f;

            case JsonValueKind.Array: {
                var stack = new Stack<float>();

                foreach (var token in value.EnumerateArray())
                {
                    if (token.ValueKind == JsonValueKind.String)
                    {
                        var op = token.GetString();

                        if (op is "ADD" or "SUB" or "MUL" or "DIV")
                        {
                            if (stack.Count < 2)
                                return defaultValue;

                            var right = stack.Pop();
                            var left = stack.Pop();

                            stack.Push(op switch {
                                "ADD" => left + right,
                                "SUB" => left - right,
                                "MUL" => left * right,
                                "DIV" => left / right,
                                _ => 0f
                            });
                            continue;
                        }
                    }

                    stack.Push(Evaluate(context, token, owner, defaultValue));
                }

                return stack.Count > 0 ? stack.Pop() : defaultValue;
            }

            default:
                return defaultValue;
        }
    }

    private static float Resolve(AbilityContext context, string rawKey, AbilityComponent owner, float defaultValue)
    {
        var negative = rawKey.StartsWith("-%", StringComparison.Ordinal);

        var key = negative ? rawKey[2..]
            : rawKey.StartsWith('%') ? rawKey[1..] : rawKey;

        var result = ResolveCore(context, key, owner, defaultValue);
        return negative ? -result : result;
    }

    private static float ResolveCore(AbilityContext context, string key, AbilityComponent owner, float defaultValue)
    {
        if (string.IsNullOrEmpty(key))
            return defaultValue;

        if (AbilityFightProperty.TryGetId(key, out var fightProp))
            return owner.GetFightProperty(fightProp);

        // AbilityKey equality is hash-based, matching Grasscutter's hash-first lookup.
        var named = AbilityKey.FromName(key);

        if (TryScalar(owner.DynamicValues, named, out var value) ||
            TryScalar(owner.ServerGlobalValues, named, out value) ||
            context.Ability is not null && TryScalar(context.Ability.Overrides, named, out value))
            return value;

        if (uint.TryParse(key, out var hash))
        {
            var hashed = AbilityKey.FromHash(hash);

            if (TryScalar(owner.DynamicValues, hashed, out value) ||
                TryScalar(owner.ServerGlobalValues, hashed, out value) ||
                context.Ability is not null && TryScalar(context.Ability.Overrides, hashed, out value))
                return value;
        }

        if (context.Definition is {} definition &&
            definition.AbilitySpecials.ValueKind == JsonValueKind.Object &&
            definition.AbilitySpecials.TryGetProperty(key, out var special))
            return Evaluate(context, special, owner, defaultValue);

        return defaultValue;
    }

    private static bool TryScalar(
        IReadOnlyDictionary<AbilityKey, AbilityScalarValue> values,
        AbilityKey key,
        out float value
    )
    {
        if (!values.TryGetValue(key, out var scalar))
        {
            value = 0;
            return false;
        }

        value = scalar.Kind switch {
            AbilityScalarKind.Float => scalar.FloatValue,
            AbilityScalarKind.Int or AbilityScalarKind.Bool => scalar.IntValue,
            AbilityScalarKind.UInt => scalar.UIntValue,
            _ => 0f
        };
        return true;
    }
}
