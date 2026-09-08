using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Ability.HpDebts;
using Starlight.Game.Resources.Binary;
using System.Text.Json;

namespace Starlight.Game.Ability.Handlers.Actions;

internal static class GlobalValueActionHelpers
{
    public static AbilityComponent EvaluationOwner(AbilityContext context) =>
        AbilityRuntimeHelpers.AbilityOwnerOf(context);

    public static AbilityComponent DefaultTarget(AbilityContext context) =>
        context.Target ?? context.Source;

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

    public static bool GetBool(AbilityConfigNode node, string field) =>
        node.Values.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.True;

    public static float GetGlobal(AbilityComponent component, string key)
    {
        if (string.IsNullOrEmpty(key) ||
            !component.DynamicValues.TryGetValue(AbilityKey.FromName(key), out var scalar))
            return 0f;

        return ScalarToFloat(scalar);
    }

    public static void SetGlobal(AbilityComponent component, string key, float value)
    {
        if (!string.IsNullOrEmpty(key))
            component.SetDynamicValue(AbilityKey.FromName(key), AbilityScalarValue.FromFloat(value));
    }

    public static void SetServerGlobal(AbilityComponent component, string key, float value)
    {
        if (!string.IsNullOrEmpty(key))
            component.SetServerGlobalValue(AbilityKey.FromName(key), AbilityScalarValue.FromFloat(value));
    }

    public static void SetAbilitySpecial(AbilityContext context, string key, float value)
    {
        if (context.Ability is not null && !string.IsNullOrEmpty(key))
            context.Ability.SetOverride(AbilityKey.FromName(key), AbilityScalarValue.FromFloat(value));
    }

    public static float AddWithGrasscutterLimit(
        float current,
        float delta,
        float minValue,
        float maxValue,
        bool useLimitRange
    )
    {
        var next = current + delta;

        if (useLimitRange && (minValue >= next || next >= maxValue))
            next = delta >= 0f ? maxValue : minValue;

        return next;
    }

    public static AbilityComponent? ResolveTarget(AbilityContext context, string targetType)
    {
        if (string.IsNullOrEmpty(targetType))
            return DefaultTarget(context);

        var abilityOwner = EvaluationOwner(context);

        return targetType switch {
            "Self" or "Applier" => context.Source,
            "Target" or "TempTarget" => DefaultTarget(context),
            "Owner" or "Caster" => abilityOwner,
            "Team" => ResolveTeam(context),
            "CurLocalAvatar" => ResolveLocalAvatar(context),
            "OriginOwner" or "CasterOriginOwner" => ResolveOriginOwner(context, abilityOwner),
            _ => null
        };
    }

    public static AbilityComponent? ResolveTargetOwner(AbilityContext context, AbilityComponent target)
    {
        if (target.Owner.Type is not (AbilityOwnerType.Gadget or AbilityOwnerType.ClientGadget or AbilityOwnerType.Weapon))
            return target;

        if (context.World.TryGetPropOwner(target, out var propOwner))
            return propOwner;

        return context.World.TryGetOwner(target, out var owner) ? owner : null;
    }

    private static AbilityComponent? ResolveOriginOwner(AbilityContext context, AbilityComponent component) =>
        context.World.TryGetOriginOwner(component, out var owner) ? owner : null;

    public static bool TryGetFightProperty(AbilityConfigNode action, out uint property)
    {
        property = 0;

        if (!action.Values.TryGetValue("fightProp", out var element) &&
            !action.Values.TryGetValue("JEGDEEKAFEP", out element) &&
            !action.Values.TryGetValue("HHIIEINHGHN", out element))
            return false;

        if (element.ValueKind == JsonValueKind.String)
            return AbilityFightProperty.TryGetId(element.GetString() ?? string.Empty, out property);

        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt32(out property))
            return property != 0;

        return false;
    }

    private static AbilityComponent? ResolveTeam(AbilityContext context)
    {
        var playerUid = context.Player.Uid;

        return context.World.Scope.Components.Values.FirstOrDefault(component =>
            component.Owner.Type == AbilityOwnerType.Team &&
            (playerUid == 0 || component.Owner.PlayerUid == playerUid));
    }

    private static AbilityComponent? ResolveLocalAvatar(AbilityContext context)
    {
        if (context.World.TryGetCurrentAvatar(out var current))
            return current;

        if (context.Source.Owner.Type == AbilityOwnerType.Avatar)
            return context.Source;

        if (context.Target?.Owner.Type == AbilityOwnerType.Avatar)
            return context.Target;

        var abilityOwner = EvaluationOwner(context);
        return abilityOwner.Owner.Type == AbilityOwnerType.Avatar ? abilityOwner : null;
    }

    private static bool TryGetString(AbilityConfigNode node, string field, out string value)
    {
        value = string.Empty;

        if (!node.Values.TryGetValue(field, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static float ScalarToFloat(AbilityScalarValue scalar) => scalar.Kind switch {
        AbilityScalarKind.Float => scalar.FloatValue,
        AbilityScalarKind.Int or AbilityScalarKind.Bool => scalar.IntValue,
        AbilityScalarKind.UInt => scalar.UIntValue,
        _ => 0f
    };
}
