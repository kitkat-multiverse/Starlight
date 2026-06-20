using Starlight.Database.Attributes;
using Starlight.Database.ChangeTracking;
using Starlight.SDK.Common;

namespace Starlight.SDK.Database.Impl.Entities;

[DbTable("accounts")]
[DbIndex("ix_player_accounts_id", nameof(Id), IsUnique = true)]
[DbIndex("ix_player_accounts_username", nameof(Username), IsUnique = true)]
[DbIndex("ix_player_accounts_email", nameof(Email))]
[DbIndex("ix_player_accounts_session_token", nameof(SessionToken))]
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

    /// <summary>Argon2id password hash formatted as <c>argon2id$[hash]$[salt]</c> (Base64 segments).</summary>
    [DbColumn("password", IsRequired = true, MaxLength = 320)]
    public string Password
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    [DbColumn("password_time")]
    public long? PasswordTime
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("country", MaxLength = 8)]
    public string? Country
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("realname_operation", MaxLength = 32)]
    public string? RealNameOperation
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("require_real_person", IsRequired = true)]
    public bool RequireRealPerson
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("require_safe_mobile", IsRequired = true)]
    public bool RequireSafeMobile
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("require_activation", IsRequired = true)]
    public bool RequireActivation
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("require_device_grant", IsRequired = true)]
    public bool RequireDeviceGrant
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("account_type", IsRequired = true)]
    public int AccountType
    {
        get;
        set => Set(ref field, value);
    } = (int)Starlight.SDK.Common.AccountType.Normal;

    [DbColumn("session_token", MaxLength = 64)]
    public string? SessionToken
    {
        get;
        set => Set(ref field, value);
    }

    [DbColumn("combo_token", MaxLength = 64)]
    public string? ComboToken
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Pipe-delimited list of known device ids (see <see cref="Starlight.SDK.Database.Models.Account.KnownDeviceIds"/>).
    /// </summary>
    [DbColumn("known_device_ids", MaxLength = 768)]
    public string? KnownDeviceIds
    {
        get;
        set => Set(ref field, value);
    }

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
