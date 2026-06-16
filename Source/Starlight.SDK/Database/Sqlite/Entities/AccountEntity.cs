using Starlight.Database.Attributes;
using Starlight.Database.ChangeTracking;

namespace Starlight.SDK.Database.Impl.Entities;

[DbTable("accounts")]
[DbIndex("ix_player_accounts_id", nameof(Id), IsUnique = true)]
public sealed class AccountEntity : TrackableEntity
{
    [DbPrimaryKey(AutoIncrement = true)]
    [DbColumn("id", IsRequired = true, IsUnique = true)]
    public uint Id
    {
        get;
        private set => Set(ref field, value);
    }

    [DbColumn("username", IsRequired = true, MaxLength = 64)]
    public string Username
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    [DbColumn("email", MaxLength = 320)]
    public string? Email
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("password", IsRequired = true, MaxLength = 320)]
    public string Password
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    [DbColumn("created_at", IsRequired = true)]
    public DateTimeOffset CreatedAt
    {
        get;
        private set => Set(ref field, value);
    } = DateTimeOffset.UtcNow;

    [DbColumn("updated_at", IsRequired = true)]
    public DateTimeOffset UpdatedAt
    {
        get;
        set => Set(ref field, value);
    } = DateTimeOffset.UtcNow;
}
