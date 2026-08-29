using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;

namespace Starlight.Game.Resources;

public sealed class GameData(IConfiguration config) : IHostedService
{
    #region Excel

    [UsedImplicitly] public readonly Dictionary<uint, AvatarData> AvatarData = new();
    [UsedImplicitly] public readonly Dictionary<uint, AvatarSkillDepotData> AvatarSkillDepotData = new();
    [UsedImplicitly] public readonly Dictionary<uint, AvatarTalentData> AvatarTalentData = new();
    [UsedImplicitly] public readonly Dictionary<uint, WeaponData> WeaponData = new();
    [UsedImplicitly] public readonly Dictionary<uint, MaterialData> MaterialData = new();
    [UsedImplicitly] public readonly Dictionary<uint, CoopPointData> CoopPointData = new();

    #endregion

    #region Binary

    public readonly Dictionary<uint, AvatarConfig> Avatars = new();
    public readonly Dictionary<uint, Dictionary<uint, PointData>> ScenePoints = new();

    #endregion

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = config.GetValue<string>("Game:ResourcesPath") ?? "./resources.zip";

        Resources.Initialize(path);
        DataLoader.Initialize(this);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
