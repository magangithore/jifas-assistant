using System;
using System.ComponentModel.DataAnnotations;

namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Request model for JIFAS AI Assistant
    /// Comprehensive validation for chat interactions
    /// </summary>
    public class ChatRequest
    {
        /// <summary>
        /// The user's message/question
        /// Must be 2-2000 characters for meaningful queries
        /// </summary>
        [Required(ErrorMessage = "Pesan harus diisi")]
        [StringLength(2000, MinimumLength = 2, 
            ErrorMessage = "Pesan harus antara 2 dan 2000 karakter")]
        public string Message { get; set; }

        /// <summary>
        /// User identifier (Windows AD username or custom ID)
        /// Optional but recommended for audit trail
        /// Can be any format (AD, LDAP, custom, etc)
        /// </summary>
        [StringLength(256, ErrorMessage = "User ID maksimal 256 karakter")]
        public string UserId { get; set; }

        /// <summary>
        /// Session ID for conversation tracking
        /// Optional but recommended for context awareness
        /// Can be UUID, GUID, or any format (flexible for different implementations)
        /// </summary>
        [StringLength(256, ErrorMessage = "Session ID maksimal 256 karakter")]
        public string SessionId { get; set; }

        /// <summary>
        /// Request correlation ID for audit trail (auto-generated)
        /// Used for tracking request through the system
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Initialize request with auto-generated correlation ID if not provided
        /// </summary>
        public ChatRequest()
        {
            if (string.IsNullOrEmpty(CorrelationId))
            {
                CorrelationId = Guid.NewGuid().ToString();
            }
        }
    }
}