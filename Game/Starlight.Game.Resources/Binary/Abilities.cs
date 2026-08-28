using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Binary;

public struct Ability
{
    [JsonPropertyName("abilityName")]
    public string AbilityName { get; set; }
}
