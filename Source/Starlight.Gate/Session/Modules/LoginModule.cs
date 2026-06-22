using System.Buffers.Binary;
using Serilog;
using Starlight.Gate.Crypto;
using Starlight.Protocol;

namespace Starlight.Gate.Session.Modules;

public sealed class LoginModule(INetworkSession session)
{
    /// Used when computing the <c>client_version_hash</c>.
    /// We use a hardcoded value existing since the Grasscutter days.
    private const string VersionKey = "c25-314dd05b0b5f";
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    [Opcode]
    public void OnGetPlayerTokenReq(GetPlayerTokenReq msg)
    {
        // TODO: Authenticate the user. Check if their account token matches.

        // TODO: Check if the `gate_ticket` matches the expected value.
        //       This call is also where we would kick the player if they are already
        //       logged in elsewhere.

        // TODO: Pick better server based on population and load.
        // session.GameTunnel = await session.Server.Tunnel.Open(GameSubjects.GateConnection, reqTimeout: ReplyTimeout);

        var crypto = session.Server.ClientCrypto;

        // Recover the client's seed by decrypting client_rand_key with the
        // signing ('cur') key. The plaintext is a big-endian 64-bit seed.
        var keyId = (int)msg.KeyId;
        var clientKeyCipher = Convert.FromBase64String(msg.ClientRandKey);
        var clientSeed = BinaryPrimitives.ReadInt64BigEndian(crypto.DecryptWithSigningKey(clientKeyCipher));

        // Combine it with a freshly generated server seed.
        var serverSeed = Random.Shared.NextInt64();
        var combinedSeed = serverSeed ^ clientSeed;

        var seedBytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(seedBytes, combinedSeed);

        // Encrypt the combined seed with the client's content (key_id) key and
        // sign it with the signing key.
        if (!crypto.TryEncryptPayload(seedBytes, keyId, out var serverRandKey))
        {
            throw new InvalidOperationException($"No content key registered for key_id {keyId}.");
        }

        var sign = crypto.GenerateSignature(seedBytes);

        session.Send(new GetPlayerTokenRsp {
            ServerRandKey = serverRandKey,
            Sign = sign,
            // TODO: Replace with dynamic variables.
            Uid = 10001,
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
        session.XorPad = MtKey.Generate((ulong)serverSeed);
    }
}
