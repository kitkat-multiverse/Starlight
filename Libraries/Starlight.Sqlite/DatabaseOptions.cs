using System.Reflection;

namespace Starlight.Database;

public sealed class StarlightDatabaseOptions
{
    // Relative paths are resolved from the application's working directory
    public string Path { get; set; } = "./data/starlight.db";

    public bool CreateIfMissing { get; set; } = true;

    // Uses SQLite WAL mode for low-overhead concurrent reads and durable incremental writes
    public bool UseWal { get; set; } = true;

    // SQLite synchronous mode. NORMAL is a good WAL default for servers
    public string Synchronous { get; set; } = "NORMAL";

    // Busy timeout, in milliseconds, used when another connection owns a SQLite lock
    public int BusyTimeoutMilliseconds { get; set; } = 5_000;

    // Automatically create schema for all [DbTable] types found in these assemblies
    public IList<Assembly> ModelAssemblies { get; } = [];

    // Permits client-side LINQ evaluation for projections/operators that cannot be translated to SQL
    public bool AllowClientEvaluation { get; set; } = true;
}
