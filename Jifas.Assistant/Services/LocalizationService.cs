using System.Collections.Generic;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Localization service for multi-language support
    /// Provides Indonesian and English messages
    /// </summary>
    public interface ILocalizationService
    {
        string GetMessage(string key, string language = "id");
        string GetErrorMessage(string errorKey, string language = "id");
    }

    public class LocalizationService : ILocalizationService
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
        {
            // Indonesian messages
            {
                "id", new Dictionary<string, string>
                {
                    // Help messages
                    { "help_general", "Saya adalah JIFAS AI Assistant. Saya dapat membantu Anda dengan pertanyaan tentang JIFAS Finance System. Tanya saja tentang Invoice, Payment, PUM, Receiving, atau Accounting!" },
                    { "help_invoice", "Untuk Invoice: Saya dapat membantu menjelaskan prosedur input invoice, approval workflow, status transitions, dan troubleshooting masalah invoice." },
                    { "help_payment", "Untuk Payment: Saya dapat membantu dengan prosedur pembayaran, approval rules, currency handling, dan FAQ seputar payment." },
                    { "help_pum", "Untuk PUM (Purchasing): Saya dapat membantu menjelaskan workflow pembelian, vendor management, dan purchase order procedures." },
                    { "help_receiving", "Untuk Receiving: Saya dapat membantu dengan prosedur penerimaan barang, 3-way matching, dan goods receipt processing." },
                    { "help_accounting", "Untuk Accounting: Saya dapat membantu dengan GL posting, cost allocation, period closing, dan accounting procedures." },

                    // Status messages
                    { "processing", "? Memproses pertanyaan Anda..." },
                    { "searching_kb", "?? Mencari informasi di Knowledge Base..." },
                    { "generating_response", "?? Menghasilkan jawaban..." },

                    // Success messages
                    { "success", "? Pertanyaan berhasil diproses" },
                    { "from_kb", "?? Jawaban dari Knowledge Base" },
                    { "generated_by_ai", "?? Jawaban dari AI" },

                    // Error messages
                    { "error_empty_message", "? Pesan tidak boleh kosong" },
                    { "error_message_too_long", "? Pesan terlalu panjang (maksimal 2000 karakter)" },
                    { "error_processing", "? Terjadi kesalahan saat memproses pertanyaan" },
                    { "error_timeout", "? Request timeout - coba lagi dalam beberapa saat" },
                    { "error_service_unavailable", "? Layanan AI tidak tersedia saat ini" },
                    { "error_invalid_request", "? Format request tidak valid" },

                    // Fallback messages
                    { "fallback_outofscope", "Maaf, pertanyaan Anda sepertinya di luar cakupan JIFAS Finance System. Mohon hubungi IT Help Desk untuk bantuan lebih lanjut." },
                    { "fallback_lowconfidence", "Saya tidak yakin dengan jawaban saya. Silakan hubungi IT Help Desk untuk informasi yang lebih akurat." },
                    { "fallback_kb_empty", "Knowledge Base belum memiliki informasi tentang topik ini. Silakan hubungi IT Help Desk." },

                    // Suggestion messages
                    { "suggest_contact_support", "Hubungi IT Help Desk untuk bantuan lebih lanjut" },
                    { "suggest_check_documentation", "Cek dokumentasi JIFAS Finance System" },
                    { "suggest_training", "Ikuti training JIFAS Finance System" },

                    // Role-based welcome
                    { "welcome_finance", "?? Selamat datang, Finance Officer! Saya siap membantu Anda dengan pertanyaan tentang Invoice, Payment, dan Accounting." },
                    { "welcome_user", "?? Selamat datang! Saya siap membantu Anda menggunakan JIFAS Finance System." },
                    { "welcome_accountant", "?? Selamat datang, Accountant! Saya siap membantu dengan GL posting dan period closing." },
                    { "welcome_procurement", "?? Selamat datang, Procurement Officer! Saya siap membantu dengan PUM dan Purchase Orders." },
                }
            },

            // English messages
            {
                "en", new Dictionary<string, string>
                {
                    // Help messages
                    { "help_general", "I'm JIFAS AI Assistant. I can help you with questions about JIFAS Finance System. Just ask about Invoice, Payment, PUM, Receiving, or Accounting!" },
                    { "help_invoice", "For Invoice: I can help explain input procedures, approval workflow, status transitions, and troubleshoot invoice issues." },
                    { "help_payment", "For Payment: I can help with payment procedures, approval rules, currency handling, and payment FAQs." },
                    { "help_pum", "For PUM (Purchasing): I can help explain purchasing workflow, vendor management, and purchase order procedures." },
                    { "help_receiving", "For Receiving: I can help with goods receipt procedures, 3-way matching, and goods receipt processing." },
                    { "help_accounting", "For Accounting: I can help with GL posting, cost allocation, period closing, and accounting procedures." },

                    // Status messages
                    { "processing", "? Processing your question..." },
                    { "searching_kb", "?? Searching Knowledge Base..." },
                    { "generating_response", "?? Generating response..." },

                    // Success messages
                    { "success", "? Question processed successfully" },
                    { "from_kb", "?? Answer from Knowledge Base" },
                    { "generated_by_ai", "?? Answer from AI" },

                    // Error messages
                    { "error_empty_message", "? Message cannot be empty" },
                    { "error_message_too_long", "? Message too long (max 2000 characters)" },
                    { "error_processing", "? Error processing your question" },
                    { "error_timeout", "? Request timeout - please try again" },
                    { "error_service_unavailable", "? AI service unavailable at the moment" },
                    { "error_invalid_request", "? Invalid request format" },

                    // Fallback messages
                    { "fallback_outofscope", "Sorry, your question seems to be outside the scope of JIFAS Finance System. Please contact IT Help Desk for further assistance." },
                    { "fallback_lowconfidence", "I'm not confident with my answer. Please contact IT Help Desk for accurate information." },
                    { "fallback_kb_empty", "Knowledge Base doesn't have information about this topic yet. Please contact IT Help Desk." },

                    // Suggestion messages
                    { "suggest_contact_support", "Contact IT Help Desk for further assistance" },
                    { "suggest_check_documentation", "Check JIFAS Finance System documentation" },
                    { "suggest_training", "Take JIFAS Finance System training" },

                    // Role-based welcome
                    { "welcome_finance", "?? Welcome, Finance Officer! I'm ready to help with Invoice, Payment, and Accounting questions." },
                    { "welcome_user", "?? Welcome! I'm ready to help you use JIFAS Finance System." },
                    { "welcome_accountant", "?? Welcome, Accountant! I'm ready to help with GL posting and period closing." },
                    { "welcome_procurement", "?? Welcome, Procurement Officer! I'm ready to help with PUM and Purchase Orders." },
                }
            }
        };

        public string GetMessage(string key, string language = "id")
        {
            // Default to Indonesian if language not supported
            if (string.IsNullOrEmpty(language) || !Messages.ContainsKey(language))
            {
                language = "id";
            }

            if (Messages[language].TryGetValue(key, out var message))
            {
                return message;
            }

            // Return key as fallback if message not found
            return key;
        }

        public string GetErrorMessage(string errorKey, string language = "id")
        {
            return GetMessage(errorKey, language);
        }
    }
}
