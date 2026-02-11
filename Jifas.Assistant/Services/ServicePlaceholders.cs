// Placeholder untuk services yang masih dalam proses migrasi
// File ini akan di-replace dengan implementasi lengkap yang menggunakan new DbContext

using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Placeholder untuk ChatService - sedang dalam proses migrasi
    /// </summary>
    public interface IChatService
    {
        Task<dynamic> ProcessMessageAsync(dynamic request);
    }

    public class ChatServicePlaceholder : IChatService
    {
        public async Task<dynamic> ProcessMessageAsync(dynamic request)
        {
            return new { message = "Service sedang dalam proses migrasi", status = "maintenance" };
        }
    }
}
