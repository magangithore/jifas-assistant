using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Jifas.Assistant.Data.Repositories
{
    /// <summary>
    /// Generic Repository Interface for Data Access
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    public interface IRepository<TEntity> where TEntity : class
    {
        /// <summary>
        /// Get entity by ID
        /// </summary>
        Task<TEntity> GetByIdAsync(int id);

        /// <summary>
        /// Get all entities
        /// </summary>
        Task<IEnumerable<TEntity>> GetAllAsync();

        /// <summary>
        /// Find entities with filter
        /// </summary>
        Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Check if entity exists
        /// </summary>
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Add entity
        /// </summary>
        Task<TEntity> AddAsync(TEntity entity);

        /// <summary>
        /// Add multiple entities
        /// </summary>
        Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities);

        /// <summary>
        /// Update entity
        /// </summary>
        Task<TEntity> UpdateAsync(TEntity entity);

        /// <summary>
        /// Delete entity
        /// </summary>
        Task<bool> DeleteAsync(int id);

        /// <summary>
        /// Delete entity
        /// </summary>
        Task<bool> DeleteAsync(TEntity entity);

        /// <summary>
        /// Get count of entities
        /// </summary>
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null);

        /// <summary>
        /// Save changes to database
        /// </summary>
        Task<int> SaveChangesAsync();
    }
}
