namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class KillGadgetHandler(IEnumerable<IAbilityGadgetRuntime> gadgetRuntimes) : AbilityActionHandler
{
    public override async ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null)
            return;

        var gadgetId = GadgetActionHelpers.GetNestedUInt32(context.Action, "gadgetInfo", "configID");
        var gadgets = gadgetRuntimes.LastOrDefault();

        if (gadgetId != 0 && gadgets is not null)
            await gadgets.KillGadgetsAsync(context.Player, gadgetId);
    }
}
