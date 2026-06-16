using System.ComponentModel.DataAnnotations;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Models;

public sealed record Player : IRpcSerializable<NetPlayer>
{
    [Key] public uint Id { get; set; }

    public uint AccountId { get; set; }

    public PlayerProfile Profile { get; set; } = new();

    public NetPlayer Serialize() => new() {
        Uid = Id,
        AccountId = AccountId,
        Profile = Profile.Serialize()
    };
}
