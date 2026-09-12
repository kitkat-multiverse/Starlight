using System.Text.Json;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability.Handlers.Actions;

internal static class GadgetActionHelpers
{
    public static bool GetBool(AbilityConfigNode node, string name) =>
        node.Values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static uint GetUInt32(AbilityConfigNode node, string name)
    {
        if (!node.Values.TryGetValue(name, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number))
            return number;

        return 0;
    }

    public static string GetString(AbilityConfigNode node, string name) =>
        node.Values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ?
            value.GetString() ?? string.Empty :
            string.Empty;

    public static uint GetNestedUInt32(AbilityConfigNode node, string objectName, string name)
    {
        if (!node.Values.TryGetValue(objectName, out var value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetUInt32(out var number))
            return 0;

        return number;
    }

    public static uint CampTargetType(string name) => name switch {
        "Alliance" => 1,
        "Enemy" => 2,
        "Self" => 3,
        "SelfCamp" => 4,
        "All" => 5,
        "AllExceptSelf" => 6,
        "AllianceIncludeSelf" => 7,
        _ => 0
    };
}
