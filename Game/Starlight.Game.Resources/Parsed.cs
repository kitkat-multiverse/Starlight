using System.Numerics;
using System.Text.Json.Serialization;

namespace Starlight.Game.Resources;

public sealed class Position
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("z")] public float Z { get; set; }

    public Vector3 AsSystem() => new() { X = X, Y = Y, Z = Z };
}
