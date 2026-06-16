namespace Starlight.Game.Resources;

/// <summary>
/// All resources should have an ID value.
/// </summary>
public abstract class Data
{
    public uint Id { get; set; }

    /// <summary>
    /// Invoked when the resource is loaded.
    /// </summary>
    public virtual void OnLoad()
    {
    }
}

/// <summary>
/// The priority of a resource to be loaded.
/// Higher values are loaded first.
/// </summary>
internal enum LoadPriority
{
    Highest = 4,
    High = 3,
    Normal = 2,
    Low = 1,
    Lowest = 0
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class GameResource(string fileName) : Attribute
{
    public string FileName { get; init; } = fileName;
    public LoadPriority Priority { get; set; } = LoadPriority.Normal;
}
