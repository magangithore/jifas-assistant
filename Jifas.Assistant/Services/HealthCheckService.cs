using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    public interface IHealthCheckService
    {
        Task<HealthStatus> GetHealthStatusAsync();
        Task<Dictionary<string, object>> GetDetailedStatusAsync();
    }

    public class HealthStatus
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = "Unknown";
        public DateTime CheckedAt { get; set; }
        public Dictionary<string, string> Components { get; set; } = new();
    }

    public class HealthCheckService : IHealthCheckService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly ILoggerService _logger;
        private readonly IWebHostEnvironment _environment;

        public HealthCheckService(
            jifas_assistant.DAL.Models.JIFAS_AssistantContext db,
            ILoggerService logger,
            IWebHostEnvironment environment)
        {
            _db = db;
            _logger = logger;
            _environment = environment;
        }

        public async Task<HealthStatus> GetHealthStatusAsync()
        {
            var status = new HealthStatus
            {
                CheckedAt = DateTime.Now,
                Components = new()
            };

            try
            {
                // Cek koneksi database untuk readiness API.
                var canConnect = await _db.Database.CanConnectAsync();
                status.Components["Database"] = canConnect ? "OK" : "Failed";
                status.IsHealthy = canConnect;
                status.Status = canConnect ? "HEALTHY" : "DEGRADED";
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HealthCheckService] Error: {ex.Message}");
                status.Components["Database"] = _environment.IsDevelopment()
                    ? "Error: " + ex.Message
                    : "Error";
                status.IsHealthy = false;
                status.Status = "UNHEALTHY";
            }

            return status;
        }

        public async Task<Dictionary<string, object>> GetDetailedStatusAsync()
        {
            var detailed = new Dictionary<string, object>();
            
            try
            {
                var canConnect = await _db.Database.CanConnectAsync();
                detailed["Database"] = canConnect ? "HEALTHY" : "UNHEALTHY";
                detailed["CheckedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                
                if (canConnect)
                {
                    try
                    {
                        var docCount = _db.KnowledgeBaseDocuments.Count();
                        var chunkCount = _db.KnowledgeBaseChunks.Count();
                        detailed["KBDocuments"] = docCount;
                        detailed["KBChunks"] = chunkCount;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                detailed["Error"] = _environment.IsDevelopment()
                    ? ex.Message
                    : "Health check gagal.";
            }
            
            return detailed;
        }
    }
}
