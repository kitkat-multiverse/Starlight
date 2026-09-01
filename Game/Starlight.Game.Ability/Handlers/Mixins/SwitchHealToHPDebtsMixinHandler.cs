using Google.Protobuf;
using Starlight.Game.Ability.HpDebts;

namespace Starlight.Game.Ability.Handlers.Mixins;

public sealed class SwitchHealToHPDebtsMixinHandler(SwitchHealToHpDebtsProcessor processor) : AbilityMixinHandler
{
    public override async ValueTask HandleAsync(AbilityContext context)
    {
        var target = context.Target ?? context.Source;

        if (context.Mixin is null || context.Ability is null ||
            !TryDecode(context.Invoke.AbilityData, out var healAmount, out var healTag))
            return;

        await processor.ApplyAsync(context, target, context.Mixin, context.Ability, healAmount, healTag);
    }

    // The actual proto is missing from dump
    private static bool TryDecode(ByteString data, out float healAmount, out string healTag)
    {
        healAmount = 0f;
        healTag = string.Empty;

        try
        {
            using var input = data.CreateCodedInput();
            uint tag;

            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 13: // field 1, fixed32 float
                        healAmount = input.ReadFloat();
                        break;
                    case 18: // field 2, length-delimited string
                        healTag = input.ReadString();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }

            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }
}
