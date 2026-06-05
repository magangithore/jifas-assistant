using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    #region Models

    /// <summary>
    /// Result dari quality validation
    /// </summary>
    public class QualityValidationResult
    {
        public bool IsValid { get; set; }
        public double OverallScore { get; set; }
        public double GroundingScore { get; set; }
        public double CompletenessScore { get; set; }
        public double RelevanceScore { get; set; }
        public double ClarityScore { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public List<string> Suggestions { get; set; } = new List<string>();
        public bool ShouldRegenerate { get; set; }
        public string RegenerationReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Confidence calculation result
    /// </summary>
    public class ConfidenceResult
    {
        public double Threshold { get; set; }
        public double CalculatedConfidence { get; set; }
        public bool MeetsThreshold { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Dictionary<string, double> FactorBreakdown { get; set; } = new Dictionary<string, double>();
    }

    #endregion

    #region Interface

    /// <summary>
    /// Service untuk mengelola kualitas response AI
    /// Menggabungkan Response Quality Validation dan Adaptive Confidence
    /// </summary>
    public interface IResponseQualityService
    {
        // === Quality Validation ===
        
        /// <summary>
        /// Validate response quality
        /// </summary>
        Task<QualityValidationResult> ValidateResponseAsync(string userQuery, string response, List<KnowledgeBaseResult> kbSources);

        /// <summary>
        /// Check if response is grounded in KB
        /// </summary>
        double CalculateGroundingScore(string response, List<KnowledgeBaseResult> kbSources);

        /// <summary>
        /// Check if response is complete
        /// </summary>
        double CalculateCompletenessScore(string response, string userQuery);

        /// <summary>
        /// Check if response is relevant
        /// </summary>
        double CalculateRelevanceScore(string response, string userQuery);

        // === Adaptive Confidence ===

        /// <summary>
        /// Calculate adaptive threshold based on context
        /// </summary>
        Task<double> CalculateThresholdAsync(string query, IntentType intent, string? sessionId = null);

        /// <summary>
        /// Calculate confidence score with multiple factors
        /// </summary>
        ConfidenceResult CalculateConfidence(List<KnowledgeBaseResult> kbResults, string query, IntentType intent, double baseThreshold = 0.5);

        /// <summary>
        /// Determine if response should be generated
        /// </summary>
        bool ShouldGenerateResponse(ConfidenceResult confidence);
    }

    #endregion

    #region Implementation

    public class ResponseQualityService : IResponseQualityService
    {
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;

        #region Constants

        // Quality thresholds
        private const int MIN_RESPONSE_LENGTH = 50;
        private const int MIN_WORD_COUNT = 10;
        private const double MIN_GROUNDING_SCORE = 0.4;
        private const double MIN_OVERALL_SCORE = 0.5;

        // Confidence thresholds
        private const double BASE_THRESHOLD = 0.5;
        private const double MIN_THRESHOLD = 0.3;
        private const double MAX_THRESHOLD = 0.75;

        // Confidence weights
        private const double WEIGHT_AVG_SCORE = 0.35;
        private const double WEIGHT_MAX_SCORE = 0.25;
        private const double WEIGHT_DIVERSITY = 0.15;
        private const double WEIGHT_RESULT_COUNT = 0.10;
        private const double WEIGHT_KEYWORD_MATCH = 0.15;

        #endregion

        #region Static Data

        private static readonly List<string> HallucinationIndicators = new List<string>
        {
            "menurut sumber", "berdasarkan informasi umum", "pada umumnya",
            "kemungkinan besar", "saya pikir", "mungkin saja", "bisa jadi",
            "according to", "generally speaking", "i think", "perhaps"
        };

        private static readonly List<string> QualityIndicators = new List<string>
        {
            "langkah", "step", "pertama", "kedua", "ketiga",
            "menu", "klik", "pilih", "masuk ke", "buka",
            "jifas", "invoice", "payment", "budget", "pum",
            "approval", "submit", "save"
        };

        private static readonly string[] TemplatePatterns = new[]
        {
            // Opening templates (too formal/robotic)
            @"^Tentu,?\s+saya\s+akan",
            @"^Baik,?\s+saya\s+akan",
            @"^Saya\s+dengan\s+senang\s+hati",
            @"^Terima\s+kasih\s+atas\s+pertanyaan",
            @"^Pertanyaan\s+yang\s+bagus",
            @"^Ini\s+adalah\s+pertanyaan\s+yang",
            @"^Saya\s+mengerti\s+pertanyaan\s+Anda",
            
            // Closing templates (robotic endings)
            @"Apakah\s+ada\s+yang\s+lain\s+yang\s+bisa\s+saya\s+bantu\?$",
            @"Semoga\s+jawaban\s+ini\s+membantu\.$",
            @"Semoga\s+informasi\s+ini\s+bermanfaat\.$",
            @"Jangan\s+ragu\s+untuk\s+bertanya\s+lagi\.$",
            @"Silakan\s+hubungi\s+saya\s+jika\s+ada\s+pertanyaan\s+lain\.$",
            
            // Over-explanation patterns
            @"^Untuk\s+menjawab\s+pertanyaan\s+Anda",
            @"^Izinkan\s+saya\s+menjelaskan",
            @"^Mari\s+saya\s+jelaskan",
            @"^Sebelum\s+saya\s+menjawab",
            
            // Filler phrases
            @"perlu\s+dicatat\s+bahwa",
            @"penting\s+untuk\s+diingat",
            @"seperti\s+yang\s+telah\s+disebutkan"
        };

        #endregion

        public ResponseQualityService(ILoggerService logger, ICacheService cacheService)
        {
            _logger = logger;
            _cacheService = cacheService;
        }

        #region Quality Validation

        public async Task<QualityValidationResult> ValidateResponseAsync(string userQuery, string response, List<KnowledgeBaseResult> kbSources)
        {
            var result = new QualityValidationResult();

            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    result.IsValid = false;
                    result.Issues.Add("Response kosong");
                    result.ShouldRegenerate = true;
                    result.RegenerationReason = "Empty response";
                    return result;
                }

                // Calculate individual scores
                result.GroundingScore = CalculateGroundingScore(response, kbSources);
                result.CompletenessScore = CalculateCompletenessScore(response, userQuery);
                result.RelevanceScore = CalculateRelevanceScore(response, userQuery);
                result.ClarityScore = CalculateClarityScore(response);

                // Calculate overall score (weighted average)
                result.OverallScore =
                    (result.GroundingScore * 0.35) +
                    (result.RelevanceScore * 0.30) +
                    (result.CompletenessScore * 0.20) +
                    (result.ClarityScore * 0.15);

                // Identify issues
                if (result.GroundingScore < MIN_GROUNDING_SCORE)
                    result.Issues.Add($"Response mungkin mengandung informasi di luar KB (Grounding: {result.GroundingScore:P0})");

                if (result.CompletenessScore < 0.5)
                {
                    result.Issues.Add("Response terlalu pendek atau tidak lengkap");
                    result.Suggestions.Add("Tambahkan detail atau langkah-langkah yang lebih spesifik");
                }

                if (result.RelevanceScore < 0.5)
                    result.Issues.Add("Response kurang relevan dengan pertanyaan user");

                if (DetectTemplateResponse(response))
                {
                    result.Issues.Add("Response terdeteksi sebagai template/kaku");
                    result.Suggestions.Add("Buat response lebih natural dan kontekstual");
                }

                // Check hallucination
                var hallucinationCheck = CheckForHallucination(response);
                if (hallucinationCheck.hasIndicators)
                {
                    result.Issues.Add($"Potential hallucination: {hallucinationCheck.indicator}");
                    result.GroundingScore *= 0.7;
                }

                // Determine validity
                result.IsValid = result.OverallScore >= MIN_OVERALL_SCORE && result.Issues.Count <= 2;

                // Determine regeneration need
                if (result.OverallScore < 0.35 || result.GroundingScore < 0.3)
                {
                    result.ShouldRegenerate = true;
                    result.RegenerationReason = $"Low quality score: {result.OverallScore:P0}";
                }

                _logger.LogInformation($"[QualityService] Validation - Overall: {result.OverallScore:P0}, Grounding: {result.GroundingScore:P0}, Valid: {result.IsValid}");

                return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QualityService] Validation error: {ex.Message}");
                return new QualityValidationResult { IsValid = true, OverallScore = 0.6, Issues = new List<string> { "Validation error - skipped" } };
            }
        }

        public double CalculateGroundingScore(string response, List<KnowledgeBaseResult> kbSources)
        {
            if (string.IsNullOrWhiteSpace(response)) return 0;

            var rLower = response.ToLower();

            // FIXED: If no KB sources, grounding is ZERO - no faking confidence
            // This prevents hallucination when AI has no knowledge base data
            if (kbSources == null || kbSources.Count == 0)
            {
                _logger.LogWarning("[QualityService] No KB sources - grounding score is 0.0 (was 0.3-0.7). Hallucination risk is HIGH.");
                return 0.0;
            }

            var responseWords = ExtractSignificantWords(rLower);
            var kbContent = string.Join(" ", kbSources.Select(s => s.Content.ToLower()));
            var kbWords = ExtractSignificantWords(kbContent);

            var matchedWords = responseWords.Intersect(kbWords).Count();
            var totalResponseWords = responseWords.Count;

            if (totalResponseWords == 0) return 0.3;

            var rawScore = (double)matchedWords / totalResponseWords;

            // Bonus for quality indicators from KB
            var qualityBonus = 0.0;
            foreach (var indicator in QualityIndicators)
            {
                if (rLower.Contains(indicator) && kbContent.Contains(indicator))
                    qualityBonus += 0.05;
            }

            return Math.Min(rawScore + qualityBonus, 1.0);
        }

        public double CalculateCompletenessScore(string response, string userQuery)
        {
            if (string.IsNullOrWhiteSpace(response)) return 0;

            var score = 0.0;

            // Length check
            if (response.Length >= MIN_RESPONSE_LENGTH) score += 0.2;
            if (response.Length >= 150) score += 0.1;
            if (response.Length >= 300) score += 0.1;

            // Word count check
            var wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= MIN_WORD_COUNT) score += 0.2;
            if (wordCount >= 30) score += 0.1;

            // Structure check
            if (Regex.IsMatch(response, @"\d+\.\s+\w+")) score += 0.15; // Numbered list
            if (response.Contains("-") || response.Contains("•")) score += 0.05; // Bullets

            // Paragraph check
            var paragraphCount = response.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (paragraphCount >= 2) score += 0.1;

            return Math.Min(score, 1.0);
        }

        public double CalculateRelevanceScore(string response, string userQuery)
        {
            if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(userQuery)) return 0;

            var queryWords = ExtractSignificantWords(userQuery.ToLower());
            var responseWords = ExtractSignificantWords(response.ToLower());

            if (queryWords.Count == 0) return 0.5;

            var matchedKeywords = queryWords.Intersect(responseWords).Count();
            var keywordCoverage = (double)matchedKeywords / queryWords.Count;

            // Question-answer alignment bonus
            var alignmentBonus = 0.0;

            if (Regex.IsMatch(userQuery.ToLower(), @"(cara|bagaimana|langkah)"))
            {
                if (Regex.IsMatch(response, @"\d+\.\s+")) alignmentBonus += 0.15;
            }

            if (Regex.IsMatch(userQuery.ToLower(), @"^apa\s+(itu|yang|saja)"))
            {
                if (Regex.IsMatch(response.ToLower(), @"(adalah|merupakan|yaitu)")) alignmentBonus += 0.15;
            }

            return Math.Min(keywordCoverage + alignmentBonus, 1.0);
        }

        private double CalculateClarityScore(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return 0;

            var score = 0.5;

            // Sentence length check
            var sentences = response.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            if (sentences.Length > 0)
            {
                var avgSentenceLength = sentences.Average(s => s.Split(' ').Length);
                if (avgSentenceLength <= 20) score += 0.2;
                else if (avgSentenceLength > 35) score -= 0.1;
            }

            // Structure bonus
            if (response.Contains(":\n") || response.Contains(":\r\n")) score += 0.1;

            // Jargon penalty
            var jargonCount = Regex.Matches(response, @"\b[A-Z]{2,5}\b").Count;
            if (jargonCount > 5) score -= 0.1;

            // Indonesian indicators
            var indonesianIndicators = new[] { "adalah", "untuk", "dengan", "dalam", "dapat" };
            if (indonesianIndicators.Count(i => response.ToLower().Contains(i)) >= 2) score += 0.1;

            return Math.Max(0, Math.Min(score, 1.0));
        }

        private (bool hasIndicators, string indicator) CheckForHallucination(string response)
        {
            var responseLower = response.ToLower();
            foreach (var indicator in HallucinationIndicators)
            {
                if (responseLower.Contains(indicator))
                    return (true, indicator);
            }
            return (false, string.Empty);
        }

        private bool DetectTemplateResponse(string response)
        {
            foreach (var pattern in TemplatePatterns)
            {
                if (Regex.IsMatch(response, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        private HashSet<string> ExtractSignificantWords(string text)
        {
            var stopwords = new HashSet<string>
            {
                "yang", "dan", "atau", "untuk", "dari", "ke", "di", "ini", "itu",
                "adalah", "dengan", "pada", "dalam", "akan", "bisa", "dapat", "sudah",
                "the", "a", "an", "is", "are", "was", "were", "be", "been",
                "saya", "anda", "kita", "kami", "mereka", "nya"
            };

            return Regex.Matches(text, @"\b[a-zA-Z]{3,}\b")
                .Cast<Match>()
                .Select(m => m.Value.ToLower())
                .Where(w => !stopwords.Contains(w))
                .ToHashSet();
        }

        #endregion

        #region Adaptive Confidence

        public async Task<double> CalculateThresholdAsync(string query, IntentType intent, string? sessionId = null)
        {
            var threshold = BASE_THRESHOLD;

            try
            {
                // Adjustment 1: Intent-based
                threshold += GetIntentAdjustment(intent);

                // Adjustment 2: Query complexity
                threshold += GetComplexityAdjustment(query);

                // Clamp to valid range
                threshold = Math.Max(MIN_THRESHOLD, Math.Min(MAX_THRESHOLD, threshold));

                _logger.LogDebug($"[QualityService] Threshold: {threshold:F2} (Intent: {intent})");

                return await Task.FromResult(threshold);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QualityService] Threshold error: {ex.Message}");
                return BASE_THRESHOLD;
            }
        }

        public ConfidenceResult CalculateConfidence(List<KnowledgeBaseResult> kbResults, string query, IntentType intent, double baseThreshold = 0.5)
        {
            var result = new ConfidenceResult { Threshold = baseThreshold };

            try
            {
                if (kbResults == null || kbResults.Count == 0)
                {
                    // No KB results, but AI has system knowledge - give a moderate base confidence
                    // so the system still generates a response from its domain knowledge
                    result.CalculatedConfidence = 0.35;
                    result.MeetsThreshold = false; // Will use fallback path but still calls AI
                    result.Reason = "No KB results - using system knowledge";
                    return result;
                }

                var topResults = kbResults.Take(5).ToList();

                // Factor 1: Average score
                var avgScore = topResults.Average(r => r.Score);
                result.FactorBreakdown["AvgScore"] = avgScore;

                // Factor 2: Max score
                var maxScore = topResults.Max(r => r.Score);
                result.FactorBreakdown["MaxScore"] = maxScore;

                // Factor 3: Diversity
                var uniqueDocs = topResults.Select(r => r.DocumentId).Distinct().Count();
                var diversityScore = Math.Min(uniqueDocs / 3.0, 1.0);
                result.FactorBreakdown["Diversity"] = diversityScore;

                // Factor 4: Result count
                var resultCountScore = Math.Min(kbResults.Count / 5.0, 1.0);
                result.FactorBreakdown["ResultCount"] = resultCountScore;

                // Factor 5: Keyword match
                var keywordScore = CalculateKeywordMatchScore(query, topResults);
                result.FactorBreakdown["KeywordMatch"] = keywordScore;

                // Calculate weighted confidence
                var confidence =
                    (avgScore * WEIGHT_AVG_SCORE) +
                    (maxScore * WEIGHT_MAX_SCORE) +
                    (diversityScore * WEIGHT_DIVERSITY) +
                    (resultCountScore * WEIGHT_RESULT_COUNT) +
                    (keywordScore * WEIGHT_KEYWORD_MATCH);

                // Apply intent modifier
                confidence = ApplyIntentModifier(confidence, intent, maxScore);

                // High-quality match bonus
                if (maxScore >= 0.85 && keywordScore >= 0.7)
                {
                    confidence = Math.Min(confidence + 0.1, 1.0);
                    result.Reason = "High-quality match";
                }

                // No strong match penalty
                if (maxScore < 0.6 && confidence > 0.6)
                {
                    confidence *= 0.85;
                    result.Reason = "Confidence reduced - no strong match";
                }

                result.CalculatedConfidence = Math.Min(confidence, 1.0);
                result.MeetsThreshold = result.CalculatedConfidence >= baseThreshold;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QualityService] Confidence error: {ex.Message}");
                result.CalculatedConfidence = 0.4;
                result.MeetsThreshold = false;
                return result;
            }
        }

        public bool ShouldGenerateResponse(ConfidenceResult confidence)
        {
            if (confidence.MeetsThreshold) return true;

            // Allow partial response if close to threshold with good quality
            if (confidence.CalculatedConfidence >= confidence.Threshold * 0.8)
            {
                if (confidence.FactorBreakdown.TryGetValue("MaxScore", out var maxScore) && maxScore >= 0.7)
                    return true;
            }

            return false;
        }

        private double GetIntentAdjustment(IntentType intent)
        {
            return intent switch
            {
                IntentType.HowTo => -0.05,
                IntentType.Troubleshooting => -0.05,
                IntentType.Explanation => 0,
                IntentType.General => 0,
                IntentType.Configuration => 0.05,
                IntentType.Navigation => 0,
                IntentType.Greeting => -0.3,
                IntentType.Gratitude => -0.3,
                IntentType.OutOfScope => 0.2,
                _ => 0
            };
        }

        private double GetComplexityAdjustment(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return 0;

            var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            if (wordCount <= 3) return -0.05;
            if (wordCount > 15) return 0.05;

            return 0;
        }

        private double CalculateKeywordMatchScore(string query, List<KnowledgeBaseResult> results)
        {
            if (string.IsNullOrWhiteSpace(query) || results == null || results.Count == 0) return 0;

            var queryWords = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .Distinct()
                .ToList();

            if (queryWords.Count == 0) return 0.5;

            var combinedContent = string.Join(" ", results.Select(r => r.Content.ToLower()));
            var matchedWords = queryWords.Count(w => combinedContent.Contains(w));

            return (double)matchedWords / queryWords.Count;
        }

        private double ApplyIntentModifier(double confidence, IntentType intent, double maxScore)
        {
            switch (intent)
            {
                case IntentType.HowTo:
                    if (maxScore >= 0.7) return confidence + 0.05;
                    break;
                case IntentType.Troubleshooting:
                    if (maxScore >= 0.5) return confidence + 0.05;
                    break;
                case IntentType.Explanation:
                    if (maxScore < 0.6) return confidence - 0.05;
                    break;
            }
            return confidence;
        }

        #endregion
    }

    #endregion
}
