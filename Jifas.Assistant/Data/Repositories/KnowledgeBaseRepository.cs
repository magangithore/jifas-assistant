using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Jifas.Assistant.Data.Models;

namespace Jifas.Assistant.Data.Repositories
{
    /// <summary>
    /// Repository for KnowledgeBaseDocument entities
    /// </summary>
    public interface IKnowledgeBaseRepository : IRepository<KnowledgeBaseDocument>
    {
        /// <summary>
        /// Get documents by category
        /// </summary>
        Task<IEnumerable<KnowledgeBaseDocument>> GetByCategoryAsync(string category);

        /// <summary>
        /// Get active documents only
        /// </summary>
        Task<IEnumerable<KnowledgeBaseDocument>> GetActiveAsync();

        /// <summary>
        /// Search documents by title
        /// </summary>
        Task<IEnumerable<KnowledgeBaseDocument>> SearchByTitleAsync(string searchTerm);

        /// <summary>
        /// Get documents with tags
        /// </summary>
        Task<IEnumerable<KnowledgeBaseDocument>> GetByTagsAsync(string tags);
    }

    /// <summary>
    /// Knowledge Base Repository Implementation
    /// </summary>
    public class KnowledgeBaseRepository : Repository<KnowledgeBaseDocument>, IKnowledgeBaseRepository
    {
        public KnowledgeBaseRepository(JifasAssistantDbContext context) : base(context) { }

        public async Task<IEnumerable<KnowledgeBaseDocument>> GetByCategoryAsync(string category)
        {
            return await FindAsync(x => x.Category == category && x.IsActive);
        }

        public async Task<IEnumerable<KnowledgeBaseDocument>> GetActiveAsync()
        {
            return await FindAsync(x => x.IsActive);
        }

        public async Task<IEnumerable<KnowledgeBaseDocument>> SearchByTitleAsync(string searchTerm)
        {
            return await FindAsync(x => 
                x.IsActive && 
                x.Title.Contains(searchTerm));
        }

        public async Task<IEnumerable<KnowledgeBaseDocument>> GetByTagsAsync(string tags)
        {
            return await FindAsync(x => 
                x.IsActive && 
                x.Tags.Contains(tags));
        }
    }
}
