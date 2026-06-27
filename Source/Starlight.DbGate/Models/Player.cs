using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Models;

[Index(nameof(Id), IsUnique = true)]
[Index(nameof(AccountId), IsUnique = true)]
public sealed record Player : IRpcSerializable<NetPlayer>
{
    /// The region-specific player ID.
    /// <br/>
    /// This is the traditional 9-digit ID you see in the
    /// bottom right corner of your screen during gameplay.
    [Key] public uint Id { get; set; }

    /// The ID assigned to the account by <see cref="Starlight.SDK"/>.
    [MaxLength(64)]
    public required string AccountId { get; set; }

    public PlayerProfile Profile { get; set; } = new();

    public NetPlayer Serialize() => new() {
        Uid = Id,
        AccountId = AccountId,
        Profile = Profile.Serialize()
    };
}
