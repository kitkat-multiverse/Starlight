using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Binary;

public abstract class ConfigEntityBase
{
    [JsonPropertyName("abilities")]
    public List<ConfigAbilityData> Abilities { get; set; } = [];

    [JsonPropertyName("move")]
    public ConfigEntityMove? Move { get; set; }

    [JsonPropertyName("globalValue")]
    public ConfigEntityGlobalValue? GlobalValue { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class ConfigEntityAvatar : ConfigEntityBase
{}

public sealed class ConfigEntityMonster : ConfigEntityBase
{}

public sealed class ConfigEntityGadget : ConfigEntityBase
{}

public sealed class ConfigEntityMove
{
    [JsonPropertyName("$type")]
    public string Type { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class ConfigEntityGlobalValue
{
    [JsonPropertyName("serverGlobalValues")]
    public List<string> ServerGlobalValues { get; set; } = [];

    [JsonPropertyName("initServerGlobalValues")]
    public Dictionary<string, float> InitServerGlobalValues { get; set; } = [];
}

public sealed class ConfigLevelEntity : ConfigEntityBase
{
    [JsonPropertyName("monsterAbilities")]
    public List<ConfigAbilityData> MonsterAbilities { get; set; } = [];

    [JsonPropertyName("avatarAbilities")]
    public List<ConfigAbilityData> AvatarAbilities { get; set; } = [];

    [JsonPropertyName("teamAbilities")]
    public List<ConfigAbilityData> TeamAbilities { get; set; } = [];
}
