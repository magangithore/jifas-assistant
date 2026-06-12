using System.Collections.Generic;
using Jifas.Assistant.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Wrapper hasil validasi input.
    /// </summary>
    public class ValidationResult<T>
    {
        public bool IsValid { get; set; }
        public T Value { get; set; }
        public string? ErrorMessage { get; set; }

        public ValidationResult(bool isValid, T value, string? errorMessage = null)
        {
            IsValid = isValid;
            Value = value;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Service validasi input API.
    /// Semua pesan user harus lewat validator ini sebelum masuk cache, KB, atau LLM.
    /// </summary>
    public interface IInputValidator
    {
        ValidationResult<ChatRequest> ValidateChatRequest(ChatRequest? request);
        ValidationResult<string> ValidateMessage(string message);
        ValidationResult<string> ValidateQuery(string query);
        ValidationResult<List<string>> ValidateSuggestions(List<string> suggestions);
        ValidationResult<string> ValidateSessionId(string sessionId);
        ValidationResult<string> ValidateUserId(string userId);
        string SanitizeInput(string input);
    }
}
