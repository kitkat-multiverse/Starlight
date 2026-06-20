namespace Starlight.SDK.Common;

/// <summary>
/// SQLite primary/extended error codes used by the SDK to detect
/// unique-constraint violations during the read-then-create race in
/// <see cref="Starlight.SDK.Services.AuthService.LoginAsync"/> and
/// <see cref="Starlight.SDK.Http.Endpoints.PassportEndpoints"/>.
/// </summary>
/// <remarks>
/// Lifted out of <c>Microsoft.Data.Sqlite.SqliteException</c> result
/// codes. See <a href="https://www.sqlite.org/rescode.html">the SQLite
/// documentation</a> for the full table.
/// </remarks>
public static class SqliteErrorCodes
{
    /// <summary>Primary result code: a constraint was violated.</summary>
    public const int Constraint = 19;

    /// <summary>Extended result code: a UNIQUE constraint was violated.</summary>
    public const int ConstraintUnique = 2067;

    /// <summary>Extended result code: a PRIMARY KEY constraint was violated.</summary>
    public const int ConstraintPrimaryKey = 1555;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="ex"/> represents a
    /// unique-constraint or primary-key violation, i.e. the
    /// "someone else inserted the row between our read and our write"
    /// race condition the auto-create-on-login flow recovers from.
    /// </summary>
    public static bool IsUniqueConstraintViolation(Microsoft.Data.Sqlite.SqliteException ex) =>
        ex.SqliteErrorCode == Constraint
        && (ex.SqliteExtendedErrorCode == ConstraintUnique
            || ex.SqliteExtendedErrorCode == ConstraintPrimaryKey);
}
