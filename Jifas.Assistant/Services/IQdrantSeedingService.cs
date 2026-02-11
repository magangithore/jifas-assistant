using System.Threading.Tasks;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service to seed (index) Knowledge Base documents to Qdrant vector database
    /// Batch loads documents from SQL Server and indexes them as vectors
    /// </summary>
    public interface IQdrantSeedingService
    {
        /// <summary>
        /// Seed all knowledge base documents to Qdrant
        /// </summary>
        Task<QdrantSeedingResult> SeedAllDocumentsAsync();

        /// <summary>
        /// Seed specific document to Qdrant
        /// </summary>
        Task<bool> SeedDocumentAsync(int documentId);

        /// <summary>
        /// Clear all vectors from Qdrant and reseed
        /// </summary>
        Task<QdrantSeedingResult> ReseedAllDocumentsAsync();
    }
}
