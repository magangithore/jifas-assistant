using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface untuk embedding service (konversi text ke vector)
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Generate embedding (vector) dari text
        /// </summary>
        Task<byte[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate multiple embeddings sekaligus
        /// </summary>
        Task<byte[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate embedding as float[] for semantic search compatibility
        /// </summary>
        Task<float[]> GenerateEmbeddingAsFloatArrayAsync(string text, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Extension methods for embedding byte[] to float[] conversion
    /// </summary>
    public static class EmbeddingExtensions
    {
        /// <summary>
        /// Convert byte[] embedding (BinaryWriter float format) to float[]
        /// </summary>
        public static float[] ToFloatArray(this byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return Array.Empty<float>();

            var floatCount = bytes.Length / sizeof(float);
            var result = new float[floatCount];

            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            for (int i = 0; i < floatCount; i++)
            {
                result[i] = reader.ReadSingle();
            }

            return result;
        }
    }
}
