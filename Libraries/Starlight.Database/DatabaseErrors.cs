using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Starlight.Database;

public static class DatabaseErrors
{
    /// SQLITE_CONSTRAINT_PRIMARYKEY & SQLITE_CONSTRAINT_UNIQUE.
    private const int SqlitePrimaryKey = 1555;
    private const int SqliteUnique = 2067;

    /// Kept narrow on purpose: a retry loop that also caught NOT NULL or disk-full would spin
    /// forever, since the re-read that ends it only finds rows a concurrent insert created.
    public static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqliteException {
            SqliteExtendedErrorCode: SqlitePrimaryKey or SqliteUnique
        };
}
