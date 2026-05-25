using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Background service that pre-warms the embedding cache on startup.
    /// Loads all chunk embeddings and metadata into static ConcurrentDictionary
    /// so the first user request does not suffer a 30–50 second cold start.
    /// </summary>
    public class EmbeddingWarmupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmbeddingWarmupService> _logger;

        public EmbeddingWarmupService(
            IServiceScopeFactory scopeFactory,
            ILogger<EmbeddingWarmupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give the app a moment to finish startup before hitting the DB
            // Retry with backoff to handle LocalDB cold-start
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    _logger.LogInformation("[EmbeddingWarmup] Starting embedding cache pre-warm (attempt {0}/{1})...", attempt, maxAttempts);
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<JIFAS_AssistantContext>();

                    var chunks = await db.KnowledgeBaseChunks
                        .Include(c => c.Document)
                        .Where(c => c.Document != null && c.Document.IsActive == true && c.Embedding != null)
                        .AsNoTracking()
                        .ToListAsync(stoppingToken);

                    int loaded = 0;
                    int failed = 0;

                    foreach (var chunk in chunks)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        try
                        {
                            if (!KnowledgeBaseSearchService.EmbeddingCache.ContainsKey(chunk.Id))
                            {
                                var parsed = JsonConvert.DeserializeObject<System.Collections.Generic.List<float>>(chunk.Embedding);
                                if (parsed != null)
                                {
                                    KnowledgeBaseSearchService.EmbeddingCache.TryAdd(chunk.Id, parsed.ToArray());
                                    KnowledgeBaseSearchService.MetadataCache.TryAdd(chunk.Id, new KnowledgeBaseChunkDto
                                    {
                                        Id = chunk.Id,
                                        DocumentId = chunk.DocumentId,
                                        Title = chunk.Document?.Title,
                                        Content = chunk.Content,
                                        Category = chunk.Document?.Category,
                                        ChunkIndex = chunk.ChunkIndex
                                    });
                                    loaded++;
                                }
                            }
                        }
                        catch
                        {
                            failed++;
                        }
                    }

                    sw.Stop();
                    _logger.LogInformation(
                        "[EmbeddingWarmup] Complete: {0} chunks cached, {1} failed in {2}ms",
                        loaded, failed, sw.ElapsedMilliseconds);
                    return; // success - done
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[EmbeddingWarmup] Warmup cancelled (app shutting down).");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EmbeddingWarmup] Attempt {0}/{1} failed: {2}", attempt, maxAttempts, ex.Message);
                    if (attempt < maxAttempts)
                    {
                        var delay = TimeSpan.FromSeconds(attempt * 10); // 10s, 20s backoff
                        _logger.LogInformation("[EmbeddingWarmup] Retrying in {0}s...", delay.TotalSeconds);
                        await Task.Delay(delay, stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex, "[EmbeddingWarmup] All {0} attempts failed. Semantic search will use on-demand loading.", maxAttempts);
                    }
                }
            }
        }
    }
}
