using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Game.World;
using Starlight.Rpc.Tunnel;

namespace Starlight.Commands;

public sealed class SpawnCommand(PlayerManager players, GameData data) : ICommand
{
    public string Name => "spawn";
    public string Description => "Spawns a resource-backed monster in an online player's current scene.";
    public string Usage => "spawn <uid> <monster-id> <level>";
    public string[] Aliases => ["s", "monster"];

    public async Task ExecuteAsync(CommandContext context, string[] args)
    {
        var player = context.Target ?? context.Invoker;
        var monsterIndex = 0;

        if (player is null)
        {
            if (args.Length != 3 || !uint.TryParse(args[0], out var uid) || uid == 0)
            {
                await UsageError(context);
                return;
            }

            if (!players.TryGet(uid, out player))
            {
                await context.ReplyAsync($"Player '{uid}' is not online.", CommandOutputLevel.Warning);
                return;
            }

            monsterIndex = 1;
        } else if (args.Length != 2)
        {
            await UsageError(context);
            return;
        }

        if (!uint.TryParse(args[monsterIndex], out var monsterId) || monsterId == 0 ||
            !uint.TryParse(args[monsterIndex + 1], out var level) || level == 0)
        {
            await UsageError(context);
            return;
        }

        if (!data.MonsterData.ContainsKey(monsterId))
        {
            await context.ReplyAsync($"Monster {monsterId} does not exist in resources.", CommandOutputLevel.Warning);
            return;
        }

        if (!data.MonsterCurveData.ContainsKey(level))
        {
            await context.ReplyAsync($"Monster level {level} has no curve data.", CommandOutputLevel.Warning);
            return;
        }

        context.CancellationToken.ThrowIfCancellationRequested();

        try
        {
            var entity = await player.Module<SceneModule>().SpawnMonster(monsterId, level);

            await context.ReplyAsync(
                $"Spawned monster {monsterId} at level {level} as entity {entity.EntityId} for player {player.Uid}.");
        }
        catch (TunnelClosedException)
        {
            if (context.Source == CommandSource.Console)
                await context.ReplyAsync("Spawn stopped because the target player disconnected.", CommandOutputLevel.Warning);
        }
        catch (InvalidOperationException exception)
        {
            await context.ReplyAsync(
                $"Cannot spawn monster for player {player.Uid}: {exception.Message}",
                CommandOutputLevel.Warning);
        }
    }

    private async ValueTask UsageError(CommandContext context)
        => await context.ReplyAsync(
            $"Usage: {(context.Target is not null || context.Invoker is not null ? "spawn <monster-id> <level>" : Usage)}",
            CommandOutputLevel.Warning);
}
