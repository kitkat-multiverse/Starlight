using System.Collections;
using Starlight.Game.Resources;

namespace Starlight.Game;

/// <summary>
///     Better solution than using a plain dictionary for fight properties.
/// </summary>
public sealed class FightPropertyStore : IReadOnlyDictionary<uint, float>
{
    private readonly Dictionary<uint, float> _values = [];

    public FightPropertyStore()
    {}

    public FightPropertyStore(IEnumerable<KeyValuePair<uint, float>> values)
    {
        Replace(values);
    }

    public FightPropertyStore(IEnumerable<KeyValuePair<FightProperty, float>> values)
    {
        Replace(values.Select(pair => new KeyValuePair<uint, float>((uint)pair.Key, pair.Value)));
    }

    public float this[uint key] => _values[key];
    public IEnumerable<uint> Keys => _values.Keys;
    public IEnumerable<float> Values => _values.Values;
    public int Count => _values.Count;

    public bool ContainsKey(uint key) => _values.ContainsKey(key);
    public bool TryGetValue(uint key, out float value) => _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<uint, float>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public event Action<uint, float>? Changed;
    public event Action? Replaced;

    public float Get(uint property) => _values.GetValueOrDefault(property);
    public float Get(FightProperty property) => Get((uint)property);

    public void Set(uint property, float value)
    {
        _values[property] = value;
        Changed?.Invoke(property, value);
    }

    public void Set(FightProperty property, float value) => Set((uint)property, value);

    public void Add(uint property, float amount) => Set(property, Get(property) + amount);
    public void Add(FightProperty property, float amount) => Add((uint)property, amount);

    public void Clear()
    {
        _values.Clear();
        Replaced?.Invoke();
    }

    public bool Remove(uint property)
    {
        if (!_values.Remove(property))
            return false;

        Replaced?.Invoke();
        return true;
    }

    public void Replace(IEnumerable<KeyValuePair<uint, float>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var snapshot = values.ToArray();
        _values.Clear();

        foreach (var (property, value) in snapshot)
        {
            _values[property] = value;
        }

        Replaced?.Invoke();
    }

    public void Replace(IEnumerable<KeyValuePair<FightProperty, float>> values) =>
        Replace(values.Select(pair => new KeyValuePair<uint, float>((uint)pair.Key, pair.Value)));
}
