using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Models;

public sealed class PlayerProfile : IRpcSerializable<NetPlayerProfile>
{
    [Key]
    [ForeignKey(nameof(Player))]
    public uint PlayerId { get; set; }

    [MaxLength(16)] public string Nickname { get; set; } = "Traveler";
    [MaxLength(50)] public string Signature { get; set; } = string.Empty;

    public uint PictureId { get; set; }
    public uint NameCardId { get; set; }

    #region Navigation Properties

    public Player Player { get; set; } = null!;

    #endregion

    public NetPlayerProfile Serialize() => new() {
        Nickname = Nickname,
        Signature = Signature,
        PictureId = PictureId,
        NameCardId = NameCardId
    };
}
