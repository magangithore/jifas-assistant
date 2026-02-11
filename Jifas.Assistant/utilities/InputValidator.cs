using System;
using System.Collections.Generic;
using System.Linq;
using Jifas.Chatbot.Models;
using Jifas.Chatbot.Utilities;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Input validation service for API requests
    /// Validates and sanitizes all user inputs
    /// </summary>
    public interface IInputValidator
    {
        ValidationResult<ChatRequest> ValidateChatRequest(ChatRequest request);
        ValidationResult<string> ValidateMessage(string message);
        ValidationResult<string> ValidateQuery(string query);
        ValidationResult<List<string>> ValidateSuggestions(List<string> suggestions);
        ValidationResult<string> ValidateSessionId(string sessionId);
        ValidationResult<string> ValidateUserId(string userId);
        string SanitizeInput(string input);
    }

    /// <summary>
    /// Generic validation result wrapper
    /// </summary>
    public class ValidationResult<T>
    {
        public bool IsValid { get; set; }
        public T Value { get; set; }
        public string ErrorMessage { get; set; }

        public ValidationResult(bool isValid, T value, string errorMessage = null)
        {
            IsValid = isValid;
            Value = value;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Input validator implementation
    /// Comprehensive validation and sanitization
    /// </summary>
    public class InputValidator : IInputValidator
    {
        private readonly ILoggerService _logger;

        public InputValidator()
        {
            _logger = LoggerFactory.GetLogger();
        }

        public ValidationResult<ChatRequest> ValidateChatRequest(ChatRequest request)
        {
            try
            {
                if (request == null)
                {
                    _logger.LogWarning("[InputValidator] Chat request is null");
                    return new ValidationResult<ChatRequest>(false, null, "Request cannot be null");
                }

                // Validate message
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    _logger.LogWarning("[InputValidator] Empty chat message");
                    return new ValidationResult<ChatRequest>(false, null, "Message cannot be empty");
                }

                var messageResult = ValidateMessage(request.Message);
                if (!messageResult.IsValid)
                {
                    _logger.LogWarning("[InputValidator] Invalid message: {0}", messageResult.ErrorMessage);
                    return new ValidationResult<ChatRequest>(false, null, messageResult.ErrorMessage);
                }

                // Validate session ID if provided
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    var sessionResult = ValidateSessionId(request.SessionId);
                    if (!sessionResult.IsValid)
                    {
                        _logger.LogWarning("[InputValidator] Invalid session ID: {0}", sessionResult.ErrorMessage);
                        return new ValidationResult<ChatRequest>(false, null, sessionResult.ErrorMessage);
                    }
                }

                // Validate user ID if provided
                if (!string.IsNullOrWhiteSpace(request.UserId))
                {
                    var userResult = ValidateUserId(request.UserId);
                    if (!userResult.IsValid)
                    {
                        _logger.LogWarning("[InputValidator] Invalid user ID: {0}", userResult.ErrorMessage);
                        return new ValidationResult<ChatRequest>(false, null, userResult.ErrorMessage);
                    }
                }

                _logger.LogDebug("[InputValidator] Chat request validated successfully");
                return new ValidationResult<ChatRequest>(true, request);
            }
            catch (Exception ex)
            {
                _logger.LogError("[InputValidator] Error validating chat request", ex);
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

                // Check length
                if (trimmed.Length < ValidationConstants.MIN_MESSAGE_LENGTH)
                {
                    return new ValidationResult<string>(false, null, "Message is too short");
                }

                if (trimmed.Length > ValidationConstants.MAX_MESSAGE_LENGTH)
                {
                    return new ValidationResult<string>(false, null, $"Message exceeds maximum length of {ValidationConstants.MAX_MESSAGE_LENGTH} characters");
                }

                // Check for SQL injection
                if (ContainsSqlInjectionPattern(trimmed))
                {
                    _logger.LogWarning("[InputValidator] Potential SQL injection detected in message");
                    return new ValidationResult<string>(false, null, "Invalid message format");
                }

                // Check for XSS
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
                _logger.LogError("[InputValidator] Error validating message", ex);
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
                if (suggestions == null)
                {
                    return new ValidationResult<List<string>>(true, new List<string>());
                }

                var validSuggestions = new List<string>();

                foreach (var suggestion in suggestions)
                {
                    if (string.IsNullOrWhiteSpace(suggestion))
                        continue;

                    var trimmed = suggestion.Trim();

                    // Check length
                    if (trimmed.Length < ValidationConstants.MIN_SUGGESTION_LENGTH ||
                        trimmed.Length > ValidationConstants.MAX_SUGGESTION_LENGTH)
                    {
                        _logger.LogDebug("[InputValidator] Suggestion length out of range, skipping");
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

                // Limit to max suggestions
                if (validSuggestions.Count > ValidationConstants.MAX_SUGGESTIONS)
                {
                    validSuggestions = validSuggestions.Take(ValidationConstants.MAX_SUGGESTIONS).ToList();
                }

                return new ValidationResult<List<string>>(true, validSuggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError("[InputValidator] Error validating suggestions", ex);
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
                if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[a-zA-Z0-9\-]+$"))
                {
                    return new ValidationResult<string>(false, null, "Session ID contains invalid characters");
                }

                return new ValidationResult<string>(true, trimmed);
            }
            catch (Exception ex)
            {
                _logger.LogError("[InputValidator] Error validating session ID", ex);
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
                _logger.LogError("[InputValidator] Error validating user ID", ex);
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

                // Remove null characters
                sanitized = sanitized.Replace("\0", "");
                sanitized = sanitized.Replace("\x00", "");

                // Remove control characters (but keep common ones like newline)
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.LogError("[InputValidator] Error sanitizing input", ex);
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
