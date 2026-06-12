using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Jifas.Assistant.Utilities
{
    public static class EmbeddingSerializer
    {
        public static string Serialize(float[] embedding)
        {
            if (embedding == null || embedding.Length == 0)
                return "[]";

            return JsonConvert.SerializeObject(embedding);
        }

        public static float[] Deserialize(string storedEmbedding)
        {
            if (string.IsNullOrWhiteSpace(storedEmbedding))
                return Array.Empty<float>();

            var trimmed = storedEmbedding.Trim();

            if (trimmed.StartsWith("["))
            {
                var parsed = JsonConvert.DeserializeObject<List<float>>(trimmed);
                return parsed?.ToArray() ?? Array.Empty<float>();
            }

            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                trimmed = JsonConvert.DeserializeObject<string>(trimmed) ?? string.Empty;

            var bytes = Convert.FromBase64String(trimmed);
            if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
                return Array.Empty<float>();

            var result = new float[bytes.Length / sizeof(float)];
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);

            for (var i = 0; i < result.Length; i++)
                result[i] = reader.ReadSingle();

            return result;
        }
    }
}
