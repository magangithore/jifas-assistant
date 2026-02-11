using System.Collections.Generic;

namespace Jifas.Chatbot.Utilities
{
    /// <summary>
    /// Validation constants and limits for API inputs
    /// Centralized configuration for all validation rules
    /// </summary>
    public static class ValidationConstants
    {
        // Message validation
        public const int MIN_MESSAGE_LENGTH = 1;
        public const int MAX_MESSAGE_LENGTH = 500;
        
        // Session validation
        public const int MIN_SESSION_ID_LENGTH = 1;
        public const int MAX_SESSION_ID_LENGTH = 50;
        
        // User ID validation
        public const int MIN_USER_ID_LENGTH = 1;
        public const int MAX_USER_ID_LENGTH = 100;
        
        // Suggestion validation
        public const int MIN_SUGGESTION_LENGTH = 5;
        public const int MAX_SUGGESTION_LENGTH = 200;
        public const int MAX_SUGGESTIONS = 3;
        
        // Query validation
        public const int MIN_QUERY_LENGTH = 1;
        public const int MAX_QUERY_LENGTH = 500;
        
        // Search result validation
        public const int MAX_TOP_K_RESULTS = 10;
        public const int MIN_TOP_K_RESULTS = 1;
        
        // SQL Injection patterns to check
        public static readonly List<string> SqlInjectionPatterns = new List<string>
        {
            "'; DROP TABLE",
            "'; DELETE FROM",
            "UNION SELECT",
            "OR '1'='1",
            "OR 1=1",
            "' OR '1'='1",
            "--",
            "/*",
            "xp_",
            "exec(",
            "execute(",
            "script>",
            "javascript:",
            "onerror=",
            "onclick=",
            "onload="
        };
        
        // XSS patterns to check
        public static readonly List<string> XssPatterns = new List<string>
        {
            "<script",
            "</script>",
            "<iframe",
            "<img",
            "javascript:",
            "onerror=",
            "onclick=",
            "onload=",
            "onmouseover="
        };
        
        // Invalid characters that shouldn't appear in queries
        public static readonly List<string> InvalidCharacterPatterns = new List<string>
        {
            "\x00", // Null character
            "\x1a", // EOF character
        };
    }
}
