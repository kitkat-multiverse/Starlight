using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Rpc.Tunnel;

namespace Starlight.Commands;

public sealed class GiveCommand(PlayerManager players, GameData data) : ICommand
{
    public string Name => "give";
    public string Description => "Gives materials, weapons, or avatars to an online player.";
    public string Usage =>
        "give <uid> <item-id|avatar-id|all|weapons|materials|avatars> [x<count>] [lvl<level>] [r<1-5>] [c<0-6>]";

    public string[] Aliases => ["g", "item", "giveitem"];

    public async Task ExecuteAsync(CommandContext context, string[] args)
    {
        try
        {
            await ExecuteCoreAsync(context, args);
        }
        catch (TunnelClosedException)
        {
            if (context.Source == CommandSource.Console)
                await context.ReplyAsync("Give stopped because the target player disconnected.", CommandOutputLevel.Warning);
        }
    }

    private async Task ExecuteCoreAsync(CommandContext context, string[] args)
    {
        var player = context.Target ?? context.Invoker;
        var selectorIndex = 0;

        if (player is null)
        {
            if (args.Length < 2 || !uint.TryParse(args[0], out var uid) || uid == 0)
            {
                await UsageError(context);
                return;
            }

            if (!players.TryGet(uid, out player))
            {
                await context.ReplyAsync($"Player '{uid}' is not online.", CommandOutputLevel.Warning);
                return;
            }

            selectorIndex = 1;
        } else if (args.Length < 1)
        {
            await UsageError(context);
            return;
        }

        if (!TryParseOptions(args[(selectorIndex + 1)..], out var options, out var error))
        {
            await context.ReplyAsync($"{error} Usage: {UsageFor(context)}", CommandOutputLevel.Warning);
            return;
        }

        context.CancellationToken.ThrowIfCancellationRequested();

        var selector = args[selectorIndex].ToLowerInvariant();
        var inventory = player.Module<InventoryModule>();

        switch (selector)
        {
            case "all": {
                var materialCount = await GiveMaterials(inventory, options);
                context.CancellationToken.ThrowIfCancellationRequested();
                var weaponCount = await GiveWeapons(inventory, options);
                context.CancellationToken.ThrowIfCancellationRequested();

                var avatarCount = await GiveAvatars(
                    player.Module<AvatarModule>(), options, defaultConstellation: 6);

                await context.ReplyAsync(
                    $"Granted {materialCount} materials, {weaponCount} weapons, and {avatarCount} avatars to {player.Uid}.");
                return;
            }

            case "weapons":
            case "weapon":
            case "wp": {
                var count = await GiveWeapons(inventory, options);
                await context.ReplyAsync($"Granted {count} weapons to {player.Uid}.");
                return;
            }

            case "materials":
            case "material":
            case "mats":
            case "mat": {
                var count = await GiveMaterials(inventory, options);
                await context.ReplyAsync($"Granted {count} materials to {player.Uid}.");
                return;
            }

            case "avatars":
            case "avatar": {
                var count = await GiveAvatars(
                    player.Module<AvatarModule>(), options, defaultConstellation: 6);
                await context.ReplyAsync($"Granted {count} avatars to {player.Uid}.");
                return;
            }
        }

        if (!uint.TryParse(selector, out var id) || id == 0)
        {
            await context.ReplyAsync(
                $"'{selector}' is not an item ID, avatar ID, or supported selector.",
                CommandOutputLevel.Warning);
            return;
        }

        if (data.MaterialData.TryGetValue(id, out var material) && material.IsInventoryMaterial)
        {
            await inventory.AddMaterial(id, options.Amount);
            await context.ReplyAsync($"Granted {options.Amount}x material {id} to {player.Uid}.");
            return;
        }

        if (data.WeaponData.TryGetValue(id, out var weapon))
        {
            await inventory.AddWeapons(
                [weapon],
                options.Amount,
                options.Level,
                options.Refinement);
            await context.ReplyAsync($"Granted {options.Amount}x weapon {id} to {player.Uid}.");
            return;
        }

        if (CanCreateAvatar(id))
        {
            var (_, added) = await player.Module<AvatarModule>()
                .AddAvatar(id, options.Level, options.Constellation);

            await context.ReplyAsync(added ?
                    $"Added avatar {id} to {player.Uid}." :
                    $"Player {player.Uid} already owns avatar {id}.",
                added ? CommandOutputLevel.Information : CommandOutputLevel.Warning);
            return;
        }

        await context.ReplyAsync(
            $"No supported material, weapon, or avatar has ID {id}.",
            CommandOutputLevel.Warning);
    }

    private async ValueTask UsageError(CommandContext context)
        => await context.ReplyAsync($"Usage: {UsageFor(context)}", CommandOutputLevel.Warning);

    private string UsageFor(CommandContext context)
        => context.Target is not null || context.Invoker is not null ?
            "give <item-id|avatar-id|all|weapons|materials|avatars> [x<count>] [lvl<level>] [r<1-5>] [c<0-6>]" :
            Usage;

    private async Task<int> GiveMaterials(InventoryModule inventory, GiveOptions options)
    {
        var ids = data.MaterialData.Values
            .Where(material => material.Id >= 100000
                               && material.IsInventoryMaterial
                               && !IsIllegalMaterial(material.Id))
            .Select(material => material.Id)
            .Order();

        var items = await inventory.AddMaterials(ids, options.Amount, showHint: false);
        return items.Count;
    }

    private static bool IsIllegalMaterial(uint id)
        => id is 100086 or 100087 or 105001 or 105004 or 107011 or 108000
               or 220050 or 220054
           || id is >= 100100 and <= 101000
           || id is >= 101106 and <= 101110
           || id is >= 101500 and <= 104000
           || id is >= 106000 and <= 107000
           || id is >= 109000 and <= 110000
           || id is >= 115000 and <= 130000
           || id is >= 200200 and <= 200899;

    private async Task<int> GiveWeapons(InventoryModule inventory, GiveOptions options)
    {
        var resources = data.WeaponData.Values
            .Where(weapon => weapon.Id is >= 11100 and <= 16000)
            .OrderBy(weapon => weapon.Id);

        var amount = Math.Min(options.Amount, val2: 5u);

        var items = await inventory.AddWeapons(
            resources,
            amount,
            options.Level,
            options.Refinement,
            showHint: false);
        return items.Count;
    }

    private async Task<int> GiveAvatars(
        AvatarModule avatars,
        GiveOptions options,
        uint defaultConstellation
    )
    {
        var constellation = options.HasConstellation ? options.Constellation : defaultConstellation;
        var count = 0;

        foreach (var avatarId in data.AvatarData.Keys
                     .Where(id => id is >= 10000002 and < 11000000)
                     .Where(CanCreateAvatar)
                     .Order())
        {
            var (_, added) = await avatars.AddAvatar(avatarId, options.Level, constellation);

            if (added)
                count++;
        }

        return count;
    }

    private bool CanCreateAvatar(uint avatarId)
    {
        if (!data.AvatarData.TryGetValue(avatarId, out var avatar))
            return false;

        return data.AvatarSkillDepotData.ContainsKey(avatar.SkillDepotId)
               && data.WeaponData.ContainsKey(avatar.InitialWeapon)
               && data.Avatars.ContainsKey(avatarId);
    }

    private static bool TryParseOptions(string[] args, out GiveOptions options, out string error)
    {
        options = new GiveOptions();
        error = string.Empty;

        foreach (var argument in args)
        {
            if (uint.TryParse(argument, out var positionalAmount))
            {
                options.Amount = positionalAmount;
                continue;
            }

            if (!TryParseModifierChain(argument.AsSpan(), options))
            {
                error = $"Invalid give modifier '{argument}'.";
                return false;
            }
        }

        if (options.Amount == 0)
            error = "Count must be at least one.";
        else if (options.Level is < 1 or > 90)
            error = "Level must be between 1 and 90.";
        else if (options.Refinement is < 1 or > 5)
            error = "Refinement must be between 1 and 5.";
        else if (options.Constellation > 6)
            error = "Constellation must be between 0 and 6.";

        return error.Length == 0;
    }

    private static bool TryParseModifierChain(ReadOnlySpan<char> input, GiveOptions options)
    {
        var offset = 0;

        while (offset < input.Length)
        {
            Modifier modifier;

            if (input[offset..].StartsWith("lvl", StringComparison.OrdinalIgnoreCase))
            {
                modifier = Modifier.Level;
                offset += 3;
            } else if (input[offset..].StartsWith("lv", StringComparison.OrdinalIgnoreCase))
            {
                modifier = Modifier.Level;
                offset += 2;
            } else
            {
                modifier = char.ToLowerInvariant(input[offset]) switch {
                    'l' => Modifier.Level,
                    'r' => Modifier.Refinement,
                    'c' => Modifier.Constellation,
                    'x' => Modifier.Amount,
                    _ => Modifier.None
                };
                offset++;
            }

            if (modifier == Modifier.None)
                return false;

            var numberStart = offset;

            while (offset < input.Length && char.IsAsciiDigit(input[offset]))
                offset++;

            if (numberStart == offset || !uint.TryParse(input[numberStart..offset], out var value))
                return false;

            switch (modifier)
            {
                case Modifier.Amount:
                    options.Amount = value;
                    break;
                case Modifier.Level:
                    options.Level = value;
                    break;
                case Modifier.Refinement:
                    options.Refinement = value;
                    break;
                case Modifier.Constellation:
                    options.Constellation = value;
                    options.HasConstellation = true;
                    break;
            }
        }

        return true;
    }

    private sealed class GiveOptions
    {
        public uint Amount { get; set; } = 1;
        public uint Level { get; set; } = 1;
        public uint Refinement { get; set; } = 1;
        public uint Constellation { get; set; }
        public bool HasConstellation { get; set; }
    }

    private enum Modifier
    {
        None,
        Amount,
        Level,
        Refinement,
        Constellation
    }
}
