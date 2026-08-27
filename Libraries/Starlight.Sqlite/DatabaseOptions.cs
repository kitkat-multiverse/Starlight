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

    // When enabled, the schema of each mapped table is kept in sync with its model on startup.
    // Missing tables are created, and existing tables whose columns no longer match the model are safely rebuilt without dropping unrelated data.
    public bool MigrateSchema { get; set; } = true;

    // Permits client-side LINQ evaluation for projections/operators that cannot be translated to SQL
    public bool AllowClientEvaluation { get; set; } = true;
}
