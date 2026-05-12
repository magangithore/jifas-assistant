using System;
using System.Collections.Generic;
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
        public string? UserId { get; set; }

        /// <summary>
        /// Session ID for conversation tracking
        /// Optional but recommended for context awareness
        /// Can be UUID, GUID, or any format (flexible for different implementations)
        /// </summary>
        [StringLength(256, ErrorMessage = "Session ID maksimal 256 karakter")]
        public string? SessionId { get; set; }

        /// <summary>
        /// Request correlation ID for audit trail (auto-generated)
        /// Used for tracking request through the system
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// User role (optional) - for role-based responses
        /// Examples: "FINA:KI", "ACCT:KI", "USER:RO", etc.
        /// Used to tailor responses based on user's department/role
        /// </summary>
        [StringLength(100, ErrorMessage = "User Role maksimal 100 karakter")]
        public string? UserRole { get; set; }

        /// <summary>
        /// Current module (optional) - context awareness
        /// Examples: "Invoice", "Payment", "PUM", "Receiving", "Accounting"
        /// Helps AI provide module-specific answers
        /// </summary>
        [StringLength(100, ErrorMessage = "Current Module maksimal 100 karakter")]
        public string? CurrentModule { get; set; }

        /// <summary>
        /// Company ID (optional) - for multi-company support
        /// Used if JIFAS supports multiple companies
        /// </summary>
        [StringLength(100, ErrorMessage = "Company ID maksimal 100 karakter")]
        public string? CompanyId { get; set; }

        /// <summary>
        /// Language preference (optional) - for localization
        /// Values: "id" (Indonesian), "en" (English)
        /// Default: "id"
        /// </summary>
        [StringLength(5, ErrorMessage = "Language maksimal 5 karakter")]
        public string Language { get; set; } = "id";

        /// <summary>
        /// Is this the first message in the session? (optional)
        /// Used to determine if we should show JIFAS introduction
        /// </summary>
        public bool IsFirstMessage { get; set; } = false;

        /// <summary>
        /// User company code (optional) - from HR/LDAP
        /// Example: "RC" (Receiving Center), "FIN" (Finance)
        /// </summary>
        [StringLength(50, ErrorMessage = "Company Code maksimal 50 karakter")]
        public string? UserCompCode { get; set; }

        /// <summary>
        /// User employee code (optional) - from HR/LDAP
        /// Example: "ERC2508001"
        /// Used for user identification and audit trail
        /// </summary>
        [StringLength(50, ErrorMessage = "Employee Code maksimal 50 karakter")]
        public string? UserEmpCode { get; set; }

        /// <summary>
        /// Context information (optional) - for context-aware responses
        /// Contains additional context like current page, selected document, etc.
        /// </summary>
        public RequestContext? Context { get; set; }

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

    /// <summary>
    /// Context informasi aktif dari halaman JIFAS yang sedang dibuka user
    /// Memungkinkan AI memberikan jawaban yang relevan dengan halaman yang sedang aktif
    /// </summary>
    public class RequestContext
    {
        /// <summary>
        /// URL/route halaman JIFAS yang sedang aktif
        /// Contoh: "/Invoice/Finance/Index", "/Payment/Monitor", "/GL/JournalEntry"
        /// AI akan memberikan respons yang kontekstual sesuai halaman ini
        /// </summary>
        public string? CurrentPage { get; set; }

        /// <summary>
        /// Nama modul JIFAS yang sedang aktif
        /// Contoh: "Invoice", "Payment", "GL", "PUM", "Receiving", "AR", "AP"
        /// </summary>
        public string? ActiveModule { get; set; }

        /// <summary>
        /// Judul/nama halaman yang sedang dibuka (human-readable)
        /// Contoh: "Finance Invoice List", "Journal Entry Form", "Payment Monitor"
        /// </summary>
        public string? PageTitle { get; set; }

        /// <summary>
        /// ID dokumen yang sedang dipilih/dibuka (opsional)
        /// Contoh: "INV-2024-001", "PMT-2024-050", "JE-2024-100"
        /// User bisa bertanya spesifik tentang dokumen ini
        /// </summary>
        public string? SelectedDocumentId { get; set; }

        /// <summary>
        /// Tipe dokumen yang sedang dibuka
        /// Contoh: "Invoice", "PaymentRequest", "JournalEntry", "PurchaseOrder"
        /// </summary>
        public string? DocumentType { get; set; }

        /// <summary>
        /// Status dokumen yang sedang aktif (opsional)
        /// Contoh: "Draft", "Submitted", "Approved", "Rejected", "Posted"
        /// </summary>
        public string? DocumentStatus { get; set; }

        /// <summary>
        /// Additional custom context data sebagai JSON object
        /// Untuk extensibility di masa mendatang
        /// </summary>
        public Dictionary<string, object>? CustomData { get; set; }
    }
}