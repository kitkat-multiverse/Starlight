using Starlight.Game.Ability.HpDebts;
using Serilog;
using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Resources;
using Starlight.Game.Resources.Excel;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;
using System.Globalization;
using System.Text.Json;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class GenerateElemBallHandler(
    ProtocolRegistry protocol,
    GameData data,
    IEnumerable<IAbilityGadgetRuntime> gadgetRuntimes
) : AbilityActionHandler
{
    public override async ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null || context.Ability is null)
            return;

        var gadgets = gadgetRuntimes.LastOrDefault();

        if (gadgets is null)
            return;

        if (!AbilityInvokeDecode.Try<AbilityActionGenerateElemBall>(protocol, context.Invoke.AbilityData, out var generate))
            return;

        var dropType = GadgetActionHelpers.GetString(context.Action, "dropType");

        if (string.IsNullOrEmpty(dropType))
            dropType = GadgetActionHelpers.GetString(context.Action, "dropType"); // GIPOPDJBAEC 7.0

        if (dropType is "" or "LevelControl")
        {
            var level = data.ResolveLevelEntity(context.World.SceneId);

            if (level is not null && IsElemBallDropDisabled(level.Extra))
                return;
        } else if (dropType == "BigWorldOnly")
        {
            if (!data.SceneData.TryGetValue(context.World.SceneId, out var scene) || scene.Type != "SCENE_WORLD")
                return;
        }

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context);

        var energy = AbilityDynamicFloat.Get(context, "baseEnergy", owner) *
                     AbilityDynamicFloat.Get(context, "ratio", owner, defaultValue: 1f);

        if (energy <= 0f)
            return;

        var itemId = GadgetActionHelpers.GetUInt32(context.Action, "configID");

        if (!data.MaterialData.TryGetValue(itemId, out var item))
        {
            Log.Warning("GenerateElemBall configID {ItemId} was not found in material data", itemId);
            return;
        }

        if (item.ItemUse.Count == 0)
        {
            Log.Warning("GenerateElemBall item {ItemId} has no item use array", itemId);
            return;
        }

        if (!TryGetRequiredEnergy(item.ItemUse[0], out var requiredEnergy) || requiredEnergy <= 0d)
        {
            Log.Warning("GenerateElemBall item {ItemId} has unsupported item use {UseOp}", itemId, item.ItemUse[0].UseOp);
            return;
        }

        var amount = (uint)Math.Ceiling(energy / requiredEnergy);

        if (amount >= 21)
        {
            Log.Warning("Attempt to generate more than 20 element balls: {Amount}", amount);
            return;
        }

        await gadgets.GenerateElemBallsAsync(
            context.Player,
            new AbilityElemBallRequest(
                itemId,
                item.GadgetId,
                amount,
                Copy(generate.Pos),
                Copy(generate.Rot)));
    }

    private static bool IsElemBallDropDisabled(
        IReadOnlyDictionary<string, JsonElement> extra
    )
    {
        return IsNone(extra, "dropElemControlType"); // OBHEINIEGFB 7.0

        static bool IsNone(
            IReadOnlyDictionary<string, JsonElement> extra,
            string key
        )
        {
            return extra.TryGetValue(key, out var value) &&
                   value.ValueKind == JsonValueKind.String &&
                   string.Equals(
                       value.GetString(),
                       "None",
                       StringComparison.Ordinal);
        }
    }

    internal static bool TryGetRequiredEnergy(ItemUseData itemUse, out int energy)
    {
        var index = itemUse.UseOp switch {
            "ITEM_USE_ADD_ELEM_ENERGY" => 1,
            "ITEM_USE_ADD_ALL_ENERGY" => 0,
            "ITEM_USE_ADD_PHLOGISTON" => 0,
            _ => -1
        };

        if (index < 0 || index >= itemUse.UseParam.Count)
        {
            energy = 0;
            return false;
        }

        return int.TryParse(
            itemUse.UseParam[index],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out energy);
    }

    private static Vector Copy(Vector? value) =>
        value is null ? new Vector() : new Vector { X = value.X, Y = value.Y, Z = value.Z };
}
