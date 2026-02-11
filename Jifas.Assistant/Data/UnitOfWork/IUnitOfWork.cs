using System;
using System.Threading.Tasks;
using Jifas.Assistant.Data.Repositories;

namespace Jifas.Assistant.Data.UnitOfWork
{
    /// <summary>
    /// Unit of Work Interface untuk mengelola multiple repositories
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// Chat Repository
        /// </summary>
        IChatRepository Chats { get; }

        /// <summary>
        /// Knowledge Base Repository
        /// </summary>
        IKnowledgeBaseRepository KnowledgeBase { get; }

        /// <summary>
        /// Save changes to database
        /// </summary>
        Task<int> SaveChangesAsync();

        /// <summary>
        /// Begin transaction
        /// </summary>
        Task BeginTransactionAsync();

        /// <summary>
        /// Commit transaction
        /// </summary>
        Task CommitAsync();

        /// <summary>
        /// Rollback transaction
        /// </summary>
        Task RollbackAsync();
    }
}
