using Starlight.Game.Ability.HpDebts;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class CreateGadgetHandler(
    ProtocolRegistry protocol,
    IEnumerable<IAbilityGadgetRuntime> gadgetRuntimes
) : AbilityActionHandler
{
    public override async ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null || !GadgetActionHelpers.GetBool(context.Action, "byServer"))
            return;

        var gadgets = gadgetRuntimes.LastOrDefault();

        if (gadgets is null)
            return;

        if (!AbilityInvokeDecode.Try<AbilityActionCreateGadget>(protocol, context.Invoke.AbilityData, out var create))
            return;

        var owner = GadgetActionHelpers.GetBool(context.Action, "ownerIsTarget") ?
            context.Target :
            AbilityRuntimeHelpers.AbilityOwnerOf(context);

        var target = context.Target;

        await gadgets.CreateGadgetAsync(
            context.Player,
            new AbilityGadgetCreateRequest(
                GadgetActionHelpers.GetUInt32(context.Action, "gadgetID"),
                Copy(create.Pos),
                Copy(create.Rot),
                GadgetActionHelpers.GetUInt32(context.Action, "campID"),
                GadgetActionHelpers.CampTargetType(GadgetActionHelpers.GetString(context.Action, "campTargetType")),
                owner?.Owner.EntityId ?? 0,
                target?.Owner.EntityId ?? 0,
                GadgetActionHelpers.GetBool(context.Action, "sightGroupWithOwner"),
                GadgetActionHelpers.GetBool(context.Action, "lifeByOwnerIsAlive") ||
                GadgetActionHelpers.GetBool(context.Action, "lifeByOwnerV2")));
    }

    private static Vector Copy(Vector? value) =>
        value is null ? new Vector() : new Vector { X = value.X, Y = value.Y, Z = value.Z };
}
