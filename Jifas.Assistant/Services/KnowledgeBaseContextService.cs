using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service untuk read dan extract informasi dari Knowledge Base files
    /// Memberikan dynamic context kepada AI Assistant tentang JIFAS system
    /// </summary>
    public interface IKnowledgeBaseContextService
    {
        Task<string> GetSystemContextAsync();
        Task<List<string>> GetAvailableTopicsAsync();
        Task<Dictionary<string, List<string>>> GetKnowledgeBaseStructureAsync();
        Task<string> GetTopicSummaryAsync(string topic);
    }

    public class KnowledgeBaseContextService : IKnowledgeBaseContextService
    {
        private readonly ILoggerService _logger;
        private readonly string _kbPath;
        private readonly ICacheService _cacheService;
        private Dictionary<string, List<string>> _kbStructure;
        private List<string> _availableTopics;

        public KnowledgeBaseContextService(
            ILoggerService logger,
            ICacheService cacheService,
            IConfiguration configuration)
        {
            _logger = logger;
            _cacheService = cacheService;
            _kbPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "KnowledgeBase"
            );
        }

        /// <summary>
        /// Get complete JIFAS system context dari Knowledge Base files
        /// </summary>
        public async Task<string> GetSystemContextAsync()
        {
            const string cacheKey = "JIFAS_SystemContext";
            
            var cached = _cacheService.Get<string>(cacheKey);
            if (cached != null)
            {
                return cached;
            }

            try
            {
                var structure = await GetKnowledgeBaseStructureAsync();
                var context = BuildSystemContext(structure);
                
                // Cache for 24 hours
                _cacheService.Set(cacheKey, context, 24 * 60);
                
                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KBContextService] Error getting system context: {ex.Message}");
                return GetDefaultSystemContext();
            }
        }

        /// <summary>
        /// Get list of available topics dari Knowledge Base
        /// </summary>
        public async Task<List<string>> GetAvailableTopicsAsync()
        {
            if (_availableTopics != null && _availableTopics.Count > 0)
            {
                return _availableTopics;
            }

            try
            {
                var structure = await GetKnowledgeBaseStructureAsync();
                _availableTopics = structure.Keys.ToList();
                return _availableTopics;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KBContextService] Error getting available topics: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Get KB folder structure dengan file list
        /// </summary>
        public async Task<Dictionary<string, List<string>>> GetKnowledgeBaseStructureAsync()
        {
            if (_kbStructure != null && _kbStructure.Count > 0)
            {
                return _kbStructure;
            }

            _kbStructure = new Dictionary<string, List<string>>();

            try
            {
                if (!Directory.Exists(_kbPath))
                {
                    _logger.LogWarning($"[KBContextService] KB folder not found: {_kbPath}");
                    return _kbStructure;
                }

                // Scan all subdirectories
                var directories = Directory.GetDirectories(_kbPath);

                foreach (var dir in directories.OrderBy(d => Path.GetFileName(d)))
                {
                    var folderName = Path.GetFileName(dir);
                    var files = Directory.GetFiles(dir, "*.txt")
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .ToList();

                    if (files.Count > 0)
                    {
                        _kbStructure[folderName] = files;
                    }
                }

                // Also add root level .txt files
                var rootFiles = Directory.GetFiles(_kbPath, "*.txt")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToList();

                if (rootFiles.Count > 0)
                {
                    _kbStructure["Root"] = rootFiles;
                }

                _logger.LogInformation($"[KBContextService] KB Structure loaded: {_kbStructure.Count} folders");

                return _kbStructure;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KBContextService] Error loading KB structure: {ex.Message}");
                return _kbStructure;
            }
        }

        /// <summary>
        /// Get summary/preview dari topic tertentu
        /// </summary>
        public async Task<string> GetTopicSummaryAsync(string topic)
        {
            try
            {
                var structure = await GetKnowledgeBaseStructureAsync();
                var summary = new System.Text.StringBuilder();

                summary.AppendLine($"**{topic}**");

                if (structure.ContainsKey(topic))
                {
                    var files = structure[topic];
                    summary.AppendLine($"Berisi {files.Count} topik:");
                    foreach (var file in files.Take(5))
                    {
                        summary.AppendLine($"  - {file}");
                    }
                    if (files.Count > 5)
                    {
                        summary.AppendLine($"  ... dan {files.Count - 5} topik lainnya");
                    }
                }

                return summary.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KBContextService] Error getting topic summary: {ex.Message}");
                return topic;
            }
        }

        /// <summary>
        /// Build system context string dari KB structure
        /// Dynamic - tidak hardcoded!
        /// </summary>
        private string BuildSystemContext(Dictionary<string, List<string>> structure)
        {
            var context = new System.Text.StringBuilder();

            context.AppendLine("Knowledge Base JIFAS System mencakup topik-topik berikut:");
            context.AppendLine();

            foreach (var folder in structure.OrderBy(k => k.Key))
            {
                context.AppendLine($"**{folder.Key}:**");
                var topics = folder.Value.OrderBy(t => t).Take(8);
                foreach (var topic in topics)
                {
                    context.AppendLine($"  - {topic}");
                }

                if (folder.Value.Count > 8)
                {
                    context.AppendLine($"  ... dan {folder.Value.Count - 8} topik lainnya");
                }
                context.AppendLine();
            }

            context.AppendLine("Setiap topik berisi dokumentasi lengkap untuk JIFAS system.");

            return context.ToString();
        }

        /// <summary>
        /// Get default system context jika KB files tidak ditemukan
        /// </summary>
        private string GetDefaultSystemContext()
        {
            return @"Knowledge Base JIFAS System mencakup:

**Master Data:**
  - Company, Department, Division, Employee, Vendor
  - COA (Chart of Account), Budget, Accounting Period
  - General Configuration

**Accounting Module:**
  - AR (Account Receivable): Invoice, Payment Receipt, Approval
  - AP (Account Payable): Invoice, Payment, Approval
  - GL (General Ledger): Transaction Posting, Reconciliation

**Payment Module:**
  - Payment Processing, BG (Bank Guarantee)
  - Cash & Bank Management
  - Payment Verification

**Budget Module:**
  - Budget Setup & Approval
  - Budget Monitoring & Realization
  - Over Budget Approval

**PUM Module:**
  - Pengajuan PUM (Dana untuk Pengeluaran Mendadak)
  - Approval Workflows
  - Realization & Reporting

**Receiving Module:**
  - Receipt of Goods (RV)
  - Tax Approval
  - Invoice Matching

**Reporting:**
  - Budget Reports
  - AR/AP Reports
  - Cash Flow & Bank Reports
  - PUM Realization Reports";
        }
    }
}
