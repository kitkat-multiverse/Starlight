using Starlight.Database.Attributes;
using Starlight.Database.ChangeTracking;

namespace Starlight.Game.Database;

[DbTable("player_accounts")]
[DbIndex("ix_player_accounts_uid", nameof(Uid), IsUnique = true)]
[DbIndex("ix_player_accounts_username", nameof(Username), IsUnique = true)] // Username as in the account username we put in Login, not the display name. Display name is not unique.
public sealed class PlayerAccount : TrackableEntity
{
    private long _id;
    private uint _uid;
    private string _username = string.Empty;
    private string _display_name = string.Empty;
    private string? _email;
    private AccountStatus _status = AccountStatus.Active;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
    private Dictionary<string, string> _metadata = []; // For storing arbitrary key-value pairs, such as linked external accounts, etc.

    [DbPrimaryKey(AutoIncrement = true)]
    [DbColumn("id")]
    public long Id
    {
        get => _id;
        private set => Set(ref _id, value);
    }

    [DbColumn("uid", IsRequired = true, IsUnique = true)]
    public uint Uid
    {
        get => _uid;
        set => Set(ref _uid, value);
    }

    [DbColumn("username", IsRequired = true, MaxLength = 64)]
    public string Username
    {
        get => _username;
        set => Set(ref _username, value);
    }

    [DbColumn("display_name", IsRequired = true, MaxLength = 64)]
    public string DisplayName
    {
        get => _display_name;
        set => Set(ref _display_name, value);
    }

    [DbColumn("email", MaxLength = 320)]
    public string? Email
    {
        get => _email;
        set => Set(ref _email, value);
    }

    [DbColumn("status", IsRequired = true, StoreEnumAsText = true)]
    public AccountStatus Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    [DbColumn("created_at", IsRequired = true)]
    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        private set => Set(ref _createdAt, value);
    }

    [DbColumn("updated_at", IsRequired = true)]
    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set => Set(ref _updatedAt, value);
    }

    [DbJson]
    [DbColumn("metadata")]
    public Dictionary<string, string> Metadata
    {
        get => _metadata;
        set => Set(ref _metadata, value);
    }

    public static PlayerAccount Create(uint uid, string username, string? email = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlayerAccount {
            Uid = uid,
            Username = username,
            Email = email,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void SetMetadata(string key, string value)
    {
        Metadata[key] = value;
        MarkDirty(nameof(Metadata));
        Touch();
    }

    public bool RemoveMetadata(string key)
    {
        var removed = Metadata.Remove(key);
        if (!removed)
            return false;

        MarkDirty(nameof(Metadata));
        Touch();
        return true;
    }

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public enum AccountStatus
{
    Active = 0,
    Banned = 1,
    Deleted = 2
}
