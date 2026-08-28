using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Binary;

public sealed class AvatarConfig
{
    /// Declaration order is the contract: the client refers to an ability by its index in the
    /// embryo list it was sent, so reordering this rebinds every invocation.
    [JsonPropertyName("abilities")]
    public List<Ability> Abilities { get; set; } = [];

    /// Ability names, in embryo order.
    public IEnumerable<string> AbilityNames => Abilities.Select(a => a.AbilityName);
}
