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
        Task<byte[]> GenerateEmbeddingAsync(string text);

        /// <summary>
        /// Generate multiple embeddings sekaligus
        /// </summary>
        Task<byte[][]> GenerateEmbeddingsAsync(string[] texts);
    }
}
