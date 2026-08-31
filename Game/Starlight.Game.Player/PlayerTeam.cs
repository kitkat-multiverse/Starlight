namespace Starlight.Game.Player;

public sealed class PlayerTeam
{
    public required uint Id { get; init; }
    public required string Name { get; set; }
    public required Avatar[] Avatars { get; set; }
    public required ulong CurrentAvatarGuid { get; set; }

    public Protocol.AvatarTeam Info() => new() {
        TeamName = Name,
        AvatarGuidList = [.. Avatars.Select(avatar => avatar.Guid)]
    };
}
