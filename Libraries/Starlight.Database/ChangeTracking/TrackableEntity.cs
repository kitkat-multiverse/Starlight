using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Starlight.Database.Attributes;

namespace Starlight.Database.ChangeTracking;

public interface IDirtyTrackable : INotifyPropertyChanged
{
    IReadOnlyCollection<string> DirtyProperties { get; }
    void AcceptChanges();
}

// Optional base class for models that want zero-scan dirty tracking.
public abstract class TrackableEntity : IDirtyTrackable
{
    private readonly HashSet<string> _dirtyProperties = new(StringComparer.Ordinal);

    [DbIgnore]
    public event PropertyChangedEventHandler? PropertyChanged;

    [DbIgnore]
    public IReadOnlyCollection<string> DirtyProperties => new ReadOnlyCollection<string>(_dirtyProperties.ToArray());

    public void AcceptChanges() => _dirtyProperties.Clear();

    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;

        MarkDirty(propertyName);
        return true;
    }

    protected void MarkDirty([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            _dirtyProperties.Add(propertyName);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
