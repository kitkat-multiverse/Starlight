namespace Starlight.Gate;

public abstract class GateServerException(string message) : Exception(message);

/// <summary>
/// Thrown when the registry lookup fails for a given packet ID.
/// </summary>
/// <param name="cmdId">The ID of the first packet received.</param>
public sealed class MissingRegistryException(uint cmdId) : GateServerException($"No protocol registry found for packet {cmdId}");
