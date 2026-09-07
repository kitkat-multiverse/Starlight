using Starlight.Common;
using Starlight.Game.Ability;
using Starlight.Game.Player;

namespace Starlight.Game.World;

public sealed class WorldGadgetRuntime(GuidManager guidManager) : IAbilityGadgetRuntime
{
    public async ValueTask CreateGadgetAsync(IPlayer player, AbilityGadgetCreateRequest request) =>
        await player.Module<SceneModule>().CreateAbilityGadget(request);

    public async ValueTask GenerateElemBallsAsync(
        IPlayer player,
        AbilityElemBallRequest request
    )
    {
        var count = checked((int)request.Count);
        var guids = new ulong[count];

        for (var i = 0; i < count; i++)
            guids[i] = guidManager.GenGuid(GuidManager.GuidType.Item);

        await player.Module<SceneModule>().CreateElemBalls(request, guids);
    }

    public async ValueTask KillGadgetsAsync(IPlayer player, uint gadgetId) =>
        await player.Module<SceneModule>().KillAbilityGadgets(gadgetId);
}
