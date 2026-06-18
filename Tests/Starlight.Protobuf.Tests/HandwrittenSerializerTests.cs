using Google.Protobuf;
using Starlight.Protobuf.Core;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Validates the runtime contract (<see cref="ISerializer{T}"/>,
/// <see cref="Core.MessageExtensions"/>) with a hand-written serializer, and confirms
/// the produced bytes equal canonical proto3 wire format.
/// </summary>
public sealed class HandwrittenSerializerTests
{
    private sealed class Sample : Core.IMessage<Sample>
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool Flag { get; set; }
        public Core.UnknownFieldSet? UnknownFields { get; set; }
    }

    private sealed class SampleSerializer : ISerializer<Sample>
    {
        public int CalculateSize(Sample m)
        {
            var size = 0;

            if (m.Id != 0)
                size += 1 + CodedOutputStream.ComputeInt32Size(m.Id);

            if (m.Name.Length != 0)
                size += 1 + CodedOutputStream.ComputeStringSize(m.Name);

            if (m.Flag)
                size += 1 + CodedOutputStream.ComputeBoolSize(m.Flag);
            return size;
        }

        public void Serialize(Sample m, CodedOutputStream output)
        {
            if (m.Id != 0)
            {
                output.WriteRawTag(0x08); // field 1, varint
                output.WriteInt32(m.Id);
            }

            if (m.Name.Length != 0)
            {
                output.WriteRawTag(0x12); // field 2, length-delimited
                output.WriteString(m.Name);
            }

            if (m.Flag)
            {
                output.WriteRawTag(0x18); // field 3, varint
                output.WriteBool(m.Flag);
            }
        }

        public void Deserialize(Sample m, CodedInputStream input)
        {
            uint tag;

            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 0x08:
                        m.Id = input.ReadInt32();
                        break;
                    case 0x12:
                        m.Name = input.ReadString();
                        break;
                    case 0x18:
                        m.Flag = input.ReadBool();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
        }
    }

    [Fact]
    public void Serialize_ProducesCanonicalProto3Bytes()
    {
        var serializer = new SampleSerializer();
        var message = new Sample { Id = 150, Name = "testing", Flag = true };

        var bytes = message.ToByteArray(serializer);

        byte[] expected = [
            0x08, 0x96, 0x01, // field 1 = 150
            0x12, 0x07, 0x74, 0x65, 0x73, 0x74, 0x69, 0x6e, 0x67, // field 2 = "testing"
            0x18, 0x01 // field 3 = true
        ];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Serialize_OmitsProto3DefaultValues()
    {
        var serializer = new SampleSerializer();
        var message = new Sample(); // all defaults

        var bytes = message.ToByteArray(serializer);

        Assert.Empty(bytes);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var serializer = new SampleSerializer();
        var original = new Sample { Id = -42, Name = "hello world", Flag = true };

        var bytes = original.ToByteArray(serializer);
        var restored = new Sample();
        restored.MergeFrom(serializer, bytes);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Flag, restored.Flag);
    }
}
