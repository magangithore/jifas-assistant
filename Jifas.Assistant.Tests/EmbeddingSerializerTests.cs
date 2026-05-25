using System.IO;
using Jifas.Assistant.Utilities;

namespace Jifas.Assistant.Tests;

public class EmbeddingSerializerTests
{
    [Fact]
    public void Deserialize_ReadsPreferredJsonFloatArray()
    {
        var parsed = EmbeddingSerializer.Deserialize("[0.25,-1.5,3]");

        Assert.Equal(new[] { 0.25f, -1.5f, 3f }, parsed);
    }

    [Fact]
    public void Deserialize_ReadsLegacyBase64BinaryFloatArray()
    {
        var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0.25f);
            writer.Write(-1.5f);
            writer.Write(3f);
        }

        var legacy = Convert.ToBase64String(bytes.ToArray());

        Assert.Equal(new[] { 0.25f, -1.5f, 3f }, EmbeddingSerializer.Deserialize(legacy));
    }

    [Fact]
    public void Serialize_WritesJsonFloatArray()
    {
        var serialized = EmbeddingSerializer.Serialize(new[] { 0.25f, -1.5f });

        Assert.StartsWith("[", serialized);
        Assert.Equal(new[] { 0.25f, -1.5f }, EmbeddingSerializer.Deserialize(serialized));
    }
}
