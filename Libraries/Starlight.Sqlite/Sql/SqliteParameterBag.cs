using Microsoft.Data.Sqlite;
using Starlight.Database.Metadata;

namespace Starlight.Database.Sql;

internal sealed class SqliteParameterBag
{
    private readonly List<SqliteParameter> _parameters = [];

    public IReadOnlyList<SqliteParameter> Parameters => _parameters;

    public string Add(object? value, DatabaseColumn? column = null)
    {
        var name = $"$p{_parameters.Count}";
        _parameters.Add(new SqliteParameter(name, SqliteValueConverter.ToDatabase(value, column) ?? DBNull.Value));
        return name;
    }

    public void Apply(SqliteCommand command)
    {
        foreach (var parameter in _parameters)
        {
            command.Parameters.Add(parameter);
        }
    }
}
