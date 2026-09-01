using Microsoft.EntityFrameworkCore;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using System.ComponentModel.DataAnnotations;

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

    /// <summary>Opaque game-owned state. DbGate stores it without knowing module internals.</summary>
    public byte[] State { get; set; } = [];

    public NetPlayer Serialize() => new() {
        Uid = Id,
        AccountId = AccountId,
        Profile = Profile.Serialize(),
        State = State.Length == 0 ? new NetPlayerState() : NetPlayerState.Parser.ParseFrom(State)
    };
}
