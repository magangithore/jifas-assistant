using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Jifas.Assistant.Data.Repositories;

namespace Jifas.Assistant.Data.UnitOfWork
{
    /// <summary>
    /// Unit of Work Implementation
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        private readonly JifasAssistantDbContext _context;
        private IDbContextTransaction _transaction;
        private IChatRepository _chatRepository;
        private IKnowledgeBaseRepository _knowledgeBaseRepository;

        public UnitOfWork(JifasAssistantDbContext context)
        {
            _context = context;
        }

        public IChatRepository Chats
        {
            get { return _chatRepository ??= new ChatRepository(_context); }
        }

        public IKnowledgeBaseRepository KnowledgeBase
        {
            get { return _knowledgeBaseRepository ??= new KnowledgeBaseRepository(_context); }
        }

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task BeginTransactionAsync()
        {
            _transaction = await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitAsync()
        {
            try
            {
                await SaveChangesAsync();
                await _transaction?.CommitAsync();
            }
            catch
            {
                await RollbackAsync();
                throw;
            }
            finally
            {
                _transaction?.Dispose();
                _transaction = null;
            }
        }

        public async Task RollbackAsync()
        {
            try
            {
                await _transaction?.RollbackAsync();
            }
            finally
            {
                _transaction?.Dispose();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _context?.Dispose();
        }
    }
}
