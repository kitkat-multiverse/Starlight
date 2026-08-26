using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("AvatarTalentExcelConfigData.json")]
public sealed class AvatarTalentData : Data
{
    [JsonPropertyName("talentId")]
    public new uint Id { get; set; }

    [JsonPropertyName("openConfig")]
    public string ConfigName { get; set; } = string.Empty;
}
