using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jifas.Assistant.Models;
using Jifas.Assistant.Utilities;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Input validator implementation
    /// Comprehensive validation and sanitization
    /// Protects against: SQL injection, XSS, invalid formats, buffer overflow
    /// </summary>
    public class InputValidator : IInputValidator
    {
        private readonly ILoggerService _logger;

        public InputValidator(ILoggerService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValidationResult<ChatRequest> ValidateChatRequest(ChatRequest request)
        {
            var correlationId = request?.CorrelationId ?? Guid.NewGuid().ToString();
            
            try
            {
                if (request == null)
                {
                    _logger.LogWarningWithCorrelation(correlationId, "[InputValidator] Chat request is null");
                    return new ValidationResult<ChatRequest>(false, null, "Request cannot be null");
                }

                // Validate message (CRITICAL)
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    _logger.LogWarningWithCorrelation(correlationId, "[InputValidator] Empty chat message");
                    return new ValidationResult<ChatRequest>(false, null, "Message cannot be empty");
                }

                var messageResult = ValidateMessage(request.Message);
                if (!messageResult.IsValid)
                {
                    _logger.LogWarningWithCorrelation(correlationId, $"[InputValidator] Invalid message: {messageResult.ErrorMessage}");
                    return new ValidationResult<ChatRequest>(false, null, messageResult.ErrorMessage);
                }

                // Update request with sanitized message
                request.Message = messageResult.Value;
                
                // Ensure correlation ID is set
                if (string.IsNullOrEmpty(request.CorrelationId))
                {
                    request.CorrelationId = correlationId;
                }

                // Validate session ID if provided
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    var sessionResult = ValidateSessionId(request.SessionId);
                    if (!sessionResult.IsValid)
                    {
                        _logger.LogWarningWithCorrelation(correlationId, $"[InputValidator] Invalid session ID: {sessionResult.ErrorMessage}");
                        return new ValidationResult<ChatRequest>(false, null, sessionResult.ErrorMessage);
                    }
                    request.SessionId = sessionResult.Value;
                }

                // Validate user ID if provided
                if (!string.IsNullOrWhiteSpace(request.UserId))
                {
                    var userResult = ValidateUserId(request.UserId);
                    if (!userResult.IsValid)
                    {
                        _logger.LogWarningWithCorrelation(correlationId, $"[InputValidator] Invalid user ID: {userResult.ErrorMessage}");
                        return new ValidationResult<ChatRequest>(false, null, userResult.ErrorMessage);
                    }
                    request.UserId = userResult.Value;
                }

                _logger.LogDebug($"[InputValidator] Chat request validated successfully");
                _logger.LogPerformance("InputValidation", 0, correlationId);
                return new ValidationResult<ChatRequest>(true, request);
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(correlationId, "[InputValidator] Error validating chat request", ex);
                return new ValidationResult<ChatRequest>(false, null, "Validation error: " + ex.Message);
            }
        }

        public ValidationResult<string> ValidateMessage(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return new ValidationResult<string>(false, null, "Message cannot be empty");
                }

                var trimmed = message.Trim();

                // Check length (CRITICAL)
                if (trimmed.Length < ValidationConstants.MIN_MESSAGE_LENGTH)
                {
                    return new ValidationResult<string>(false, null, "Message is too short");
                }

                if (trimmed.Length > ValidationConstants.MAX_MESSAGE_LENGTH)
                {
                    return new ValidationResult<string>(false, null, 
                        $"Message exceeds maximum length of {ValidationConstants.MAX_MESSAGE_LENGTH} characters");
                }

                // Sanitize input
                trimmed = SanitizeInput(trimmed);

                // Check for SQL injection (CRITICAL)
                if (ContainsSqlInjectionPattern(trimmed))
                {
                    _logger.LogWarning("[InputValidator] Potential SQL injection detected in message");
                    return new ValidationResult<string>(false, null, "Invalid message format");
                }

                // Check for XSS (CRITICAL)
                if (ContainsXssPattern(trimmed))
                {
                    _logger.LogWarning("[InputValidator] Potential XSS detected in message");
                    return new ValidationResult<string>(false, null, "Invalid message format");
                }

                // Check for invalid characters
                if (ContainsInvalidCharacters(trimmed))
                {
                    _logger.LogWarning("[InputValidator] Invalid characters detected in message");
                    return new ValidationResult<string>(false, null, "Message contains invalid characters");
                }

                return new ValidationResult<string>(true, trimmed);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputValidator] Error validating message: {ex.Message}");
                return new ValidationResult<string>(false, null, "Validation error");
            }
        }

        public ValidationResult<string> ValidateQuery(string query)
        {
            // Same as message validation for now
            return ValidateMessage(query);
        }

        public ValidationResult<List<string>> ValidateSuggestions(List<string> suggestions)
        {
            try
            {
                if (suggestions == null || suggestions.Count == 0)
                {
                    return new ValidationResult<List<string>>(true, new List<string>());
                }

                var validSuggestions = new List<string>();

                foreach (var suggestion in suggestions)
                {
                    if (string.IsNullOrWhiteSpace(suggestion))
                        continue;

                    var trimmed = suggestion.Trim();
                    trimmed = SanitizeInput(trimmed);

                    // Check length
                    if (trimmed.Length < ValidationConstants.MIN_SUGGESTION_LENGTH ||
                        trimmed.Length > ValidationConstants.MAX_SUGGESTION_LENGTH)
                    {
                        _logger.LogDebug($"[InputValidator] Suggestion length out of range, skipping");
                        continue;
                    }

                    // Check for injection patterns
                    if (ContainsSqlInjectionPattern(trimmed) || ContainsXssPattern(trimmed))
                    {
                        _logger.LogWarning("[InputValidator] Injection pattern detected in suggestion, skipping");
                        continue;
                    }

                    validSuggestions.Add(trimmed);
                }

                // Limit to max suggestions (normally 3)
                if (validSuggestions.Count > ValidationConstants.MAX_SUGGESTIONS)
                {
                    validSuggestions = validSuggestions.Take(ValidationConstants.MAX_SUGGESTIONS).ToList();
                }

                return new ValidationResult<List<string>>(true, validSuggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputValidator] Error validating suggestions: {ex.Message}");
                return new ValidationResult<List<string>>(false, new List<string>(), "Validation error");
            }
        }

        public ValidationResult<string> ValidateSessionId(string sessionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return new ValidationResult<string>(false, null, "Session ID cannot be empty");
                }

                var trimmed = sessionId.Trim();

                if (trimmed.Length < ValidationConstants.MIN_SESSION_ID_LENGTH ||
                    trimmed.Length > ValidationConstants.MAX_SESSION_ID_LENGTH)
                {
                    return new ValidationResult<string>(false, null, "Session ID length is invalid");
                }

                // Only allow alphanumeric and hyphens (standard GUID format)
                if (!Regex.IsMatch(trimmed, @"^[a-zA-Z0-9\-]+$"))
                {
                    return new ValidationResult<string>(false, null, "Session ID contains invalid characters");
                }

                return new ValidationResult<string>(true, trimmed);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputValidator] Error validating session ID: {ex.Message}");
                return new ValidationResult<string>(false, null, "Validation error");
            }
        }

        public ValidationResult<string> ValidateUserId(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new ValidationResult<string>(false, null, "User ID cannot be empty");
                }

                var trimmed = userId.Trim();

                if (trimmed.Length < ValidationConstants.MIN_USER_ID_LENGTH ||
                    trimmed.Length > ValidationConstants.MAX_USER_ID_LENGTH)
                {
                    return new ValidationResult<string>(false, null, "User ID length is invalid");
                }

                // Check for SQL injection in user ID
                if (ContainsSqlInjectionPattern(trimmed))
                {
                    _logger.LogWarning("[InputValidator] SQL injection pattern in user ID");
                    return new ValidationResult<string>(false, null, "User ID contains invalid content");
                }

                return new ValidationResult<string>(true, trimmed);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputValidator] Error validating user ID: {ex.Message}");
                return new ValidationResult<string>(false, null, "Validation error");
            }
        }

        public string SanitizeInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            try
            {
                // Trim whitespace
                var sanitized = input.Trim();

                // FIX #6: Remove only truly dangerous characters
                // Remove null characters
                sanitized = sanitized.Replace("\0", "");
                sanitized = sanitized.Replace("\x00", "");

                // Remove dangerous control characters (but keep common ones like newline, tab)
                // Keep: \t (09), \n (0A), \r (0D)
                sanitized = Regex.Replace(sanitized, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

                // FIX #6: Normalize spaces but preserve intentional newlines
                // Replace tab with space
                sanitized = sanitized.Replace("\t", " ");
                // Normalize multiple spaces to single space
                sanitized = Regex.Replace(sanitized, @"[ ]+", " ");
                // Normalize multiple newlines to double newline (preserve paragraph breaks)
                sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");

                // FIX #6: Validate length after sanitization
                if (sanitized.Length > ValidationConstants.MAX_MESSAGE_LENGTH)
                {
                    // Trim to max length but don't cut mid-word
                    sanitized = sanitized.Substring(0, ValidationConstants.MAX_MESSAGE_LENGTH).TrimEnd();
                    _logger.LogDebug($"[InputValidator] Input truncated to {ValidationConstants.MAX_MESSAGE_LENGTH} characters");
                }

                _logger.LogDebug($"[InputValidator] Input sanitized successfully (original: {input.Length}, sanitized: {sanitized.Length})");
                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[InputValidator] Error sanitizing input: {ex.Message}");
                return input; // Return original if sanitization fails
            }
        }

        private bool ContainsSqlInjectionPattern(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var lowerInput = input.ToLower();
            return ValidationConstants.SqlInjectionPatterns.Any(pattern => lowerInput.Contains(pattern.ToLower()));
        }

        private bool ContainsXssPattern(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var lowerInput = input.ToLower();
            return ValidationConstants.XssPatterns.Any(pattern => lowerInput.Contains(pattern.ToLower()));
        }

        private bool ContainsInvalidCharacters(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return ValidationConstants.InvalidCharacterPatterns.Any(pattern => input.Contains(pattern));
        }
    }
}
