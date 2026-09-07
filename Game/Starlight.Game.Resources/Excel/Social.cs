using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("ProfilePictureExcelConfigData.json")]
public sealed class ProfilePictureData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("priority")]
    public uint Priority { get; set; }

    [JsonPropertyName("unlockParam")]
    public uint UnlockParam { get; set; }

    [JsonPropertyName("unlockType")]
    public string UnlockTypeName { get; set; } = string.Empty;

    [JsonIgnore]
    public ProfilePictureUnlockType UnlockType { get; private set; }

    public override void OnLoad()
    {
        UnlockType = UnlockTypeName switch {
            "PROFILE_PICTURE_UNLOCK_BY_AVATAR" => ProfilePictureUnlockType.Avatar,
            "PROFILE_PICTURE_UNLOCK_BY_COSTUME" => ProfilePictureUnlockType.Costume,
            "PROFILE_PICTURE_UNLOCK_BY_ITEM" => ProfilePictureUnlockType.Item,
            "PROFILE_PICTURE_UNLOCK_BY_PARENT_QUEST" => ProfilePictureUnlockType.ParentQuest,
            "PROFILE_PICTURE_UNLOCK_BY_NONE" or "PROFILE_PICTURE_UNLOCK_BY_DEFAULT" => ProfilePictureUnlockType.Default,
            _ => ProfilePictureUnlockType.Unknown
        };
    }
}

public enum ProfilePictureUnlockType
{
    Unknown,
    Default,
    Item,
    Avatar,
    Costume,
    ParentQuest
}
