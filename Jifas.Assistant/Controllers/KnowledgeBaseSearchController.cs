using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jifas.Assistant.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Knowledge Base Search API
    /// Provides keyword and semantic search capabilities for JIFAS KB
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class KnowledgeBaseSearchController : ControllerBase
    {
        private readonly IKnowledgeBaseSearchService _searchService;

        public KnowledgeBaseSearchController(IKnowledgeBaseSearchService searchService)
        {
            _searchService = searchService;
        }

        /// <summary>
        /// Search knowledge base by keyword
        /// </summary>
        /// <param name="query">Search query (keywords)</param>
        /// <param name="topK">Number of results to return (default: 5)</param>
        /// <returns>List of relevant KB chunks</returns>
        [HttpGet("keyword")]
        [ProducesResponseType(typeof(List<KnowledgeBaseChunkDto>), 200)]
        public async Task<IActionResult> SearchByKeyword([FromQuery] string query, [FromQuery] int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { error = "Query cannot be empty" });
            }

            if (topK <= 0 || topK > 20)
                topK = 5;

            var results = await _searchService.SearchByKeywordAsync(query, topK);
            return Ok(new
            {
                query,
                resultsCount = results.Count,
                results
            });
        }

        /// <summary>
        /// Search knowledge base by semantic similarity
        /// Requires embedding vector as input
        /// </summary>
        /// <param name="embedding">Query embedding vector (3072 dimensions)</param>
        /// <param name="topK">Number of results to return (default: 5)</param>
        /// <returns>List of semantically similar KB chunks</returns>
        [HttpPost("semantic")]
        [ProducesResponseType(typeof(List<KnowledgeBaseChunkDto>), 200)]
        public async Task<IActionResult> SearchBySemantic([FromBody] SemanticSearchRequest request)
        {
            if (request?.Embedding == null || request.Embedding.Length == 0)
            {
                return BadRequest(new { error = "Embedding vector required" });
            }

            if (request.TopK <= 0 || request.TopK > 20)
                request.TopK = 5;

            var results = await _searchService.SearchBySemanticAsync(request.Embedding, request.TopK);
            return Ok(new
            {
                embeddingDimensions = request.Embedding.Length,
                resultsCount = results.Count,
                results
            });
        }

        /// <summary>
        /// Hybrid search: keyword + semantic
        /// </summary>
        /// <param name="request">Search request with query and optional embedding</param>
        /// <returns>List of relevant KB chunks (hybrid results)</returns>
        [HttpPost("search")]
        [ProducesResponseType(typeof(List<KnowledgeBaseChunkDto>), 200)]
        public async Task<IActionResult> Search([FromBody] KnowledgeBaseSearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Query))
            {
                return BadRequest(new { error = "Query cannot be empty" });
            }

            if (request.TopK <= 0 || request.TopK > 20)
                request.TopK = 5;

            var results = await _searchService.SearchAsync(
                request.Query,
                request.Embedding,
                request.TopK
            );

            return Ok(new
            {
                query = request.Query,
                searchType = request.Embedding != null ? "hybrid" : "keyword",
                resultsCount = results.Count,
                results
            });
        }

        /// <summary>
        /// Health check for KB search service
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "KB Search Service is operational" });
        }
    }

    /// <summary>
    /// Request model for semantic search
    /// </summary>
    public class SemanticSearchRequest
    {
        public float[]? Embedding { get; set; }
        public int TopK { get; set; } = 5;
    }

    /// <summary>
    /// Request model for hybrid search (keyword + semantic)
    /// </summary>
    public class KnowledgeBaseSearchRequest
    {
        public string? Query { get; set; }
        public float[]? Embedding { get; set; }
        public int TopK { get; set; } = 5;
    }
}
