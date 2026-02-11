using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for Knowledge Base search service
    /// </summary>
    public interface IKnowledgeBaseService
    {
        /// <summary>
        /// Search knowledge base by keyword/query
        /// </summary>
        Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 3);

        /// <summary>
        /// Get all active documents
        /// </summary>
        Task<List<KnowledgeBaseResult>> GetAllDocumentsAsync();

        /// <summary>
        /// Get document by ID
        /// </summary>
        Task<KnowledgeBaseResult> GetDocumentByIdAsync(int id);
    }
}
