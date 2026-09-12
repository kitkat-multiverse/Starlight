namespace Starlight.Protobuf.Registry;

/// <summary>
///     Resolves the correct <see cref="ProtocolRegistry" /> for a session, either by
///     the CmdId of its first packet (version detection) or by an explicit version
///     string. One provider indexes every registry known to the process.
/// </summary>
public interface IProtocolRegistryProvider
{
    /// <summary>All registries this provider knows about.</summary>
    IReadOnlyCollection<ProtocolRegistry> Registries { get; }

    /// <summary>
    ///     Resolves the version registry whose <see cref="ProtocolRegistry.KnownFirst" />
    ///     contains <paramref name="cmdId" />. If several versions accept the same
    ///     first-packet CmdId, the newest version wins. Returns <c>null</c> when no
    ///     registry recognizes it.
    /// </summary>
    ProtocolRegistry? ResolveByFirstPacket(int cmdId);

    /// <summary>
    ///     Returns the registry for <paramref name="version" /> (e.g. <c>"V66"</c>),
    ///     case-insensitively, or <c>null</c> if no such version is loaded.
    /// </summary>
    ProtocolRegistry? GetByVersion(string version);
}
