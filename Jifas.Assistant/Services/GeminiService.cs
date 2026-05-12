using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Ollama AI Service - menggunakan Ollama API untuk generasi respons
    /// Model dikonfigurasi via Ollama:Model di appsettings.json
    /// </summary>
    public class OllamaAIService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly IPromptEngineeringService _promptEngineering;
        private readonly IKnowledgeBaseSearchService _kbSearch;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly float _temperature;
        private readonly int _maxOutputTokens;

        private const string OLLAMA_CHAT_ENDPOINT = "/api/chat";

        public OllamaAIService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILoggerService logger,
            IPromptEngineeringService promptEngineering,
            IKnowledgeBaseSearchService kbSearch)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _promptEngineering = promptEngineering ?? throw new ArgumentNullException(nameof(promptEngineering));
            _kbSearch = kbSearch ?? throw new ArgumentNullException(nameof(kbSearch));

            _apiKey = _configuration["Ollama:ApiKey"] ?? string.Empty;
            _model = _configuration["Ollama:Model"] ?? "qwen3:8b";
            _baseUrl = _configuration["Ollama:BaseUrl"] ?? "http://10.0.12.54:11434";
            _temperature = _configuration.GetValue<float>("Ollama:Temperature", 0.3f);
            _maxOutputTokens = _configuration.GetValue<int>("Ollama:MaxTokens", 2048);

            var timeout = _configuration.GetValue<int>("Ollama:TimeoutSeconds", 120);
            _httpClient.Timeout = TimeSpan.FromSeconds(timeout);

            _logger.LogInformation("[OllamaAIService] Initialized with model: {0}", _model);
        }

        /// <summary>
        /// Generate response menggunakan Ollama dengan knowledge base context
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string? sessionContext = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";

                _logger.LogInformation("[OllamaAIService] Processing query: {0}", userQuery);

                if (kbResults == null || kbResults.Count == 0)
                {
                    _logger.LogWarning("[OllamaAIService] No KB results for query: {0}", userQuery);
                    return BuildNoResultsMessage(userQuery);
                }

                _logger.LogInformation("[OllamaAIService] Found {0} KB results (relevance: {1:P0}), context: {2}",
                    kbResults.Count, kbResults.Max(r => r.Score), sessionContext ?? "(none)");

                var intelligentPrompt = await _promptEngineering.BuildIntelligentPromptAsync(
                    userQuery, kbResults, sessionContext: sessionContext);

                var response = await CallOllamaApiAsync(intelligentPrompt);

                if (string.IsNullOrEmpty(response))
                    return "Maaf, terjadi kesalahan dalam memproses jawaban. Silakan coba lagi.";

                _logger.LogInformation("[OllamaAIService] Generated response: {0} chars", response.Length);
                return response;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError("[OllamaAIService] HTTP error calling Ollama API: {0}", httpEx, new object[] { httpEx.Message });
                return "Maaf, layanan AI saat ini tidak tersedia. Silakan coba lagi nanti.";
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error generating response: {0}", ex, new object[] { ex.Message });
                return "Maaf, terjadi kesalahan dalam memproses permintaan Anda.";
            }
        }

        /// <summary>
        /// Generate 3 follow-up suggestions yang relevan berdasarkan konteks percakapan.
        /// Menggunakan prompt minimal agar hemat token namun tetap cerdas dan kontekstual.
        /// </summary>
        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                    return GetDefaultSuggestions();

                // Ambil ringkasan respons (maks 400 char) agar prompt hemat token
                var responseSummary = response.Length > 400
                    ? response.Substring(0, 400) + "..."
                    : response;

                var suggestionsPrompt = $@"Konteks: pengguna bertanya tentang sistem JIFAS (ERP keuangan PT Jababeka).

Pertanyaan: {userQuery}
Jawaban AI: {responseSummary}

Buat 3 pertanyaan lanjutan yang NATURAL dan BERGUNA bagi pengguna.
Syarat:
- Relevan dengan topik di atas (bukan pertanyaan umum)
- Spesifik tentang fitur/proses JIFAS
- Singkat (maks 12 kata)
- Bahasa Indonesia

Tulis HANYA 3 pertanyaan, format:
1. [pertanyaan]
2. [pertanyaan]
3. [pertanyaan]";

                var suggestionsText = await CallOllamaApiAsync(suggestionsPrompt);
                var parsed = ExtractSuggestions(suggestionsText);
                return parsed.Count > 0 ? parsed : GetDefaultSuggestions();
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error generating suggestions: {0}", ex, new object[] { ex.Message });
                return GetDefaultSuggestions();
            }
        }

        /// <summary>
        /// Scope check tanpa LLM call - sudah dihandle OutOfScopeDetector via keyword matching.
        /// Method ini dipertahankan untuk kompatibilitas interface.
        /// </summary>
        public Task<bool> IsInScopeAsync(string userQuery)
        {
            // Keyword-based check tanpa menggunakan AI API
            var outOfScope = new[] { "cuaca", "berita", "politik", "film", "resep", "crypto", "bitcoin", "agama" };
            var query = userQuery?.ToLowerInvariant() ?? "";
            var isOut = outOfScope.Any(k => query.Contains(k));
            return Task.FromResult(!isOut);
        }

        /// <summary>
        /// Memanggil Ollama API dengan retry on error
        /// </summary>
        public async Task<string> CallOllamaApiAsync(string prompt)
        {
            const int maxRetries = 2;
            var retryDelaysMs = new[] { 3000, 8000 };

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallOllamaApiInternalAsync(prompt);
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("TooManyRequests") && attempt < maxRetries)
                {
                    var delayMs = retryDelaysMs[attempt];
                    _logger.LogWarning("[OllamaAIService] Rate limited (429), retry {0}/{1} in {2}ms...", (attempt + 1), maxRetries, delayMs);
                    await Task.Delay(delayMs);
                }
            }

            throw new HttpRequestException("Ollama API rate limit exceeded after retries");
        }

        private async Task<string> CallOllamaApiInternalAsync(string prompt)
        {
            try
            {
                var endpoint = $"{_baseUrl}{OLLAMA_CHAT_ENDPOINT}";

                // Ollama /api/chat request body
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = BuildJifasSystemInstruction() },
                        new { role = "user", content = prompt }
                    },
                    stream = false,
                    options = new
                    {
                        temperature = _temperature,
                        top_p = 0.85,
                        top_k = 40,
                        num_predict = _maxOutputTokens
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody, Formatting.None);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogDebug("[OllamaAIService] Calling Ollama endpoint: {0}", endpoint);

                var response = await _httpClient.PostAsync(endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[OllamaAIService] API error {response.StatusCode}: {errorBody}");
                    throw new HttpRequestException($"Ollama API returned {response.StatusCode}: {errorBody}");
                }

                var responseText = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("[OllamaAIService] Response received, parsing...");

                return ParseOllamaResponse(responseText);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error calling Ollama API: {0}", ex, new object[] { ex.Message });
                throw;
            }
        } // end CallOllamaApiInternalAsync

        /// <summary>
        /// Parse response dari Ollama API format JSON.
        /// Format: { "message": { "role": "assistant", "content": "..." } }
        /// </summary>
        private string ParseOllamaResponse(string responseJson)
        {
            try
            {
                var json = JObject.Parse(responseJson);

                // Ollama /api/chat response: message.content
                var text = json["message"]?["content"]?.ToString();

                if (!string.IsNullOrEmpty(text))
                    return text.Trim();

                // Fallback: cek field "response" (Ollama /api/generate format)
                var fallback = json["response"]?.ToString();
                if (!string.IsNullOrEmpty(fallback))
                    return fallback.Trim();

                _logger.LogWarning("[OllamaAIService] Could not extract text from response: {0}", responseJson.Substring(0, Math.Min(200, responseJson.Length)));
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error parsing Ollama response: {0}", ex, new object[] { ex.Message });
                return string.Empty;
            }
        }

        /// <summary>
        /// System instruction mendalam untuk JIFAS AI Persona Agent
        /// Dibangun dari Knowledge Base nyata JIFAS PT Jababeka Tbk
        /// </summary>
        private string BuildJifasSystemInstruction()
        {
            return @"Kamu adalah JIFAS AI Assistant â€” AI Persona Agent resmi untuk sistem JIFAS (Jababeka Integrated Finance Accounting System) milik PT Jababeka Tbk dan seluruh anak perusahaannya.

=== IDENTITAS & PERSONA ===
Namamu: JIFAS AI
Peranmu: Expert JIFAS System Advisor & Business Process Consultant
Karaktermu: Cerdas, profesional, jujur, helpful, dan bicara seperti rekan kerja senior yang paham sistem â€” bukan robot kaku.
Bahasa: Bahasa Indonesia yang natural, hangat, dan mudah dipahami. Boleh sesekali memakai istilah teknis JIFAS bila relevan.

=== PENGETAHUAN MENDALAM TENTANG JIFAS ===

JIFAS adalah sistem ERP keuangan terintegrasi yang dipakai oleh semua unit bisnis Jababeka Group:
- KIJ (Kawasan Industri Jababeka), GBC, MPK, JM, BW, TL, SPPK â†’ URL: jifas.jababeka.com
- JI, ICTEL, NGE â†’ URL: jifasweb.jiinfra.com
- BP, UP, TS â†’ URL: jifas-bp.bekasipower.co.id
- KIK â†’ URL: jifas.kik.com
Login: gunakan username Windows TANPA @jababeka.com

MODUL-MODUL UTAMA JIFAS:

1. MASTER DATA (Pengaturan Dasar)
   - Company: profil perusahaan, cabang, divisi
   - Employee: data karyawan (dipakai di PUM/payroll)
   - Vendor: supplier dan rekanan bisnis
   - Division / Department: struktur organisasi
   - COA (Chart of Accounts): kode akun keuangan
   - Account Period: periode akuntansi (buka/tutup bulan)
   - List COA: daftar CoA yang aktif
   - Report Setup: konfigurasi laporan keuangan
   - Roles & Authorization: hak akses user (WMTR=IT, USER=umum, USRL=bisa pilih dept di PUM)
   - Budget: input dan kelola anggaran

2. INVOICE (Pengajuan Pembayaran)
   - Finance Invoice: pengajuan tagihan/biaya ke bagian keuangan
   - Alur: Create â†’ Finance Approval â†’ Head Approval â†’ Tax â†’ Posting
   - Status: Draft â†’ Submitted â†’ Finance Checking â†’ Head Approval â†’ Approved â†’ Posted
   - Tombol utama: Save, Submit, Approve, Reject, Post, Void
   - Yang bisa edit: hanya saat status Draft atau dikembalikan

3. PUM (Perjalanan Uang Muka / Perjalanan Dinas)
   - Pengajuan uang muka untuk kebutuhan operasional/perjalanan dinas
   - Alur: Pengajuan â†’ Finance Approval â†’ Head Approval â†’ Realisasi â†’ Settlement
   - Settlement: karyawan melaporkan realisasi pengeluaran vs uang muka
   - Jika realisasi < uang muka: karyawan kembalikan sisa
   - Jika realisasi > uang muka: perusahaan bayar kekurangan
   - Status PUM: Draft â†’ Submitted â†’ Approved â†’ Distributed â†’ Realization â†’ Settled
   - Role USRL: bisa pilih department/divisi saat buat PUM

4. RECEIVING (Penerimaan Barang/Jasa)
   - Receive of Sales: penerimaan dari transaksi penjualan
   - Receive Tax (Tax Approval): penerimaan dengan aspek perpajakan
   - ReceiveTax: verifikasi NPWP, alamat WP harus lengkap sebelum approve
   - Reject jika tax rate salah â€” buat dokumen baru
   - Status: Need Finance Checking â†’ Approved â†’ Posted
   - RV (Receive Voucher): nomor dokumen penerimaan

5. PAYMENT (Pembayaran)
   - Payment Invoice: pembayaran atas invoice yang sudah disetujui
   - Payment PUM: pembayaran/distribusi uang muka PUM
   - Finance Approval â†’ Head Approval â†’ Posting
   - Metode pembayaran: Transfer, BG (Bank Garansi), Cek, Giro
   - List BG: daftar Bank Garansi yang tersedia
   - PaymentTax: pembayaran yang melibatkan pajak

6. ACCOUNTING (Jurnal & Laporan Keuangan)
   - GL (General Ledger): buku besar, jurnal manual
   - AP (Account Payable): hutang ke vendor/supplier
   - AR (Account Receivable): piutang dari pelanggan
   - Posting: proses merekam transaksi ke buku besar
   - Inquiry AP/AR/CB/PUM: lihat saldo dan detail transaksi
   - Bulk Posting: posting banyak dokumen sekaligus
   - Konsolidasi: gabung laporan multi-cabang
   - Acc Period: buka/tutup periode akuntansi

7. REPORT (Laporan Keuangan)
   - Budget Card: kartu anggaran per cost center
   - Budget Committed: komitmen anggaran yang belum terealisasi
   - Budget Payment: realisasi pembayaran vs anggaran
   - Budget Realization: laporan realisasi anggaran
   - Budget Receive: penerimaan terkait anggaran
   - Cashbank Detail / Recap: laporan kas dan bank
   - Daily Cashflow: arus kas harian
   - Deposito Aktif: daftar deposito yang aktif
   - Inquiry AP/AR/CB/PUM: inquiry saldo piutang/hutang/kas
   - Realisasi PUM: laporan realisasi uang muka
   - Saldo Buku Bank: rekonsiliasi buku vs bank
   - Commited Realization: laporan komitmen vs realisasi

8. OVER BUDGET
   - Terjadi ketika transaksi melebihi batas anggaran
   - Budget Status: Remaining, Committed, Actual
   - Langkah: minta approval khusus budget atau revisi budget

ALUR APPROVAL UMUM DI JIFAS:
Creator (buat & submit) â†’ Finance (verifikasi keuangan) â†’ Head/Director (approval final) â†’ Tax (jika ada pajak) â†’ Posting (rekam ke GL)

ATURAN PENTING JIFAS:
- Dokumen yang sudah di-posting TIDAK bisa diedit
- Periode yang sudah ditutup TIDAK bisa diinput transaksi
- Reject = dokumen dikembalikan ke pembuat untuk diperbaiki
- Void = pembatalan dokumen yang sudah final
- Semua user wajib login dengan username Windows (tanpa @jababeka.com)
- Password = password Windows domain

=== CARA KAMU MENJAWAB (ATURAN WAJIB) ===

1. GROUNDED IN KB: Jawab berdasarkan informasi dari Knowledge Base yang diberikan. Ini adalah sumber kebenaran utama.
2. HONEST IF UNKNOWN: Jika KB tidak punya informasinya, katakan: ""Informasi spesifik ini belum tersedia di Knowledge Base JIFAS. Coba hubungi IT Help Desk: it@jababeka.com""
3. NO HALLUCINATION: Jangan mengarang langkah, nomor menu, atau fitur yang tidak ada di KB.
4. CONTEXT AWARE: Jika user sedang di halaman/modul tertentu, prioritaskan jawaban yang relevan dengan konteks itu.
5. ACTIONABLE: Berikan langkah-langkah konkret, bukan jawaban abstrak.
6. NATURAL: Bicara seperti rekan senior yang paham sistem, bukan seperti baca manual book.
7. STRUCTURED: Gunakan bullet atau numbering untuk langkah-langkah. Gunakan bold untuk istilah penting.
8. CONCISE: Jawab yang ditanya, tidak perlu preamble panjang atau kesimpulan berulang.

=== ESKALASI ===
Jika masalah tidak bisa diselesaikan via KB:
- IT Help Desk: it@jababeka.com
- Untuk akses/permission: minta ke admin sistem atau atasan langsung

Kamu adalah wajah digital JIFAS â€” bantu user dengan penuh keyakinan dan keakuratan.";
        }

        private string BuildNoResultsMessage(string query) =>
            $"Maaf, saya tidak menemukan informasi tentang '{query}' di Knowledge Base JIFAS. " +
            "Silakan coba dengan kata kunci berbeda, atau hubungi Tim IT Help Desk di it@jababeka.com untuk bantuan lebih lanjut.";

        private List<string> ExtractSuggestions(string responseText)
        {
            var suggestions = new List<string>();
            if (string.IsNullOrEmpty(responseText)) return GetDefaultSuggestions();

            try
            {
                var lines = responseText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line.Trim(), @"^\d+[.)]\s*(.+)$");
                    if (match.Success)
                    {
                        var suggestion = match.Groups[1].Value.Trim();
                        if (suggestion.Length >= 5 && suggestion.Length <= 200)
                            suggestions.Add(suggestion);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[OllamaAIService] Error parsing suggestions: {0}", ex.Message);
            }

            return suggestions.Count > 0 ? suggestions.Take(3).ToList() : GetDefaultSuggestions();
        }

        private static List<string> GetDefaultSuggestions() => new List<string>
        {
            "Bagaimana cara membuat dokumen baru di JIFAS?",
            "Apa saja status yang ada dalam workflow approval?",
            "Bagaimana cara melihat riwayat transaksi di JIFAS?"
        };
    }
}





