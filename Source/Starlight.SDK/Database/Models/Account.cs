using Starlight.Common;
using Starlight.SDK.Common;

namespace Starlight.SDK.Database.Models;

/// <summary>
/// Domain-model view of an SDK account. The session/combo tokens are
/// mutated by the auth service on every successful login and are persisted
/// via <see cref="Starlight.SDK.Database.IAccountRepository.UpdateSessionAsync"/>.
/// </summary>
public sealed class Account
{
    /// <summary>
    /// Max number of device ids remembered per account. Oldest entries are
    /// evicted once this is exceeded, so it bounds storage without forcing
    /// a re-verification every time the player switches between a small
    /// set of devices (e.g. mobile/PC).
    /// </summary>
    public const int MaxKnownDeviceIds = 5;

    public uint Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Argon2 password hash stored as a Base64 string with formatting.
    /// <br/>
    /// See <see cref="Starlight.Crypto.Argon2Crypto.Verify"/>.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public long PasswordTime { get; set; }

    /// <summary>
    /// ISO-3166 country code associated with the account. Populated from
    /// <see cref="Starlight.SDK.Services.IGeoIpLookup"/> on first login
    /// and refreshed on subsequent logins.
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// Real-name flow marker. One of the constants on
    /// <see cref="RealNameOperations"/>. Stored as a string so unknown
    /// values pass through without extra plumbing.
    /// </summary>
    public string RealNameOperation { get; set; } = RealNameOperations.None;

    /// <summary>Whether the account must still complete real-person verification.</summary>
    public bool RequireRealPerson { get; set; }

    /// <summary>Whether the account must still bind a "safe" mobile number.</summary>
    public bool RequireSafeMobile { get; set; }

    /// <summary>Whether the account must be reactivated by the user.</summary>
    public bool RequireActivation { get; set; }

    /// <summary>Whether the account must acknowledge a new device grant.</summary>
    public bool RequireDeviceGrant { get; set; }

    /// <summary>
    /// Combo account type. See <see cref="Starlight.SDK.Common.AccountType"/>.
    /// </summary>
    public AccountType AccountType { get; set; } = AccountType.Normal;

    /// <summary>
    /// Token returned by the shield <c>login</c> endpoint and consumed by
    /// the combo granter endpoint. Rotated on every fresh login.
    /// </summary>
    public string SessionToken { get; set; } = string.Empty;

    /// <summary>
    /// Token consumed by the gate server. Rotated on every combo exchange.
    /// </summary>
    public string ComboToken { get; set; } = string.Empty;

    /// <summary>
    /// Device ids seen on this account (most-recently-used last), set by
    /// both endpoints from the <c>x-rpc-device_id</c> request header. Kept
    /// as a small set rather than a single value so switching between a
    /// handful of known devices doesn't require re-verification each time.
    /// </summary>
    public List<string> KnownDeviceIds { get; set; } = [];

    /// <summary>
    /// Records <paramref name="deviceId"/> as seen on this account. If it's
    /// already known it's just moved to the most-recently-used position;
    /// otherwise it's appended and the oldest entry is evicted once
    /// <see cref="MaxKnownDeviceIds"/> is exceeded.
    /// </summary>
    public void RegisterDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return;

        KnownDeviceIds.Remove(deviceId);
        KnownDeviceIds.Add(deviceId);

        while (KnownDeviceIds.Count > MaxKnownDeviceIds)
            KnownDeviceIds.RemoveAt(0);
    }
}
