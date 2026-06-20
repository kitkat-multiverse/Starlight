using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Starlight.Ec2b;
using Starlight.Game;
using Starlight.Game.Protocol;
using Starlight.Protobuf.Core;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starlight.SDK.Http.Endpoints;

public static class RegionEndpoints
{
    private const string PlainTextContentType = "text/plain; charset=utf-8";

    public static void MapRegionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/query_region_list", HandleQueryRegionList);
        routes.MapGet("/query_cur_region/{name}", HandleQueryCurrentRegion);
    }

    // version=OSRELWin3.0.0&lang=1&platform=3&binary=1&time=257&channel_id=1&sub_channel_id=3
    private static IResult HandleQueryRegionList(
        HttpContext httpContext,
        [FromServices] DispatchRegionCache dispatchCache,
        [FromQuery] string? version,
        [FromQuery] string? lang,
        [FromQuery] string? platform,
        [FromQuery] string? binary,
        [FromQuery] string? time,
        [FromQuery(Name = "channel_id")] string? channelId,
        [FromQuery(Name = "sub_channel_id")] string? subChannelId
    ) => Results.Text(dispatchCache.GetRegionListBase64(httpContext), PlainTextContentType);

    private static IResult HandleQueryCurrentRegion(
        string name,
        [FromServices] DispatchRegionCache dispatchCache
    ) => dispatchCache.TryGetCurrentRegionBase64(name, out var payload) ?
        Results.Text("{\"content\":" + payload + ",\"sign\":\"TW9yZSBsb3ZlIGZvciBVQSBQYXRjaCBwbGF5ZXJz\"}", PlainTextContentType) :
        Results.NotFound($"Unknown dispatch region '{name}'.");
}

/// <summary>
/// Builds and owns the immutable dispatch payloads returned by the SDK dispatch endpoints.
/// The per-region payloads are generated once at startup; the region-list payload is cached
/// per public base URL so reverse-proxy / direct-host deployments do not rebuild protobufs on
/// every request.
/// </summary>
public sealed class DispatchRegionCache
{
    private const int DispatchEc2bSeedLength = 32;
    private const int MinimumEc2bSeedLength = 16;
    private const int MaximumEc2bSeedLength = 1024;
    private const int DerivedXorpadSize = 4096;

    private static readonly JsonSerializerOptions CustomConfigJsonOptions = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SdkConfig _sdkConfig;
    private readonly SdkDispatchConfig _dispatchConfig;
    private readonly ILogger<DispatchRegionCache> _logger;
    private readonly ByteString _clientSecretKey;
    private readonly byte[] _clientSecretXorpad;
    private readonly byte[] _clientCustomConfigPayload;
    private readonly IReadOnlyList<SdkDispatchRegionConfig> _regions;
    private readonly IReadOnlyDictionary<string, string> _currentRegionPayloads;
    private readonly ConcurrentDictionary<string, string> _regionListPayloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly GateConfig _gateConfig;

    public DispatchRegionCache(
        SdkConfig sdkConfig,
        GateConfig gateConfig,
        ILogger<DispatchRegionCache> logger
    )
    {
        _sdkConfig = sdkConfig;
        _dispatchConfig = sdkConfig.Dispatch ?? new SdkDispatchConfig();
        _gateConfig = gateConfig;
        _logger = logger;

        _regions = NormalizeRegions(_dispatchConfig);

        var clientSecretKey = CreateClientSecretKey(_dispatchConfig);

        try
        {
            _clientSecretKey = ByteString.CopyFrom(clientSecretKey);
            _clientSecretXorpad = DeriveXorpadFromClientSecretKey(_clientSecretKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clientSecretKey);
        }

        _clientCustomConfigPayload = BuildClientCustomConfigPayload(_dispatchConfig.ClientCustomConfig, _clientSecretXorpad);
        _currentRegionPayloads = BuildCurrentRegionPayloads(_regions, _clientSecretKey, _gateConfig);

        _logger.LogInformation(
            "Initialized SDK dispatch cache with {RegionCount} region(s), an in-memory {Ec2bLength}-byte EC2B client secret, and an in-memory {XorpadLength}-byte derived xorpad; no key material was written to disk",
            _regions.Count,
            _clientSecretKey.Length,
            _clientSecretXorpad.Length);
    }

    /// <summary>
    /// Gets the derived XOR pad for the cached dispatch EC2B key. The game server can use this
    /// same material for packet encryption/decryption without deriving it again.
    /// </summary>
    public ReadOnlyMemory<byte> ClientSecretXorpad => _clientSecretXorpad;

    public string GetRegionListBase64(HttpContext httpContext)
    {
        var publicBaseUrl = ResolvePublicBaseUrl(httpContext);
        return _regionListPayloads.GetOrAdd(publicBaseUrl, BuildRegionListPayload);
    }

    public bool TryGetCurrentRegionBase64(string name, out string payload)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            payload = string.Empty;
            return false;
        }

        return _currentRegionPayloads.TryGetValue(name, out payload!);
    }

    private string BuildRegionListPayload(string publicBaseUrl)
    {
        var response = new QueryRegionListHttpRsp {
            ClientSecretKey = _clientSecretKey,
            ClientCustomConfigEncrypted = ByteString.CopyFrom(_clientCustomConfigPayload),
            EnableLoginPc = _dispatchConfig.EnableLoginPc
        };

        foreach (var region in _regions)
        {
            response.RegionList.Add(new RegionSimpleInfo {
                Name = region.Name,
                Title = region.Title,
                Type = string.IsNullOrWhiteSpace(region.Type) ? _dispatchConfig.RegionType : region.Type,
                DispatchUrl = BuildRegionDispatchUrl(publicBaseUrl, region)
            });
        }

        var encoded = Convert.ToBase64String(response.ToByteArray());
        _logger.LogDebug("Built cached query_region_list payload for {PublicBaseUrl}", publicBaseUrl);
        return encoded;
    }

    private static IReadOnlyDictionary<string, string> BuildCurrentRegionPayloads(
        IEnumerable<SdkDispatchRegionConfig> regions,
        ByteString clientSecretKey,
        GateConfig gateConfig
    )
    {
        var payloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in regions)
        {
            // from GateConfig
            var regionInfo = new RegionInfo {
                GateserverIp = gateConfig.BindAddress,
                GateserverPort = Convert.ToUInt32(gateConfig.BindPort),
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
                SecretKey = clientSecretKey,
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
                GameBiz = region.GameBiz ?? string.Empty
            };

            var response = new QueryCurrRegionHttpRsp {
                RegionInfo = regionInfo,
                ClientSecretKey = clientSecretKey,
                ConnectGateTicket = region.ConnectGateTicket ?? string.Empty
            };

            payloads.Add(region.Name, Convert.ToBase64String(response.ToByteArray()));
        }

        return payloads;
    }

    private string ResolvePublicBaseUrl(HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(_dispatchConfig.PublicBaseUrl))
            return NormalizeBaseUrl(_dispatchConfig.PublicBaseUrl);

        var request = httpContext.Request;
        var scheme = GetFirstForwardedHeader(request, "X-Forwarded-Proto") ?? request.Scheme;
        var host = GetFirstForwardedHeader(request, "X-Forwarded-Host") ?? request.Host.Value;

        if (string.IsNullOrWhiteSpace(host))
        {
            var bindAddress = _sdkConfig.BindAddress is "0.0.0.0" or "::" or "[::]" ? "127.0.0.1" : _sdkConfig.BindAddress;

            host = $"{bindAddress}:{_sdkConfig.BindPort}";
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

    private static string NormalizeBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Dispatch public base URL cannot be empty.");

        return value.Trim().TrimEnd('/');
    }

    private static string BuildRegionDispatchUrl(string publicBaseUrl, SdkDispatchRegionConfig region)
    {
        if (!string.IsNullOrWhiteSpace(region.DispatchUrl))
            return region.DispatchUrl.Trim();

        return $"{publicBaseUrl}/query_cur_region/{Uri.EscapeDataString(region.Name)}";
    }

    private static IReadOnlyList<SdkDispatchRegionConfig> NormalizeRegions(SdkDispatchConfig config)
    {
        var sourceRegions = config.Regions is { Count: > 0 } ?
            config.Regions :
            new List<SdkDispatchRegionConfig> { new() { Name = config.DefaultRegionName } };

        var normalized = new List<SdkDispatchRegionConfig>(sourceRegions.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in sourceRegions)
        {
            region.Name = NormalizeRequired(region.Name, nameof(region.Name), config.DefaultRegionName);
            region.Title = string.IsNullOrWhiteSpace(region.Title) ? region.Name : region.Title.Trim();

            region.Type = string.IsNullOrWhiteSpace(region.Type) ?
                string.IsNullOrWhiteSpace(config.RegionType) ? "DEV_PUBLIC" : config.RegionType.Trim() :
                region.Type.Trim();

            if (!names.Add(region.Name))
                throw new InvalidOperationException($"Duplicate dispatch region name '{region.Name}'. Region names must be unique.");

            normalized.Add(region);
        }

        return normalized;
    }

    private static string NormalizeRequired(string? value, string propertyName, string? fallback = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value;

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"Dispatch {propertyName} cannot be empty.");

        return normalized.Trim();
    }

    private static byte[] CreateClientSecretKey(SdkDispatchConfig config)
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

    private static byte[] DeriveXorpadFromClientSecretKey(ByteString clientSecretKey)
    {
        var clientSecretKeyBytes = clientSecretKey.ToByteArray();

        try
        {
            ValidateEc2b(clientSecretKeyBytes, "clientSecretKey");

            var xorpad = Starlight.Ec2b.Ec2b.Derive(clientSecretKeyBytes);
            ValidateXorpad(xorpad, "clientSecretKey");
            return xorpad;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clientSecretKeyBytes);
        }
    }

    private static byte[] CreateEc2bSeed(SdkDispatchConfig config)
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

    private static void ValidateXorpad(byte[] xorpad, string source)
    {
        if (xorpad.Length != DerivedXorpadSize)
        {
            throw new InvalidOperationException(
                $"Invalid dispatch EC2B xorpad derived from {source}: {xorpad.Length} bytes. Expected {DerivedXorpadSize} bytes.");
        }
    }

    private static byte[] BuildClientCustomConfigPayload(
        SdkDispatchClientCustomConfig? customConfig,
        ReadOnlySpan<byte> clientSecretXorpad
    )
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            customConfig ?? new SdkDispatchClientCustomConfig(),
            CustomConfigJsonOptions);

        XorWithClientSecretXorpad(payload, clientSecretXorpad);
        return payload;
    }

    private static void XorWithClientSecretXorpad(Span<byte> payload, ReadOnlySpan<byte> clientSecretXorpad)
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
}
