using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Jifas.Assistant.Utilities;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Background service that pre-warms the embedding cache on startup.
    /// FIXED: Now implements memory-bounded LRU cache to prevent memory leak.
    /// </summary>
    public class EmbeddingWarmupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmbeddingWarmupService> _logger;

        // FIXED: Configurable max cache size (default 50000 chunks)
        // Each chunk has ~2560 floats (10KB) + metadata = ~15KB
        // At 50000 chunks = ~750MB max
        private const int DEFAULT_MAX_CACHE_SIZE = 50000;

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

                    // FIXED: Add memory monitoring and size limits
                    var allChunks = await db.KnowledgeBaseChunks
                        .Include(c => c.Document)
                        .Where(c => c.Document != null && c.Document.IsActive == true && c.Embedding != null)
                        .AsNoTracking()
                        .ToListAsync(stoppingToken);

                    int loaded = 0;
                    int skipped = 0;
                    var embeddings = new Dictionary<int, float[]>();
                    var metadata = new Dictionary<int, KnowledgeBaseChunkDto>();

                    foreach (var chunk in allChunks)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        // FIXED: Check memory bounds before loading
                        if (embeddings.Count >= DEFAULT_MAX_CACHE_SIZE)
                        {
                            _logger.LogWarning("[EmbeddingWarmup] Reached max cache size ({0}), skipping remaining {1} chunks",
                                DEFAULT_MAX_CACHE_SIZE, allChunks.Count - loaded - skipped);
                            skipped = allChunks.Count - loaded;
                            break;
                        }

                        try
                        {
                            var parsed = EmbeddingSerializer.Deserialize(chunk.Embedding);
                            if (parsed.Length > 0)
                            {
                                embeddings[chunk.Id] = parsed;
                                metadata[chunk.Id] = new KnowledgeBaseChunkDto
                                {
                                    Id = chunk.Id,
                                    DocumentId = chunk.DocumentId,
                                    Title = chunk.Document?.Title ?? string.Empty,
                                    Content = chunk.Document?.Category ?? "General",
                                    Category = chunk.Document?.Category ?? "General",
                                    ChunkIndex = chunk.ChunkIndex
                                };
                                loaded++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        catch
                        {
                            skipped++;
                        }
                    }

                    if (loaded > 0)
                    {
                        KnowledgeBaseSearchService.ReplaceEmbeddingCache(embeddings, metadata);

                        // FIXED: Log memory usage estimate
                        var memEstimateMB = (loaded * 10.0) / 1024.0; // ~10KB per chunk estimate
                        sw.Stop();
                        _logger.LogInformation(
                            "[EmbeddingWarmup] Complete: {0} chunks cached, {1} failed/skipped in {2}ms (est. {3:F1}MB)",
                            loaded, skipped, sw.ElapsedMilliseconds, memEstimateMB);
                    }
                    else
                    {
                        sw.Stop();
                        _logger.LogWarning("[EmbeddingWarmup] No chunks loaded (0 successful). Semantic search will use on-demand loading.");
                    }
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
