using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Starlight.Common;
using Starlight.Ec2b;
using Starlight.Rpc.Proto;
using Starlight.SDK.Proto;

namespace Starlight.SDK.Services;

/// <summary>
/// Builds and owns the immutable dispatch payloads returned by the SDK dispatch endpoints.
/// <br/>
/// The region-list payload is cached per public base URL so reverse-proxy / direct-host deployments
/// do not rebuild protobufs on every request.
/// <br/>
/// The per-region payloads are generated on-the-fly depending on gateway circumstances.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class DispatchRegionCache
{
    private const int DispatchEc2bSeedLength = 32;
    private const int MinimumEc2bSeedLength = 16;
    private const int MaximumEc2bSeedLength = 1024;
    private const int DerivedXorpadSize = 4096;

    private readonly DispatchConfig _config;
    private readonly ILogger<DispatchRegionCache> _logger;

    /// Temporary variable before moving <see cref="SdkConfig.BindAddress"/> to another location.
    private readonly string _bindHost;
    private readonly ByteString _clientSecretKey;
    private readonly byte[] _clientSecretXorpad;

    #region Live Data

    /// Map of <c>Region ID</c> -> [<c>Server ID</c> -> <c>GateServerInfo</c>]
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, GateServerInfo>> _regions = new();

    #endregion

    #region Cache

    private readonly byte[] _clientCustomConfig;
    private readonly ConcurrentDictionary<string, ByteString> _regionKeys = new();
    private readonly ConcurrentDictionary<string, DispatchRegionConfig> _regionInfo = new();

    /// Because <c>dispatch_url</c> can vary depending on endpoint,
    /// we have a dictionary mapping request host to appropriate region list (with dispatch URL).
    private readonly ConcurrentDictionary<string, string> _regionListCache = new();

    #endregion

    public DispatchRegionCache(
        SdkConfig sdkConfig,
        DispatchConfig config,
        ILogger<DispatchRegionCache> logger
    )
    {
        _config = config;
        _logger = logger;

        // Generate client secret key.
        //
        // This is specific to the dispatch server & region list.
        // Individual regions have their own name-derived secret key.
        var clientSecretKey = CreateClientSecretKey(config);

        try
        {
            _clientSecretKey = ByteString.CopyFrom(clientSecretKey);
            _clientSecretXorpad = DeriveXorpad(clientSecretKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clientSecretKey);
        }

        _clientCustomConfig = BuildClientCustomConfig();

        var bindAddress = sdkConfig.BindAddress is "0.0.0.0" or "::" or "[::]" ? "127.0.0.1" : sdkConfig.BindAddress;
        _bindHost = $"{bindAddress}:{sdkConfig.BindPort}";

        foreach (var region in config.Regions)
        {
            _regionInfo[region.Name] = region;
            _regions[region.Name] = new ConcurrentDictionary<string, GateServerInfo>();

            var secret = Ec2bKeyGen.Create(region.Name);
            _regionKeys[region.Name] = ByteString.CopyFrom(secret);
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    #region Secret Key Generation

    private static byte[] CreateClientSecretKey(DispatchConfig config)
    {
        var seed = CreateEc2bSeed(config);

        try
        {
            var clientSecretKey = Ec2bKeyGen.Create(seed);
            ValidateEc2b(clientSecretKey, "generated clientSecretKey");
            return clientSecretKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
        }
    }

    private static byte[] CreateEc2bSeed(DispatchConfig config)
    {
        var seedLength = config.GeneratedEc2bSeedLength <= 0 ? DispatchEc2bSeedLength : config.GeneratedEc2bSeedLength;

        if (seedLength is < MinimumEc2bSeedLength or > MaximumEc2bSeedLength)
        {
            throw new InvalidOperationException(
                $"Dispatch GeneratedEc2bSeedLength must be between {MinimumEc2bSeedLength} and {MaximumEc2bSeedLength} bytes.");
        }

        return RandomNumberGenerator.GetBytes(seedLength);
    }

    private static void ValidateEc2b(byte[] ec2b, string source)
    {
        if (!Ec2bKeyGen.HasValidLayout(ec2b))
        {
            throw new InvalidOperationException(
                $"Invalid dispatch EC2B client secret from {source}: expected a {Ec2bKeyGen.Ec2bSize}-byte Ec2b buffer with a valid header.");
        }
    }

    private static byte[] DeriveXorpad(byte[] clientSecretKey)
    {
        ValidateEc2b(clientSecretKey, "clientSecretKey");

        var xorpad = Ec2bHelper.Derive(clientSecretKey);

        if (xorpad.Length != DerivedXorpadSize)
        {
            throw new InvalidOperationException(
                $"Invalid dispatch EC2B xorpad derived from {nameof(clientSecretKey)}: {xorpad.Length} bytes. Expected {DerivedXorpadSize} bytes.");
        }

        return xorpad;
    }

    #endregion

    private byte[] BuildClientCustomConfig()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(_config.ClientCustomConfig, Constants.JsonOptions);
        MinXor(payload, _clientSecretXorpad);
        return payload;
    }

    /// <summary>
    /// Adds or updates the server's info for the region,
    /// and also rebuilds the region list if a new region was registered.
    /// </summary>
    public void Update(string regionId, GateServerInfo server)
    {
        if (!_regions.TryGetValue(regionId, out var servers))
        {
            throw new ArgumentException($"Received heartbeat for unknown region '{regionId}'.", nameof(regionId));
        }
        servers[server.ServerId] = server;
        _regions[regionId] = servers;
    }

    #region Public URL Resolution

    private string ResolvePublicBaseUrl(HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(_config.PublicBaseUrl))
            return NormalizeBaseUrl(_config.PublicBaseUrl);

        var request = httpContext.Request;
        var scheme = GetFirstForwardedHeader(request, "X-Forwarded-Proto") ?? request.Scheme;
        var host = GetFirstForwardedHeader(request, "X-Forwarded-Host") ?? request.Host.Value;

        if (string.IsNullOrWhiteSpace(host))
        {
            host = _bindHost;
        }

        return NormalizeBaseUrl($"{scheme}://{host}");
    }

    private static string? GetFirstForwardedHeader(HttpRequest request, string headerName)
    {
        if (!request.Headers.TryGetValue(headerName, out var values))
            return null;

        var value = values.ToString();

        if (string.IsNullOrWhiteSpace(value))
            return null;

        var commaIndex = value.IndexOf(',');
        return (commaIndex >= 0 ? value[..commaIndex] : value).Trim();
    }

    private static string NormalizeBaseUrl(string value) => string.IsNullOrWhiteSpace(value) ?
        throw new InvalidOperationException("Dispatch public base URL cannot be empty.") :
        value.Trim().TrimEnd('/');

    #endregion

    #region Region List

    /// <summary>
    /// Fetches a cached region list based on the incoming request's host
    /// or builds it from the given data.
    /// </summary>
    public string GetRegionList(HttpContext context)
    {
        var host = ResolvePublicBaseUrl(context);
        return _regionListCache.GetOrAdd(host, BuildRegionList);
    }

    /// <summary>
    /// Rebuilds the region list for the given host address.
    /// </summary>
    /// <returns></returns>
    private string BuildRegionList(string host)
    {
        var response = new QueryRegionListHttpRsp {
            ClientSecretKey = _clientSecretKey,
            ClientCustomConfigEncrypted = ByteString.CopyFrom(_clientCustomConfig),
            EnableLoginPc = _config.EnableLoginPc
        };

        foreach (var regionInfo in _config.Regions)
        {
            response.RegionList.Add(new RegionSimpleInfo {
                Name = regionInfo.Name,
                Title = regionInfo.Title,
                Type = string.IsNullOrEmpty(regionInfo.Type) ?
                    _config.RegionType :
                    regionInfo.Type,
                DispatchUrl = string.IsNullOrEmpty(regionInfo.DispatchUrl) ?
                    BuildDispatchUrl(host, regionInfo.Name) :
                    regionInfo.DispatchUrl
            });
        }

        return Convert.ToBase64String(response.ToByteArray());
    }

    #endregion

    #region Regions

    /// <summary>
    /// Resolves the region's gateways by name (identifier).
    /// <br/>
    /// Picks the best server in the region to match the request to.
    /// </summary>
    /// <returns>Null if the region does not exist.</returns>
    public byte[]? GetRegion(string regionName)
    {
        if (!_regionInfo.TryGetValue(regionName, out var config))
            return null;

        if (!_regions.TryGetValue(regionName, out var servers))
            return null;

        if (!_regionKeys.TryGetValue(regionName, out var key))
            return null;

        // TODO: Implement proper matching algorithm.
        var target = servers.Values
            .OrderBy(s => s.Sessions.Count)
            .FirstOrDefault();

        if (target is not null)
        {
            return new QueryCurrRegionHttpRsp {
                RegionInfo = BuildRegionInfo(config, key, target),
                ClientSecretKey = key,
                // TODO: Add some kind of authentication ticket based on account ID which expires.
                ConnectGateTicket = ""
            }.ToByteArray();
        }

        _logger.LogWarning("Region {RegionName} is registered, but has no available servers.", regionName);
        return null;
    }

    private static RegionInfo BuildRegionInfo(DispatchRegionConfig region, ByteString secretKey, GateServerInfo server) => new() {
        GateserverIp = server.ExternalAddress,
        GateserverPort = Convert.ToUInt32(server.ExternalPort),
        PayCallbackUrl = region.PayCallbackUrl ?? string.Empty,
        AreaType = region.AreaType ?? string.Empty,
        ResourceUrl = region.ResourceUrl ?? string.Empty,
        DataUrl = region.DataUrl ?? string.Empty,
        FeedbackUrl = region.FeedbackUrl ?? string.Empty,
        BulletinUrl = region.BulletinUrl ?? string.Empty,
        ResourceUrlBak = region.ResourceUrlBak ?? string.Empty,
        DataUrlBak = region.DataUrlBak ?? string.Empty,
        ClientDataVersion = region.ClientDataVersion,
        HandbookUrl = region.HandbookUrl ?? string.Empty,
        ClientSilenceDataVersion = region.ClientSilenceDataVersion,
        ClientDataMd5 = region.ClientDataMd5 ?? string.Empty,
        ClientSilenceDataMd5 = region.ClientSilenceDataMd5 ?? string.Empty,
        SecretKey = secretKey,
        OfficialCommunityUrl = region.OfficialCommunityUrl ?? string.Empty,
        ClientVersionSuffix = region.ClientVersionSuffix ?? string.Empty,
        ClientSilenceVersionSuffix = region.ClientSilenceVersionSuffix ?? string.Empty,
        UseGateserverDomainName = region.UseGateserverDomainName,
        GateserverDomainName = region.GateserverDomainName ?? string.Empty,
        UserCenterUrl = region.UserCenterUrl ?? string.Empty,
        AccountBindUrl = region.AccountBindUrl ?? string.Empty,
        CdkeyUrl = region.CdkeyUrl ?? string.Empty,
        PrivacyPolicyUrl = region.PrivacyPolicyUrl ?? string.Empty,
        NextResourceUrl = region.NextResourceUrl ?? string.Empty,
        GameBiz = region.GameBiz
    };

    #endregion

    #region Helpers

    private static string BuildDispatchUrl(string host, string region)
        => $"{host}/query_cur_region/{Uri.EscapeDataString(region)}";

    /// <summary>
    /// Performs an XOR cipher on the given data.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the XOR key is empty or too small for the payload.</exception>
    private static void MinXor(Span<byte> payload, ReadOnlySpan<byte> clientSecretXorpad)
    {
        if (clientSecretXorpad.IsEmpty)
            throw new InvalidOperationException("Dispatch custom-config xorpad cannot be empty.");

        if (payload.Length > clientSecretXorpad.Length)
        {
            throw new InvalidOperationException(
                $"Dispatch custom-config payload is {payload.Length} bytes, but the derived clientSecretKey xorpad is only {clientSecretXorpad.Length} bytes.");
        }

        for (var i = 0; i < payload.Length; i++)
            payload[i] ^= clientSecretXorpad[i];
    }

    #endregion
}
