using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Jifas.Assistant.Data.Models;

namespace Jifas.Assistant.Data.Repositories
{
    /// <summary>
    /// Repository for Chat entities
    /// </summary>
    public interface IChatRepository : IRepository<Chat>
    {
        /// <summary>
        /// Get chats by user ID
        /// </summary>
        Task<IEnumerable<Chat>> GetByUserIdAsync(string userId);

        /// <summary>
        /// Get chats by session ID
        /// </summary>
        Task<IEnumerable<Chat>> GetBySessionIdAsync(string sessionId);

        /// <summary>
        /// Get chat history for a user in date range
        /// </summary>
        Task<IEnumerable<Chat>> GetUserHistoryAsync(string userId, DateTime from, DateTime to);
    }

    /// <summary>
    /// Chat Repository Implementation
    /// </summary>
    public class ChatRepository : Repository<Chat>, IChatRepository
    {
        public ChatRepository(JifasAssistantDbContext context) : base(context) { }

        public async Task<IEnumerable<Chat>> GetByUserIdAsync(string userId)
        {
            return await FindAsync(x => x.UserId == userId);
        }

        public async Task<IEnumerable<Chat>> GetBySessionIdAsync(string sessionId)
        {
            return await FindAsync(x => x.SessionId == sessionId);
        }

        public async Task<IEnumerable<Chat>> GetUserHistoryAsync(string userId, DateTime from, DateTime to)
        {
            return await FindAsync(x => 
                x.UserId == userId && 
                x.CreatedAt >= from && 
                x.CreatedAt <= to);
        }
    }
}
