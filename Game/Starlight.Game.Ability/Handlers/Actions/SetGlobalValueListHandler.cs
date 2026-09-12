using System.Text.Json;
using Serilog;
using Starlight.Game.Ability.DynamicProps;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class SetGlobalValueListHandler : AbilityActionHandler
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null)
            return ValueTask.CompletedTask;

        var hasList =
            context.Action.Values.TryGetValue("globalValueList", out var list) ||
            context.Action.Values.TryGetValue("NBFDJBEMKBJ", out list);

        if (!hasList ||
            list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() == 0)
        {
            Log.Warning("[SetGlobalValueList] Broken res, globalValueList size = 0");
            return ValueTask.CompletedTask;
        }

        var owner = GlobalValueActionHelpers.EvaluationOwner(context);
        var target = GlobalValueActionHelpers.DefaultTarget(context);

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("key", out var keyElement) ||
                keyElement.ValueKind != JsonValueKind.String ||
                !entry.TryGetProperty("value", out var valueElement))
                continue;

            var key = keyElement.GetString() ?? string.Empty;

            if (key.Length == 0)
                continue;

            var value = AbilityDynamicFloat.Get(context, valueElement, owner);
            GlobalValueActionHelpers.SetGlobal(target, key, value);
        }

        return ValueTask.CompletedTask;
    }
}
