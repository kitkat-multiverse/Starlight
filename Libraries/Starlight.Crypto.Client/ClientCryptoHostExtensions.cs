using Microsoft.Extensions.Hosting;
using Serilog;

namespace Starlight.Crypto.Client;

/// <summary>
/// Host-builder helpers that let multiple services (e.g. the SDK server and the
/// gate server) each contribute filesystem key-path overrides for the shared
/// <see cref="ClientCrypto"/> singleton. Because per-service configs duplicate
/// the key paths, the first service to claim a slot wins; later attempts are
/// ignored and surface a debug warning to help diagnose conflicting config.
/// </summary>
public static class ClientCryptoHostExtensions
{
    private const string StateKey = "Starlight.Crypto.Client.KeyState";

    private sealed class KeyState
    {
        public ClientCryptoOptions Options { get; } = new();
        public string? SigningKeyOwner { get; set; }
        public string? SdkKeyOwner { get; set; }
    }

    private static KeyState GetState(IHostApplicationBuilder builder)
    {
        if (builder.Properties.TryGetValue(StateKey, out var existing) && existing is KeyState state)
        {
            return state;
        }

        state = new KeyState();
        state.Options.BasePath = builder.Environment.ContentRootPath;
        builder.Properties[StateKey] = state;
        return state;
    }

    /// <summary>
    /// Returns the accumulated key-path overrides contributed by the services
    /// added so far. Pass the result to <see cref="ClientCrypto.Create"/> when
    /// registering the shared singleton.
    /// </summary>
    public static ClientCryptoOptions GetClientCryptoOptions(this IHostApplicationBuilder builder)
        => GetState(builder).Options;

    /// <summary>
    /// Attempts to claim the signing ('cur') key path for <paramref name="serviceName"/>.
    /// No-op for null/whitespace paths. If another service already claimed the
    /// slot, the path is ignored and a debug warning is logged.
    /// </summary>
    public static IHostApplicationBuilder TrySetSigningKeyPath(this IHostApplicationBuilder builder, string serviceName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return builder;
        }

        var state = GetState(builder);

        if (state.SigningKeyOwner is null)
        {
            state.Options.SigningKeyPath = path;
            state.SigningKeyOwner = serviceName;
        } else if (!PathsEqual(state.Options.SigningKeyPath, path, state.Options.BasePath))
        {
            WarnConflict("signing", state.SigningKeyOwner, state.Options.SigningKeyPath, serviceName, path);
        }

        return builder;
    }

    /// <summary>
    /// Attempts to claim the SDK key path for <paramref name="serviceName"/>.
    /// No-op for null/whitespace paths. If another service already claimed the
    /// slot, the path is ignored and a debug warning is logged.
    /// </summary>
    public static IHostApplicationBuilder TrySetSdkKeyPath(this IHostApplicationBuilder builder, string serviceName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return builder;
        }

        var state = GetState(builder);

        if (state.SdkKeyOwner is null)
        {
            state.Options.SdkKeyPath = path;
            state.SdkKeyOwner = serviceName;
        } else if (!PathsEqual(state.Options.SdkKeyPath, path, state.Options.BasePath))
        {
            WarnConflict("SDK", state.SdkKeyOwner, state.Options.SdkKeyPath, serviceName, path);
        }

        return builder;
    }

    private static bool PathsEqual(string? left, string? right, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        basePath ??= Directory.GetCurrentDirectory();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left, basePath), Path.GetFullPath(right, basePath), comparison);
    }

    private static void WarnConflict(string keyName, string? owner, string? ownerPath, string serviceName, string ignoredPath)
        => Log.Warning(
            "ClientCrypto {KeyName} key path already set to '{OwnerPath}' by {Owner}; ignoring '{IgnoredPath}' from {Service}. "
            + "Per-service configs duplicate key paths and the first non-empty path to register wins. "
            + "If this is unexpected, align the key paths across service configs.",
            keyName, ownerPath, owner, ignoredPath, serviceName);
}
