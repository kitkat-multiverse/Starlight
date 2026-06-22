using System.Net;
using Starlight.Gate.Crypto;
using Starlight.Protocol;
using Starlight.Rpc;

namespace Starlight.Gate.Session.Modules;

public sealed class LoginModule(INetworkSession session)
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    [Opcode(typeof(GetPlayerTokenReq))]
    public async Task<GetPlayerTokenRsp> OnGetPlayerTokenReq(GetPlayerTokenReq msg)
    {
        // TODO: Authenticate the user. Check if their account token matches.

        // TODO: Check if the `gate_ticket` matches the expected value.
        //       This call is also where we would kick the player if they are already
        //       logged in elsewhere.

        // TODO: Pick better server based on population and load.
        session.GameTunnel = await session.Server.Tunnel.Open(GameSubjects.GateConnection, reqTimeout: ReplyTimeout);

        // Determine client seed.
        var keyId = msg.KeyId;
        var clientKey = Convert.FromBase64String(msg.ClientRandKey);
        // TODO: Decrypt client key.
        var clientSeed = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(clientKey));

        // Generate server seed.
        var serverSeed = Random.Shared.NextInt64();
        var encryptedSeed = IPAddress.HostToNetworkOrder(serverSeed);
        // TODO: Encrypt server seed.
        var serverKey = Convert.ToBase64String(BitConverter.GetBytes(serverSeed));

        // Generate the new XOR-pad from MTKey.
        var seed = clientSeed ^ serverSeed;
        session.XorPad = MtKey.Generate((ulong)seed);

        // Sign the seed's bytes.
        var seedBytes = BitConverter.GetBytes(seed);
        // TODO: Sign the combined seed bytes.

        // ... do whatever
        return new GetPlayerTokenRsp {
            ServerRandKey = serverKey,
            Sign = Convert.ToBase64String(seedBytes)
        };
    }
}
