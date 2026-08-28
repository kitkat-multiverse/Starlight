using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using Serilog;
using Starlight.Crypto.Client;
using Starlight.Gate.Crypto;
using Starlight.Kcp;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.Gate.Session.Modules;

public sealed class LoginModule(INetworkSession session)
{
    private static readonly ILogger Logger = Log.ForContext<LoginModule>();

    /// Used when computing the <c>client_version_hash</c>.
    /// We use a hardcoded value existing since the Grasscutter days.
    private const string VersionKey = "c25-314dd05b0b5f";
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    [Opcode]
    public async Task OnGetPlayerTokenReq(GetPlayerTokenReq msg)
    {
        // TODO: Authenticate the user. Check if their account token matches.

        // TODO: Check if the `gate_ticket` matches the expected value.
        //       This call is also where we would kick the player if they are already
        //       logged in elsewhere.

        // Runs before the tunnel is opened so a bad key leaves nothing to unwind.
        if (!TrySignSeed(session.Server.ClientCrypto, msg, out var seed))
        {
            Logger.Warning("Rejecting {Remote}: bad client_rand_key or unknown key_id {KeyId}.",
                session.Remote, msg.KeyId);

            Reject(Retcode.RETCODE_TOKEN_PARAM_ERROR, msg);
            return;
        }

        var uid = await ResolveUid(msg.AccountUid);

        if (uid is null)
        {
            Logger.Warning("Rejecting {Remote}: no player for account '{AccountUid}'.",
                session.Remote, msg.AccountUid);

            Reject(Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST, msg);
            return;
        }

        // TODO: Pick better server based on population and load.
        var sessionInfo = new PlayerConnectNotify {
            Uid = uid.Value,
            AccountUid = msg.AccountUid,
            RemoteAddr = session.Remote.Address.ToString(),
            RemotePort = (ushort)session.Remote.Port
        }.ToByteArray();

        var gameTunnel = await session.Server.Tunnel.Open(
            GameSubjects.GateConnection, sessionInfo, ReplyTimeout, ct: session.Closing);

        // Relay packets the game server emits back down to the client.
        _ = gameTunnel.Subscribe(GameSubjects.OutboundPacket, raw => {
            session.Send(raw.Decode<Starlight.Protobuf.Core.IMessage>());
            return Task.CompletedTask;
        });

        // Drop the client when the game server asks us to.
        _ = gameTunnel.Subscribe(GameSubjects.Disconnect, raw => {
            var notify = raw.Decode<DisconnectNotify>();
            session.Disconnect(notify.Reason, notify.Flush);
            return Task.CompletedTask;
        });

        // Subscribing first means the tunnel is usable the moment it is visible to the session.
        if (!session.AttachTunnel(gameTunnel))
            return;

        session.Send(new GetPlayerTokenRsp {
            ServerRandKey = seed.ServerRandKey,
            Sign = seed.Signature,
            Uid = uid.Value,
            // TODO: Replace with dynamic variables.
            AccountUid = msg.AccountUid,
            Token = "somethingreallylong",
            PlatformType = msg.PlatformType,
            CountryCode = "US",
            ClientIpStr = "127.0.0.1",
            ClientVersionRandomKey = VersionKey,
            KeyId = msg.KeyId
        });

        // Derive the session XOR-pad from the server seed. The client recovers
        // this same value as (clientSeed ^ server_rand_key); server_rand_key
        // carries the combined seed so only the holder of clientSeed can extract it.
        session.Rekey(MtKey.Generate((ulong)seed.ServerSeed));
    }

    /// <summary>Looks up the uid backing <paramref name="accountUid"/>, or null if it can't be had.</summary>
    private async Task<uint?> ResolveUid(string accountUid)
    {
        try
        {
            var response = await session.Server.Rpc.Request<FetchPlayerReq, FetchPlayerRsp>(
                GameSubjects.FetchPlayer,
                new FetchPlayerReq { AccountUid = accountUid, Create = true },
                ct: session.Closing);

            return response is { Player: {} player, Retcode: StarlightRetcode.Success } ? player.Uid : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "Failed to resolve a uid for account '{AccountUid}'", accountUid);
            return null;
        }
    }

    private void Reject(Retcode retcode, GetPlayerTokenReq msg)
    {
        session.Send(new GetPlayerTokenRsp {
            Retcode = (int)retcode,
            AccountUid = msg.AccountUid,
            KeyId = msg.KeyId
        });
        session.Disconnect((uint)DisconnectReason.ServerKick, flush: true);
    }

    /// Mixes the client's seed with a fresh server one, then encrypts and signs the result.
    /// Returns <c>false</c> for any unusable key material.
    private static bool TrySignSeed(ClientCrypto crypto, GetPlayerTokenReq msg, out SeedExchange seed)
    {
        seed = default;

        if (!crypto.CanSign)
        {
            return false;
        }

        // client_rand_key holds a big-endian 64-bit seed encrypted against the signing ('cur') key.
        var cipher = new byte[msg.ClientRandKey.Length / 4 * 3];

        if (!Convert.TryFromBase64String(msg.ClientRandKey, cipher, out var length)
            || !crypto.TryDecryptWithSigningKey(cipher[..length], out var clientKey)
            || clientKey.Length < sizeof(long))
        {
            return false;
        }

        var serverSeed = BinaryPrimitives.ReadInt64BigEndian(RandomNumberGenerator.GetBytes(sizeof(long)));
        var combinedSeed = serverSeed ^ BinaryPrimitives.ReadInt64BigEndian(clientKey);

        var seedBytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(seedBytes, combinedSeed);

        // Encrypt the combined seed with the client's content (key_id) key and
        // sign it with the signing key.
        if (!crypto.TryEncryptPayload(seedBytes, (int)msg.KeyId, out var serverRandKey))
        {
            return false;
        }

        seed = new SeedExchange(serverSeed, serverRandKey, crypto.GenerateSignature(seedBytes));
        return true;
    }

    private readonly record struct SeedExchange(long ServerSeed, string ServerRandKey, string Signature);
}
