using Starlight.Game.Player;
using Starlight.Game.World;
using Starlight.Protocol;

namespace Starlight.Commands;

public sealed class PropCommand(PlayerManager players) : ICommand
{
    private const int ToggleValue = -1;
    private const int OffValue = 0;
    private const int AllValue = -2;

    private readonly Dictionary<string, Entry> _entries = BuildEntries();

    private static IEnumerable<string> AdvertisedNames
        => ["god", "ns", "ue", "um", "fly", "dive", "wl", "<property-name>"];

    public string Name => "prop";
    public string Description => "Sets a player property or cheat toggle for an online player.";
    public string Usage => "prop <uid> <prop> <value>";
    public string[] Aliases => ["setprop"];

    public async Task ExecuteAsync(CommandContext context, string[] args)
    {
        var player = context.Target ?? context.Invoker;
        var selectorIndex = 0;

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

            selectorIndex = 1;
        } else if (args.Length < 1)
        {
            await Status(context, player);
            return;
        } else if (args.Length != 2)
        {
            await UsageError(context);
            return;
        }

        var name = args[selectorIndex].ToLowerInvariant();

        if (!_entries.TryGetValue(name, out var entry))
        {
            await context.ReplyAsync(
                $"'{name}' is not a known prop. Usable: {string.Join(", ", AdvertisedNames)}.",
                CommandOutputLevel.Warning);
            return;
        }

        if (!TryParseValue(args[selectorIndex + 1], out var value))
        {
            await context.ReplyAsync(
                $"'{args[selectorIndex + 1]}' is not a valid value. Use on, off, toggle, all, or a number.",
                CommandOutputLevel.Warning);
            return;
        }

        var props = player.Module<PropsModule>();

        switch (entry.Kind)
        {
            case EntryKind.NotImplemented:
                // TODO: Requires a manager that does not exist yet (see entry.RequiredManager).
                await context.ReplyAsync(
                    $"{entry.DisplayName} is not implemented yet, it requires {entry.RequiredManager}.",
                    CommandOutputLevel.Warning);
                return;

            case EntryKind.Toggle: {
                var enabled = props.SetToggle(entry.Toggle!.Value, value);
                await context.ReplyAsync($"{entry.DisplayName} is now {(enabled ? "on" : "off")} for {player.Uid}.");
                return;
            }

            case EntryKind.Map: {
                var count = await player.Module<MapModule>().UnlockAllAsync(value);
                await context.ReplyAsync($"Unlocked {count} map points for {player.Uid}.");
                return;
            }

            case EntryKind.Dive:
                if (value != OffValue)
                {
                    await props.SetPropAsync(PlayerProperty.IsDiveable, value: 1);
                    await props.SetPropAsync(PlayerProperty.MaxDiveStamina, value: 10_000);
                    await props.SetPropAsync(PlayerProperty.CurPersistDiveStamina, value: 10_000);
                    await context.ReplyAsync($"Diving is now enabled for {player.Uid}.");
                } else
                {
                    await props.SetPropAsync(PlayerProperty.IsDiveable, value: 0);
                    await props.SetPropAsync(PlayerProperty.MaxDiveStamina, value: 0);
                    await props.SetPropAsync(PlayerProperty.CurPersistDiveStamina, value: 0);
                    await context.ReplyAsync($"Diving is now disabled for {player.Uid}.");
                }

                return;

            default: {
                var error = await props.SetPropAsync(entry.Property, value);

                if (error is not null)
                    await context.ReplyAsync(error, CommandOutputLevel.Warning);
                else
                    await context.ReplyAsync($"{entry.DisplayName} for {player.Uid} set to {args[selectorIndex + 1]}.");

                return;
            }
        }
    }

    private async Task Status(CommandContext context, IPlayer player)
    {
        var props = player.Module<PropsModule>();

        await context.ReplyAsync(
            $"{player.Uid}: godmode {(props.Cheats.GodMode ? "on" : "off")}, " +
            $"world level {props.Props.Get(PlayerProperty.PlayerWorldLevel)}, " +
            $"level {props.Props.Get(PlayerProperty.PlayerLevel)}.");
    }

    private static bool TryParseValue(string input, out int value)
    {
        switch (input.ToLowerInvariant())
        {
            case "on":
            case "true":
                value = 1;
                return true;
            case "off":
            case "false":
                value = OffValue;
                return true;
            case "toggle":
                value = ToggleValue;
                return true;
            case "all":
                value = AllValue;
                return true;
            default:
                return int.TryParse(input, out value);
        }
    }

    private static Dictionary<string, Entry> BuildEntries()
    {
        var entries = new Dictionary<string, Entry>();

        foreach (var prop in Enum.GetValues<PlayerProperty>())
        {
            if (prop == PlayerProperty.None)
                continue;

            entries[prop.ToString().ToLowerInvariant()] = new Entry(prop.ToString(), EntryKind.Property, prop);
        }

        void Add(
            string displayName,
            EntryKind kind,
            IEnumerable<string> aliases,
            PlayerProperty property = default,
            CheatToggle? toggle = null,
            string? requiredManager = null
        )
        {
            var entry = new Entry(displayName, kind, property, toggle, requiredManager);

            foreach (var alias in aliases)
            {
                entries[alias] = entry;
            }
        }

        Add("World Level", EntryKind.Property, ["wl", "worldlevel"], PlayerProperty.PlayerWorldLevel);
        Add("GodMode", EntryKind.Toggle, ["god", "godmode"], toggle: CheatToggle.GodMode);

        Add("UnlimitedStamina", EntryKind.NotImplemented,
            ["ns", "us", "nostamina", "nostam", "unlimitedstamina"], requiredManager: "a StaminaManager");

        Add("UnlimitedEnergy", EntryKind.NotImplemented, ["ue", "unlimitedenergy"],
            requiredManager: "an EnergyManager");
        Add("UnlockMap", EntryKind.Map, ["um", "unlockmap"]);

        Add("IsFlyable", EntryKind.Property, ["fly", "canfly", "glider", "canglide"],
            PlayerProperty.IsFlyable);
        Add("Diving", EntryKind.Dive, ["dive", "swim", "water", "candive"]);

        return entries;
    }

    private async ValueTask UsageError(CommandContext context)
        => await context.ReplyAsync($"Usage: {UsageFor(context)}", CommandOutputLevel.Warning);

    private string UsageFor(CommandContext context)
        => context.Target is not null || context.Invoker is not null ?
            "prop <prop> <on|off|toggle|all|value>" :
            Usage;

    private enum EntryKind
    {
        Property,
        Toggle,
        Map,
        Dive,
        NotImplemented
    }

    private sealed record Entry(
        string DisplayName,
        EntryKind Kind,
        PlayerProperty Property = default,
        CheatToggle? Toggle = null,
        string? RequiredManager = null
    );
}
