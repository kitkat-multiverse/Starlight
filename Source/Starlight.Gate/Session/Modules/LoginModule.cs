using Google.Protobuf;
using Serilog;
using Starlight.Crypto.Client;
using Starlight.Gate.Crypto;
using Starlight.Kcp;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using System.Buffers.Binary;
using System.Security.Cryptography;

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
        // Dispatch currently advertises an empty connect_gate_ticket. Reject anything else
        // rather than silently accepting a ticket which Starlight did not issue.
        if (!string.IsNullOrEmpty(msg.Ticket))
        {
            Reject(Retcode.RETCODE_GATE_TICKET_CHECK_ERROR, msg);
            return;
        }

        // Runs before the tunnel is opened so a bad key leaves nothing to unwind.
        if (!TrySignSeed(session.Server.ClientCrypto, msg, out var seed))
        {
            Logger.Warning("Rejecting {Remote}: bad client_rand_key or unknown key_id {KeyId}.",
                session.Remote, msg.KeyId);

            Reject(Retcode.RETCODE_TOKEN_PARAM_ERROR, msg);
            return;
        }

        var account = await ValidateAccount(msg);

        if (account is null)
        {
            Reject(Retcode.RETCODE_ACCOUNT_VEIRFY_ERROR, msg);
            return;
        }

        if (account.Retcode != StarlightRetcode.Success)
        {
            Reject(account.Retcode switch {
                StarlightRetcode.AccountNotFound => Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST,
                StarlightRetcode.AccountInvalidToken => Retcode.RETCODE_TOKEN_ERROR,
                _ => Retcode.RETCODE_ACCOUNT_VEIRFY_ERROR
            }, msg);
            return;
        }

        // DbGate is the authority for the account -> player UID mapping and creates the
        // player's row on first login.
        var uid = await ResolveUid(msg.AccountUid);

        if (uid is null)
        {
            Logger.Warning("Rejecting {Remote}: no player for account '{AccountUid}'.",
                session.Remote, msg.AccountUid);

            Reject(Retcode.RETCODE_ACCOUNT_INFO_NOT_EXIST, msg);
            return;
        }

        // Tunnel routing owns game-server selection; the gate only supplies player metadata.
        var sessionInfo = new PlayerConnectNotify {
            Uid = uid.Value,
            AccountUid = msg.AccountUid,
            RemoteAddr = session.Remote.Address.ToString(),
            RemotePort = (ushort)session.Remote.Port
        }.ToByteArray();

        var gameTunnel = await session.Server.Tunnel.Open(
            GameSubjects.GateConnection, sessionInfo, ReplyTimeout, ct: session.Closing);

        // Relay packets the game server emits back down to the client.
        _ = gameTunnel.Subscribe(GameSubjects.OutboundPacket, async raw => {
            try
            {
                await session.SendAsync(raw.Decode<Starlight.Protobuf.Core.IMessage>());
            }
            catch (OperationCanceledException) when (session.Closing.IsCancellationRequested)
            {
                // The connection closed while this packet was waiting for transport capacity.
            }
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
            AccountUid = msg.AccountUid,
            Token = msg.AccountToken,
            PlatformType = msg.PlatformType,
            CountryCode = account.CountryCode,
            ClientIpStr = session.Remote.Address.ToString(),
            ClientVersionRandomKey = VersionKey,
            KeyId = msg.KeyId
        });

        // Derive the session XOR-pad from the server seed. The client recovers
        // this same value as (clientSeed ^ server_rand_key); server_rand_key
        // carries the combined seed so only the holder of clientSeed can extract it.
        session.Rekey(MtKey.Generate((ulong)seed.ServerSeed));
    }

    private async Task<ValidateAccountRsp?> ValidateAccount(GetPlayerTokenReq msg)
    {
        try
        {
            return await session.Server.Rpc.Request<ValidateAccountReq, ValidateAccountRsp>(
                SdkSubjects.ValidateAccount,
                new ValidateAccountReq {
                    AccountId = msg.AccountUid,
                    AccountToken = msg.AccountToken
                },
                ReplyTimeout,
                session.Closing);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "Failed to validate account '{AccountUid}'", msg.AccountUid);
            return null;
        }
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
