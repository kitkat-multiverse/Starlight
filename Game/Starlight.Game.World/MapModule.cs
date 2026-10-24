using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.World;

public sealed class MapModule(IPlayer player, GameData? data = null) : IModule
{
    private const uint AreaIdCeiling = 999;

    private readonly Dictionary<uint, NetSceneUnlocks> _unlocks = [];
    private bool _loaded;

    [Opcode]
    public GetScenePointRsp OnGetScenePoint(GetScenePointReq msg)
    {
        uint[] points;

        lock (player.StateLock)
        {
            LoadState();
            points = [.. _unlocks.GetValueOrDefault(msg.SceneId)?.UnlockedPoints ?? []];
        }

        return new GetScenePointRsp {
            SceneId = msg.SceneId,
            BelongUid = msg.BelongUid,
            IsRelogin = msg.IsRelogin,
            UnlockedPointList = [.. points],
            UnhidePointList = [.. points],
            UnlockAreaList = [1, 2, 3, 4, 5, 6, 7, 8] //hardcoded this way in GC too.
        };
    }

    [Opcode]
    public GetSceneAreaRsp OnGetSceneArea(GetSceneAreaReq msg)
    {
        uint[] areas;

        lock (player.StateLock)
        {
            LoadState();
            areas = [.. _unlocks.GetValueOrDefault(msg.SceneId)?.UnlockedAreas ?? []];
        }

        // TODO: also send CityInfo for statue cities 1-5 here, from its Manager.
        //       Requires a statue-of-the-seven manager that does not exist yet.
        return new GetSceneAreaRsp {
            SceneId = msg.SceneId,
            AreaIdList = [.. areas]
        };
    }

    public async Task<int> UnlockAllAsync(int value)
    {
        var gameData = data;

        if (gameData is null)
            return 0;

        var added = 0;
        ScenePointUnlockNotify? pointNotify = null;
        SceneAreaUnlockNotify? areaNotify = null;

        lock (player.StateLock)
        {
            LoadState();

            foreach (var (sceneId, points) in gameData.ScenePoints)
            {
                var unlocks = EnsureUnlocks(sceneId);

                foreach (var (pointId, point) in points)
                {
                    if (value != -2 && point.ForbidSimpleUnlock)
                        continue;

                    if (unlocks.UnlockedPoints.Contains(pointId))
                        continue;

                    unlocks.UnlockedPoints.Add(pointId);
                    added++;
                }

                for (var area = 1u; area <= AreaIdCeiling; area++)
                    unlocks.UnlockedAreas.Add(area);
            }

            var scene = player.Module<WorldModule>().Scene;

            if (scene is not null && _unlocks.TryGetValue(scene.Id, out var current))
            {
                pointNotify = new ScenePointUnlockNotify {
                    SceneId = scene.Id,
                    PointList = [.. current.UnlockedPoints]
                };

                areaNotify = new SceneAreaUnlockNotify {
                    SceneId = scene.Id,
                    AreaList = [.. current.UnlockedAreas]
                };
            }
        }

        if (pointNotify is not null)
            await player.Send(pointNotify);

        if (areaNotify is not null)
            await player.Send(areaNotify);

        return added;
    }

    private void LoadState()
    {
        lock (player.StateLock)
        {
            if (_loaded)
                return;

            _loaded = true;

            foreach (var (sceneId, unlocks) in player.State.SceneUnlocks)
            {
                _unlocks[sceneId] = unlocks;
            }
        }
    }

    private NetSceneUnlocks EnsureUnlocks(uint sceneId)
    {
        if (_unlocks.TryGetValue(sceneId, out var existing))
            return existing;

        var created = new NetSceneUnlocks();
        _unlocks[sceneId] = created;
        player.State.SceneUnlocks[sceneId] = created;

        return created;
    }
}
