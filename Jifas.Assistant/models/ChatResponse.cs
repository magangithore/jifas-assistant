using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Response model for JIFAS AI Assistant
    /// Includes error handling, audit trail, and performance metrics
    /// </summary>
    public class ChatResponse
    {
        /// <summary>
        /// Name of the AI assistant
        /// </summary>
        public string Sender { get; set; } = "JIFAS AI Assistant";

        /// <summary>
        /// The AI response message
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Error messages if response generation failed
        /// Null/empty if successful
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Correlation ID from the request for audit trail
        /// Used to track requests through the system
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Source of the response (Knowledge Base, AI Generated, Out of Scope, etc.)
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Timestamp of the response
        /// </summary>
        public string Timestamp { get; set; }

        /// <summary>
        /// Indicates if the request was processed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Session ID for conversation tracking
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Indicates if the response was derived from Knowledge Base
        /// </summary>
        public bool IsFromKnowledgeBase { get; set; }

        /// <summary>
        /// Confidence score of the response (0-1)
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Follow-up question suggestions for user
        /// </summary>
        public List<string> Suggestions { get; set; } = new List<string>();

        /// <summary>
        /// Ticket information if a ticket was created from this response
        /// </summary>
        public TicketInfo Ticket { get; set; }

        /// <summary>
        /// Knowledge Base results used to generate this response
        /// </summary>
        public List<KnowledgeBaseResult> KnowledgeBaseResults { get; set; } = new List<KnowledgeBaseResult>();

        /// <summary>
        /// Performance metrics for this response (in milliseconds)
        /// </summary>
        [JsonProperty("performanceMetrics")]
        public PerformanceMetrics PerformanceMetrics { get; set; } = new PerformanceMetrics();
    }

    /// <summary>
    /// Performance metrics tracking for response time analysis
    /// All times in milliseconds (ms)
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Time taken for input validation (ms)
        /// </summary>
        public long InputValidationMs { get; set; }

        /// <summary>
        /// Time taken for cache lookup (ms)
        /// </summary>
        public long CacheLookupMs { get; set; }

        /// <summary>
        /// Time taken for scope detection (ms)
        /// </summary>
        public long ScopeDetectionMs { get; set; }

        /// <summary>
        /// Time taken for KB search (keyword + semantic in parallel) (ms)
        /// </summary>
        public long KbSearchMs { get; set; }

        /// <summary>
        /// Time taken for KB result validation (ms)
        /// </summary>
        public long ResultValidationMs { get; set; }

        /// <summary>
        /// Time taken for confidence calculation (ms)
        /// </summary>
        public long ConfidenceCalculationMs { get; set; }

        /// <summary>
        /// Time taken for LLM response generation (ms)
        /// </summary>
        public long LlmResponseMs { get; set; }

        /// <summary>
        /// Time taken for suggestions generation (ms)
        /// </summary>
        public long SuggestionsMs { get; set; }

        /// <summary>
        /// Time taken for response caching (ms)
        /// </summary>
        public long CachingMs { get; set; }

        /// <summary>
        /// Total end-to-end response time (ms)
        /// </summary>
        public long TotalMs { get; set; }

        /// <summary>
        /// Whether the response was served from cache (fast path)
        /// </summary>
        public bool WasCacheLit { get; set; }

        /// <summary>
        /// Whether suggestions were cached
        /// </summary>
        public bool SuggestionsCached { get; set; }

        /// <summary>
        /// Average KB search result score (0-1)
        /// </summary>
        public double AverageKbScore { get; set; }

        /// <summary>
        /// Number of KB results before validation
        /// </summary>
        public int KbResultsBeforeValidation { get; set; }

        /// <summary>
        /// Number of KB results after validation
        /// </summary>
        public int KbResultsAfterValidation { get; set; }

        /// <summary>
        /// Performance summary for logging
        /// </summary>
        public string GetSummary()
        {
            return $"[PERFORMANCE] Total: {TotalMs}ms | Validation: {InputValidationMs}ms | KB Search: {KbSearchMs}ms | LLM: {LlmResponseMs}ms | Suggestions: {SuggestionsMs}ms | Cache: {(WasCacheLit ? "HIT (fast)" : "MISS")}";
        }
    }

    /// <summary>
    /// Ticket information embedded in chat response
    /// </summary>
    public class TicketInfo
    {
        /// <summary>
        /// Generated ticket number
        /// </summary>
        public string TicketNumber { get; set; }

        /// <summary>
        /// Current status of the ticket
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Response message about the ticket
        /// </summary>
        public string Message { get; set; }
    }
}