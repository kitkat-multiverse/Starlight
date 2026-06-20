namespace Starlight.DbGate;

public enum ProviderType
{
    Sqlite,
    Postgres,
    MySql
}

public sealed class DbGateConfig
{
    public ProviderType Provider { get; set; } = ProviderType.Sqlite;
    public string ConnectionString { get; set; } = "Data Source=starlight.db;";
}
