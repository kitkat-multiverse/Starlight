using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

public sealed class PlayerProps
{
    private static readonly Dictionary<uint, long> Defaults = new() {
        [(uint)PlayerProperty.IsFlyable] = 1,
        [(uint)PlayerProperty.IsTransferable] = 1,
        [(uint)PlayerProperty.IsDiveable] = 1,
        [(uint)PlayerProperty.Level] = 60,
        [(uint)PlayerProperty.PlayerLevel] = 60,
        [(uint)PlayerProperty.PlayerExp] = 0,
        [(uint)PlayerProperty.MaxStamina] = 24_000,
        [(uint)PlayerProperty.CurPersistStamina] = 24_000,
        [(uint)PlayerProperty.PlayerWorldLevel] = 1
    };

    private readonly Dictionary<uint, long> _explicit = [];
    public IReadOnlyDictionary<uint, long> Explicit => _explicit;

    public long Get(PlayerProperty prop)
        => _explicit.TryGetValue((uint)prop, out var value) ? value : Defaults.GetValueOrDefault((uint)prop);

    public string? TrySet(PlayerProperty prop, long value)
    {
        if (!PlayerPropertyInfo.IsSettable(prop))
            return $"'{prop}' cannot be set.";

        var min = PlayerPropertyInfo.Min(prop);

        if (value < min)
            return $"Value for {prop} must be {DescribeRange(min, Max(prop))}.";

        var max = Max(prop);

        if (value > max)
            return $"Value for {prop} must be {DescribeRange(min, max)}.";

        if (CurrentFor(prop) is {} current && Get(current) > value)
            _explicit[(uint)current] = value;

        _explicit[(uint)prop] = value;
        return null;
    }

    public IEnumerable<KeyValuePair<uint, PropValue>> Snapshot()
    {
        foreach (var (id, value) in Defaults)
        {
            yield return new KeyValuePair<uint, PropValue>(id, ((PlayerProperty)id).Value(value));
        }

        foreach (var (id, value) in _explicit)
        {
            yield return new KeyValuePair<uint, PropValue>(id, ((PlayerProperty)id).Value(value));
        }
    }

    public static PlayerProps FromState(NetPlayerState state)
    {
        var props = new PlayerProps();

        foreach (var (id, value) in state.PropValues)
        {
            props._explicit[id] = value;
        }

        return props;
    }

    public void WriteTo(NetPlayerState state)
    {
        state.PropValues.Clear();

        foreach (var (id, value) in _explicit)
        {
            state.PropValues[id] = value;
        }
    }

    private long Max(PlayerProperty prop)
        => prop switch {
            PlayerProperty.CurSpringVolume => Get(PlayerProperty.MaxSpringVolume),
            PlayerProperty.CurPersistStamina => Get(PlayerProperty.MaxStamina),
            PlayerProperty.CurPersistDiveStamina => Get(PlayerProperty.MaxDiveStamina),
            _ => PlayerPropertyInfo.Max(prop)
        };

    internal static PlayerProperty? CurrentFor(PlayerProperty prop)
        => prop switch {
            PlayerProperty.MaxSpringVolume => PlayerProperty.CurSpringVolume,
            PlayerProperty.MaxStamina => PlayerProperty.CurPersistStamina,
            PlayerProperty.MaxDiveStamina => PlayerProperty.CurPersistDiveStamina,
            _ => null
        };

    private static string DescribeRange(long min, long max) => (min, max) switch {
        (long.MinValue, long.MaxValue) => "any value",
        (long.MinValue, _) => $"at most {max}",
        (_, long.MaxValue) => $"at least {min}",
        _ => $"between {min} and {max}"
    };
}
