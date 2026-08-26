using JetBrains.Annotations;
using Microsoft.Extensions.Hosting;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;

namespace Starlight.Game.Resources;

public sealed class GameData : IHostedService
{
    #region Excel

    [UsedImplicitly] public readonly Dictionary<uint, AvatarTalentData> AvatarTalentData = new();
    [UsedImplicitly] public readonly Dictionary<uint, CoopPointData> CoopPointData = new();

    #endregion

    #region Binary

    public readonly Dictionary<uint, Dictionary<uint, PointData>> ScenePoints = new();

    #endregion

    public Task StartAsync(CancellationToken cancellationToken)
    {
        DataLoader.Initialize(this);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
