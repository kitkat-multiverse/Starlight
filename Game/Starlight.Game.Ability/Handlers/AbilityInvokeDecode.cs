using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Registry;

namespace Starlight.Game.Ability.Handlers;

internal static class AbilityInvokeDecode
{
    public static bool Try<T>(ProtocolRegistry protocol, ByteString data, out T message)
        where T : class, ISelfSerializable<T>, new()
    {
        message = new T();

        try
        {
            using var input = data.CreateCodedInput();

            // Version registries only emit serializers for messages declared by that
            // version. Shared/imported messages (for example AbilityString and
            // AbilityScalarValueEntry from define.proto) are intentionally absent,
            // so ProtocolRegistry.Deserialize would silently leave them empty.
            if (protocol.GetDescriptor(typeof(T)) is not null)
                protocol.Deserialize(message, input);
            else
                T.Serializer.Deserialize(message, input);

            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            message = null!;
            return false;
        }
    }
}
