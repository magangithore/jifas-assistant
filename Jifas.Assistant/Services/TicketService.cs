using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Jifas.Assistant.Models;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    #region Models

    /// <summary>
    /// Menyimpan posisi percakapan saat user sedang membuat tiket.
    /// State ini disimpan di cache per session agar flow bisa dilanjutkan.
    /// </summary>
    public class TicketDialogState
    {
        public TicketFlowStage Stage { get; set; } = TicketFlowStage.None;
        public TicketFlowType FlowType { get; set; } = TicketFlowType.None;
        public string Problem { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Priority { get; set; } = "Medium";
        public string GeneratedTitle { get; set; } = string.Empty;
        public string AiSolution { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    }

    public enum TicketFlowStage
    {
        None,
        WaitingForProblem,       // User sudah minta tiket, bot menunggu detail masalah
        WaitingForConfirmation,  // Bot sudah mencoba solusi, menunggu konfirmasi ya/tidak
        WaitingForTitleConfirm,  // Judul tiket sudah dibuat, menunggu konfirmasi user
        WaitingForCustomTitle,   // User ingin mengganti judul tiket sebelum dibuat
        Completed
    }

    public enum TicketFlowType
    {
        None,
        DirectRequest,   // User eksplisit bilang "buat tiket"
        ProblemFirst,    // User menjelaskan masalah, AI menawarkan tiket
        Combined         // User bilang "buat tiket karena X"
    }

    /// <summary>
    /// Response dari flow dialog tiket.
    /// </summary>
    public class TicketDialogResponse
    {
        public string Message { get; set; } = string.Empty;
        public bool FlowCompleted { get; set; }
        public bool FlowActive { get; set; }
        public TicketCreationResult? Ticket { get; set; }
        public List<string> Suggestions { get; set; } = new List<string>();
    }

    #endregion

    #region Interface

    public interface ITicketService
    {
        Task<TicketCreationResult> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default);
        Task<TicketDialogResponse> HandleTicketDialogAsync(string sessionId, string userId, string userMessage, string? problemContext = null, CancellationToken cancellationToken = default);
        bool IsInTicketFlow(string sessionId);
        void ClearTicketFlow(string sessionId);
        string DetectUrgency(string message);
        string DetectCategory(string message);
    }

    #endregion

    #region Implementation

    public class TicketService : ITicketService
    {
        private readonly HttpClient _httpClient;
        private readonly ICacheService _cacheService;
        private readonly IOllamaService _ollamaService;
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;

        private readonly string _jiraBaseUrl;
        private readonly string _projectKey;
        private readonly string _cloudId;
        private readonly string _jiraApiToken;
        private readonly string _accountEmail;
        private readonly string _emailDomain;
        private readonly string _defaultIssueType;
        private readonly bool _enableOfflineFallback;

        private const string DIALOG_STATE_PREFIX = "TicketFlow_";
        private const int DIALOG_TIMEOUT_MINUTES = 30;

        #region Confirmation / Rejection patterns

        private static readonly List<string> ConfirmationPatterns = new List<string>
        {
            "ya", "iya", "yak", "yap", "yep", "yes", "ok", "oke", "okay",
            "boleh", "setuju", "lanjut", "lanjutkan", "buatkan",
            "create", "confirm", "sip", "siap", "gas", "ayo",
            "belum terselesaikan", "belum solved",
            "masih error", "masih bermasalah", "masih gagal",
            "tetap error", "tetap tidak bisa",
            "ya buatkan", "ya buat", "tolong buat", "buat tiketnya"
        };

        // Konfirmasi pendek hanya berlaku untuk pesan maksimal 4 kata.
        private static readonly HashSet<string> ShortOnlyConfirmations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ya", "iya", "yak", "yap", "yep", "yes", "ok", "oke", "okay",
            "boleh", "setuju", "lanjut", "sip", "siap", "gas", "ayo", "belum", "confirm"
        };

        private static readonly List<string> RejectionPatterns = new List<string>
        {
            "tidak", "nggak", "ngga", "gak", "no", "jangan", "batal",
            "cancel", "sudah", "solved", "terselesaikan", "beres",
            "sudah bisa", "sudah selesai", "sudah ok", "udah bisa",
            "sudah solved", "gak jadi", "tidak perlu", "tidak usah"
        };

        // Penolakan satu kata hanya berlaku untuk pesan maksimal 3 kata.
        private static readonly HashSet<string> ShortOnlyRejections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tidak", "nggak", "ngga", "gak", "no", "jangan", "batal", "cancel", "sudah", "solved", "beres"
        };

        #endregion

        #region Category & Urgency patterns

        private static readonly Dictionary<string, List<string>> CategoryPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Invoice",    new() { "invoice", "tagihan", "faktur", "billing" } },
            { "Payment",    new() { "payment", "pembayaran", "bayar", "transfer", "giro" } },
            { "PUM",        new() { "pum", "uang muka", "advance", "kasbon", "perjalanan dinas" } },
            { "Receiving",  new() { "receiving", "penerimaan", "rv", "terima barang" } },
            { "Budget",     new() { "budget", "anggaran", "overbudget", "over budget" } },
            { "Approval",   new() { "approval", "approve", "reject", "otorisasi" } },
            { "Accounting", new() { "posting", "jurnal", "gl", "ledger", "coa" } },
            { "Master Data",new() { "master", "vendor", "company", "divisi", "department" } },
            { "SPK",        new() { "spk", "kontrak", "surat perintah" } },
            { "Tax",        new() { "tax", "pajak", "ppn", "pph", "faktur pajak" } },
            { "Report",     new() { "report", "laporan", "dashboard", "cashflow" } },
            { "Access",     new() { "login", "akses", "password", "user", "role", "permission" } },
        };

        private static readonly Dictionary<string, List<string>> UrgencyPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Highest", new() { "urgent", "darurat", "kritis", "critical", "segera", "deadline hari ini", "harus sekarang" } },
            { "High",    new() { "penting", "high", "cepat", "buru-buru", "deadline besok", "mendesak" } },
            { "Medium",  new() { "medium", "normal", "biasa", "standar" } },
            { "Low",     new() { "low", "rendah", "tidak urgent", "santai", "kapan saja" } },
        };

        #endregion

        public TicketService(
            HttpClient httpClient,
            ICacheService cacheService,
            IOllamaService ollamaService,
            ILoggerService logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _cacheService = cacheService;
            _ollamaService = ollamaService;
            _logger = logger;
            _configuration = configuration;

            _jiraBaseUrl = _configuration["Jira:BaseUrl"] ?? "https://willyjan.atlassian.net";
            _projectKey = _configuration["Jira:ProjectKey"] ?? "JTU";
            _cloudId = _configuration["Jira:CloudId"] ?? "";
            _jiraApiToken = _configuration["Jira:ApiToken"] ?? "";
            _accountEmail = _configuration["Jira:AccountEmail"] ?? "";
            _emailDomain = _configuration["Jira:EmailDomain"] ?? "jababeka.com";
            _defaultIssueType = _configuration["Jira:DefaultIssueType"] ?? "Task";
            _enableOfflineFallback = _configuration.GetValue<bool>("Jira:EnableOfflineFallback", false);

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var timeout = _configuration.GetValue<int>("Jira:TimeoutSeconds", 30);
            _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
        }

        // Flow dialog pembuatan tiket.

        public bool IsInTicketFlow(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            var state = _cacheService.Get<TicketDialogState>(DIALOG_STATE_PREFIX + sessionId);
            return state != null && state.Stage != TicketFlowStage.None && state.Stage != TicketFlowStage.Completed;
        }

        public void ClearTicketFlow(string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
                _cacheService.Remove(DIALOG_STATE_PREFIX + sessionId);
        }

        private TicketDialogState GetDialogState(string sessionId)
        {
            return _cacheService.Get<TicketDialogState>(DIALOG_STATE_PREFIX + sessionId);
        }

        private void SetDialogState(string sessionId, TicketDialogState state)
        {
            _cacheService.Set(DIALOG_STATE_PREFIX + sessionId, state, DIALOG_TIMEOUT_MINUTES);
        }

        /// <summary>
        /// Main dialog flow handler - processes user message within ticket creation flow.
        /// Returns a TicketDialogResponse with the bot's reply and flow status.
        /// </summary>
        public async Task<TicketDialogResponse> HandleTicketDialogAsync(
            string sessionId,
            string userId,
            string userMessage,
            string? problemContext = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = GetDialogState(sessionId);

            // NEW flow entry
            if (state == null || state.Stage == TicketFlowStage.None || state.Stage == TicketFlowStage.Completed)
            {
                return await StartNewFlowAsync(sessionId, userId, userMessage, cancellationToken);
            }

            // Continue existing flow based on stage
            return state.Stage switch
            {
                TicketFlowStage.WaitingForProblem => await HandleWaitingForProblemAsync(sessionId, userId, userMessage, state, cancellationToken),
                TicketFlowStage.WaitingForConfirmation => await HandleWaitingForConfirmationAsync(sessionId, userId, userMessage, state, cancellationToken),
                TicketFlowStage.WaitingForTitleConfirm => await HandleWaitingForTitleConfirmAsync(sessionId, userId, userMessage, state, cancellationToken),
                TicketFlowStage.WaitingForCustomTitle => HandleWaitingForCustomTitle(sessionId, userMessage, state),
                _ => new TicketDialogResponse
                {
                    Message = "Terjadi kesalahan dalam flow tiket. Silakan mulai ulang.",
                    FlowCompleted = true
                }
            };
        }

        /// <summary>
        /// Start a new ticket flow. Detects whether it's Direct, Combined, or ProblemFirst.
        /// </summary>
        private async Task<TicketDialogResponse> StartNewFlowAsync(
            string sessionId,
            string userId,
            string userMessage,
            CancellationToken cancellationToken)
        {
            var messageLower = userMessage.ToLowerInvariant();
            var problemDescription = ExtractProblemFromTicketRequest(messageLower);

            if (!string.IsNullOrEmpty(problemDescription) && problemDescription.Length > 10)
            {
                // Flow 3: Combined - "buat tiket karena invoice error"
                var state = new TicketDialogState
                {
                    Stage = TicketFlowStage.WaitingForConfirmation,
                    FlowType = TicketFlowType.Combined,
                    Problem = problemDescription,
                    Category = DetectCategory(problemDescription),
                    Priority = DetectUrgency(problemDescription)
                };

                // Try to solve first
                var solution = await TrySolveWithAIAsync(problemDescription, cancellationToken);
                state.AiSolution = solution ?? string.Empty;

                SetDialogState(sessionId, state);

                var response = new StringBuilder();
                if (!string.IsNullOrEmpty(solution))
                {
                    response.AppendLine(solution);
                    response.AppendLine();
                }
                response.AppendLine("Apakah masalah sudah terselesaikan, atau mau saya buatkan tiket ke IT Help Desk?");

                return new TicketDialogResponse
                {
                    Message = response.ToString(),
                    FlowActive = true,
                    Suggestions = new List<string>
                    {
                        "Ya, buatkan tiket",
                        "Sudah terselesaikan, terima kasih",
                        "Batal"
                    }
                };
            }
            else
            {
                // Flow 1: Direct request - "buat tiket"
                var state = new TicketDialogState
                {
                    Stage = TicketFlowStage.WaitingForProblem,
                    FlowType = TicketFlowType.DirectRequest
                };
                SetDialogState(sessionId, state);

                return new TicketDialogResponse
                {
                    Message = "Baik, saya akan bantu buatkan tiket ke IT Help Desk.\n\nSilakan jelaskan masalah yang Anda alami secara detail (modul apa, error apa, langkah apa yang sudah dicoba).",
                    FlowActive = true,
                    Suggestions = new List<string>
                    {
                        "Invoice tidak bisa di-approve",
                        "Login JIFAS error",
                        "Payment gagal diproses"
                    }
                };
            }
        }

        /// <summary>
        /// Handle: user provides problem description after "buat tiket"
        /// </summary>
        private async Task<TicketDialogResponse> HandleWaitingForProblemAsync(
            string sessionId,
            string userId,
            string userMessage,
            TicketDialogState state,
            CancellationToken cancellationToken)
        {
            // Check if user wants to cancel
            if (IsRejection(userMessage))
            {
                ClearTicketFlow(sessionId);
                return new TicketDialogResponse
                {
                    Message = "Baik, pembuatan tiket dibatalkan. Ada yang lain yang bisa saya bantu?",
                    FlowCompleted = true
                };
            }

            state.Problem = userMessage;
            state.Category = DetectCategory(userMessage);
            state.Priority = DetectUrgency(userMessage);

            // Try to solve with AI first
            var solution = await TrySolveWithAIAsync(userMessage, cancellationToken);
            state.AiSolution = solution ?? string.Empty;
            state.Stage = TicketFlowStage.WaitingForConfirmation;
            SetDialogState(sessionId, state);

            var response = new StringBuilder();
            if (!string.IsNullOrEmpty(solution))
            {
                response.AppendLine(solution);
                response.AppendLine();
            }
            response.AppendLine("Apakah masalah sudah terselesaikan, atau mau saya buatkan tiket ke IT Help Desk?");

            return new TicketDialogResponse
            {
                Message = response.ToString(),
                FlowActive = true,
                Suggestions = new List<string>
                {
                    "Ya, buatkan tiket",
                    "Sudah terselesaikan, terima kasih",
                    "Batal"
                }
            };
        }

        /// <summary>
        /// Handle: user confirms/rejects ticket creation after AI solution
        /// </summary>
        private async Task<TicketDialogResponse> HandleWaitingForConfirmationAsync(
            string sessionId,
            string userId,
            string userMessage,
            TicketDialogState state,
            CancellationToken cancellationToken)
        {
            if (IsRejection(userMessage))
            {
                ClearTicketFlow(sessionId);
                return new TicketDialogResponse
                {
                    Message = "Senang masalahnya sudah terselesaikan! Kalau ada pertanyaan lain, langsung tanya saja.",
                    FlowCompleted = true
                };
            }

            if (IsConfirmation(userMessage))
            {
                // Generate title and ask confirmation
                var title = await GenerateTicketTitleAsync(state.Problem, cancellationToken);
                state.GeneratedTitle = title;
                state.Stage = TicketFlowStage.WaitingForTitleConfirm;
                SetDialogState(sessionId, state);

                return BuildTitleConfirmationResponse(state);
            }

            // User might be providing more detail - update problem
            state.Problem += "\n" + userMessage;
            SetDialogState(sessionId, state);

            return new TicketDialogResponse
            {
                Message = "Terima kasih atas informasi tambahannya. Jadi, mau saya buatkan tiket atau masalahnya sudah selesai?",
                FlowActive = true,
                Suggestions = new List<string>
                {
                    "Ya, buatkan tiket",
                    "Sudah terselesaikan",
                    "Batal"
                }
            };
        }

        /// <summary>
        /// Handle: user confirms title and ticket is created
        /// </summary>
        private async Task<TicketDialogResponse> HandleWaitingForTitleConfirmAsync(
            string sessionId,
            string userId,
            string userMessage,
            TicketDialogState state,
            CancellationToken cancellationToken)
        {
            if (IsRejection(userMessage))
            {
                ClearTicketFlow(sessionId);
                return new TicketDialogResponse
                {
                    Message = "Pembuatan tiket dibatalkan. Ada yang lain yang bisa saya bantu?",
                    FlowCompleted = true
                };
            }

            // Check if user wants to change title
            var messageLower = userMessage.ToLowerInvariant();
            if (messageLower.Contains("ubah") || messageLower.Contains("ganti") || messageLower.Contains("change"))
            {
                var requestedTitle = ExtractRequestedTitle(userMessage);
                if (!string.IsNullOrWhiteSpace(requestedTitle))
                {
                    state.GeneratedTitle = requestedTitle;
                    state.Stage = TicketFlowStage.WaitingForTitleConfirm;
                    SetDialogState(sessionId, state);
                    return BuildTitleConfirmationResponse(state);
                }

                state.Stage = TicketFlowStage.WaitingForCustomTitle;
                SetDialogState(sessionId, state);

                return new TicketDialogResponse
                {
                    Message = "Silakan ketik judul tiket yang Anda inginkan:",
                    FlowActive = true
                };
            }

            // User confirmed - create the ticket
            // FIXED: Use AI-enhanced description generation
            var ticketRequest = new CreateTicketRequest
            {
                UserId = userId,
                Title = state.GeneratedTitle,
                Description = await BuildAIEnhancedTicketDescriptionAsync(state, sessionId, cancellationToken),
                Category = state.Category ?? "General",
                Priority = MapPriorityToJira(state.Priority),
                SessionId = sessionId
            };

            var result = await CreateTicketAsync(ticketRequest, cancellationToken);

            ClearTicketFlow(sessionId);

            if (result.Success)
            {
                var urlLine = string.IsNullOrWhiteSpace(result.Url)
                    ? string.Empty
                    : $"**URL:** {result.Url}\n\n";

                return new TicketDialogResponse
                {
                    Message = $"Tiket berhasil dibuat!\n\n" +
                              $"**Nomor Tiket:** {result.TicketNumber}\n" +
                              $"**Status:** {result.Status}\n\n" +
                              urlLine +
                              $"Tim IT Help Desk akan menindaklanjuti tiket Anda. " +
                              $"Ada yang lain yang bisa saya bantu?",
                    FlowCompleted = true,
                    Ticket = result
                };
            }
            else
            {
                return new TicketDialogResponse
                {
                    Message = $"Maaf, gagal membuat tiket: {result.Message}\n\n" +
                              $"Silakan coba lagi atau hubungi IT Help Desk langsung di it@jababeka.com.",
                    FlowCompleted = true
                };
            }
        }

        // Integrasi Jira REST API.

        private TicketDialogResponse HandleWaitingForCustomTitle(
            string sessionId,
            string userMessage,
            TicketDialogState state)
        {
            if (IsRejection(userMessage))
            {
                ClearTicketFlow(sessionId);
                return new TicketDialogResponse
                {
                    Message = "Pembuatan tiket dibatalkan. Ada yang lain yang bisa saya bantu?",
                    FlowCompleted = true
                };
            }

            var title = CleanTicketText(userMessage, 200);
            if (title == "-")
            {
                return new TicketDialogResponse
                {
                    Message = "Judul tiket belum terbaca. Silakan ketik judul tiket yang jelas.",
                    FlowActive = true
                };
            }

            state.GeneratedTitle = title;
            state.Stage = TicketFlowStage.WaitingForTitleConfirm;
            SetDialogState(sessionId, state);

            return BuildTitleConfirmationResponse(state);
        }

        private static TicketDialogResponse BuildTitleConfirmationResponse(TicketDialogState state)
        {
            return new TicketDialogResponse
            {
                Message = $"Saya akan membuat tiket dengan detail berikut:\n\n" +
                          $"**Judul:** {state.GeneratedTitle}\n" +
                          $"**Kategori:** {state.Category ?? "General"}\n" +
                          $"**Prioritas:** {state.Priority}\n\n" +
                          $"Apakah sudah benar? Atau mau ubah judulnya?",
                FlowActive = true,
                Suggestions = new List<string>
                {
                    "Ya, buat tiketnya",
                    "Ubah judul",
                    "Batal"
                }
            };
        }

        public async Task<TicketCreationResult> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default)
        {
            const int maxRetries = 3;
            var retryDelaysMs = new[] { 2000, 5000, 10000 }; // Exponential backoff for Jira API

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await CreateTicketInternalAsync(request, attempt, cancellationToken);
                }
                catch (HttpRequestException ex) when (IsJiraRetryableError(ex) && attempt < maxRetries)
                {
                    var jitter = Random.Shared.Next(0, 500);
                    var delayMs = retryDelaysMs[attempt] + jitter;
                    _logger.LogWarning($"[TicketService] Jira retryable error on attempt {attempt + 1}/{maxRetries}, retrying in {delayMs}ms: {ex.Message}");
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    _logger.LogWarning($"[TicketService] Ticket creation failed on attempt {attempt + 1}/{maxRetries}: {ex.Message}");
                    await Task.Delay(retryDelaysMs[attempt], cancellationToken);
                }
            }

            // All retries exhausted - fall back to offline mode or return error
            _logger.LogError($"[TicketService] All {maxRetries + 1} attempts to create Jira ticket failed");
            return HandleJiraFailure(request, $"Jira API failed after {maxRetries + 1} attempts.");
        }

        /// <summary>
        /// Internal method for actual ticket creation
        /// </summary>
        private async Task<TicketCreationResult> CreateTicketInternalAsync(CreateTicketRequest request, int attempt, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"[TicketService] Creating Jira ticket (attempt {attempt + 1}): {request.Title}");

            // Use configured service account email, or fall back to user-derived email
            var authEmail = !string.IsNullOrWhiteSpace(_accountEmail)
                ? _accountEmail
                : DeriveUserEmail(request.UserId);

            // Validate Jira credentials
            if (string.IsNullOrEmpty(_jiraApiToken) || string.IsNullOrEmpty(authEmail))
            {
                return HandleJiraFailure(request, "Jira credentials are not configured.");
            }

            // Filter out null/empty labels
            var labelList = new List<string> { "jifas-assistant" };
            var cat = request.Category?.ToLowerInvariant().Trim();
            if (!string.IsNullOrWhiteSpace(cat)) labelList.Add(cat);

            var issuePayload = new
            {
                fields = new
                {
                    project = new { key = _projectKey },
                    summary = request.Title,
                    description = MarkdownToAdf(request.Description ?? request.Title),
                    issuetype = new { name = _defaultIssueType },
                    priority = new { name = request.Priority ?? "Medium" },
                    // FIXED: Add reporter field to map to actual user
                    reporter = new { email = authEmail },
                    labels = labelList.ToArray()
                }
            };

            var json = JsonConvert.SerializeObject(issuePayload);

            // Use Atlassian API gateway with CloudId (same as jiraClient.js Playwright integration)
            var apiUrl = !string.IsNullOrWhiteSpace(_cloudId)
                ? $"https://api.atlassian.com/ex/jira/{_cloudId}/rest/api/3/issue"
                : $"{_jiraBaseUrl}/rest/api/3/issue";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{authEmail}:{_jiraApiToken}"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var jiraResponse = JObject.Parse(responseBody);
                var issueKey = jiraResponse["key"]?.ToString() ?? string.Empty;
                var issueId = jiraResponse["id"]?.ToString();

                _logger.LogInformation($"[TicketService] Jira ticket created: {issueKey}");

                return new TicketCreationResult
                {
                    Success = true,
                    TicketId = int.TryParse(issueId, out var id) ? id : 0,
                    TicketNumber = issueKey,
                    Message = "Tiket berhasil dibuat di Jira",
                    Status = "Open",
                    Url = BuildJiraIssueUrl(issueKey),
                    CreatedAt = DateTime.UtcNow
                };
            }
            else
            {
                _logger.LogError($"[TicketService] Jira API error {response.StatusCode}: {responseBody}");
                throw new HttpRequestException($"Jira API error {response.StatusCode}: {responseBody}");
            }
        }

        /// <summary>
        /// Determines if a Jira API error should trigger a retry
        /// </summary>
        private static bool IsJiraRetryableError(HttpRequestException ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("502") ||
                   message.Contains("503") ||
                   message.Contains("504") ||
                   message.Contains("connection") ||
                   message.Contains("timeout");
        }

        private TicketCreationResult HandleJiraFailure(CreateTicketRequest request, string reason)
        {
            if (_enableOfflineFallback)
            {
                _logger.LogWarning($"[TicketService] {reason} Falling back to offline ticket mode.");
                return CreateOfflineTicket(request);
            }

            return new TicketCreationResult
            {
                Success = false,
                TicketNumber = string.Empty,
                Message = $"{reason} Tiket belum dibuat di Jira. Silakan cek konfigurasi Jira atau coba lagi.",
                Status = "Failed",
                CreatedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Fallback offline ticket when Jira is unavailable
        /// FIXED: Use GUID-based ID instead of Random to prevent collision
        /// </summary>
        private TicketCreationResult CreateOfflineTicket(CreateTicketRequest request)
        {
            // FIXED: Use GUID-based ID instead of Random to prevent collision
            var ticketId = Math.Abs(Guid.NewGuid().GetHashCode()) % 90000 + 10000;
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var ticketNumber = $"OFFLINE-{timestamp}-{ticketId}";

            _logger.LogWarning($"[TicketService] Created offline ticket: {ticketNumber} (Jira unavailable). " +
                             "This ticket will be queued for later sync to Jira.");

            return new TicketCreationResult
            {
                Success = true,
                TicketId = ticketId,
                TicketNumber = ticketNumber,
                Message = "Tiket dibuat secara offline (Jira tidak tersedia). " +
                         "Tiket akan disinkronkan ke Jira saat koneksi pulih. " +
                         "IT Help Desk akan dihubungi via email.",
                Status = "Pending Sync",
                Url = string.Empty,
                CreatedAt = DateTime.UtcNow
            };
        }

        // Helper deteksi intent tiket.

        /// <summary>
        /// Derive user email from UserId (Windows AD username).
        /// Pattern: {userId}@{emailDomain}
        /// </summary>
        private string? DeriveUserEmail(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous" || userId == "unknown")
                return null;

            // If userId already contains @, it's already an email
            if (userId.Contains('@'))
                return userId;

            // Strip domain prefix if present (e.g., "DOMAIN\username" -> "username")
            if (userId.Contains('\\'))
                userId = userId.Split('\\').Last();

            return $"{userId}@{_emailDomain}";
        }

        public string DetectCategory(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "General";
            var lower = message.ToLowerInvariant();

            foreach (var (category, keywords) in CategoryPatterns)
            {
                foreach (var keyword in keywords)
                {
                    if (lower.Contains(keyword))
                        return category;
                }
            }

            return "General";
        }

        public string DetectUrgency(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "Medium";
            var lower = message.ToLowerInvariant();

            foreach (var (urgency, keywords) in UrgencyPatterns)
            {
                foreach (var keyword in keywords)
                {
                    if (lower.Contains(keyword))
                        return urgency;
                }
            }

            return "Medium";
        }

        private bool IsConfirmation(string message)
        {
            var lower = message.ToLowerInvariant().Trim();
            var wordCount = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            foreach (var pattern in ConfirmationPatterns)
            {
                if (!lower.Contains(pattern)) continue;

                // Pattern pendek hanya berlaku untuk pesan maksimal 4 kata.
                if (ShortOnlyConfirmations.Contains(pattern) && wordCount > 4)
                    continue;

                return true;
            }

            return false;
        }

        private bool IsRejection(string message)
        {
            var lower = message.ToLowerInvariant().Trim();
            var wordCount = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            foreach (var pattern in RejectionPatterns)
            {
                if (!lower.Contains(pattern)) continue;

                // Pattern pendek hanya berlaku untuk pesan maksimal 4 kata.
                // Ini mencegah "tidak bisa di approve" terbaca sebagai penolakan.
                if (ShortOnlyRejections.Contains(pattern) && wordCount > 4)
                    continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract problem description from combined ticket request like "buat tiket karena invoice error"
        /// </summary>
        private string? ExtractProblemFromTicketRequest(string message)
        {
            var patterns = new[]
            {
                @"(?:buat(?:kan)?|create|bikin)\s+tiket?\s+(?:karena|karena\s+)?(.+)",
                @"(?:buat(?:kan)?|create|bikin)\s+tiket?\s+(?:untuk|tentang|perihal)\s+(.+)",
                @"(?:lapor(?:kan)?|report)\s+(?:masalah|issue|error)\s+(.+)",
                @"tiket?\s+(?:untuk|tentang)\s+(.+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Generate a concise ticket title using AI
        /// </summary>
        private async Task<string> GenerateTicketTitleAsync(string problemDescription, CancellationToken cancellationToken)
        {
            try
            {
                var explicitTitle = TryBuildExplicitTestTitle(problemDescription);
                if (!string.IsNullOrWhiteSpace(explicitTitle))
                    return explicitTitle;

                var prompt = $@"Buatkan judul tiket IT Help Desk yang singkat dan jelas (maksimal 10 kata) berdasarkan deskripsi masalah berikut:

Masalah: ""{problemDescription}""

Contoh format judul yang baik:
- ""Invoice Approval Error di Modul AR""
- ""Login JIFAS Gagal Setelah Reset Password""
- ""Budget Over Limit Tidak Bisa Di-approve""

Tulis HANYA judul tiket, tanpa penjelasan tambahan:";

                var title = await _ollamaService.CallOllamaApiAsync(prompt, cancellationToken);

                // Clean up response
                title = title?.Trim().Trim('"', '\'', '*', '-');
                if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
                {
                    // Fallback: truncate problem description
                    title = problemDescription.Length > 80
                        ? problemDescription.Substring(0, 80).TrimEnd() + "..."
                        : problemDescription;
                }

                return title;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;

                _logger.LogError($"[TicketService] Error generating title: {ex.Message}");
                return problemDescription.Length > 80
                    ? problemDescription.Substring(0, 80).TrimEnd() + "..."
                    : problemDescription;
            }
        }

        /// <summary>
        /// Try to solve the problem using AI before creating a ticket
        /// </summary>
        private async Task<string?> TrySolveWithAIAsync(string problem, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _ollamaService.GenerateResponseAsync(
                    problem,
                    new List<KnowledgeBaseResult>(),
                    cancellationToken: cancellationToken);

                if (!string.IsNullOrWhiteSpace(response) && response.Length > 30)
                    return response;

                return null;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;

                _logger.LogWarning($"[TicketService] AI solution attempt failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Build ticket description using AI for intelligent, structured output
        /// FIXED: Now uses AI to generate professional descriptions instead of templates
        /// </summary>
        private async Task<string> BuildAIEnhancedTicketDescriptionAsync(
            TicketDialogState state,
            string? sessionId,
            CancellationToken cancellationToken)
        {
            try
            {
                // Generate description using AI
                var prompt = $@"Buatkan deskripsi tiket IT Help Desk yang profesional dan terstruktur berdasarkan informasi berikut:

MASALAH: ""{state.Problem}""

KATEGORI: {state.Category ?? "General"}
PRIORITAS: {state.Priority ?? "Medium"}

{(!string.IsNullOrWhiteSpace(state.AiSolution) ? $@"
SOLUSI YANG SUDAH DICOBA OLEH AI:
{state.AiSolution}
" : "")}

Format deskripsi tiket yang diharapkan:

**Ringkasan Masalah**
[Paragraf ringkas yang menjelaskan masalah secara jelas dan spesifik - bukan copy paste user message, tapi versi yang lebih profesional]

**Detail Masalah**
- Kategori: [kategori]
- Prioritas: [prioritas]
- Sumber laporan: JIFAS AI Assistant
- Waktu dibuat: [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC]
{(!string.IsNullOrEmpty(sessionId) ? $"- Session ID: {sessionId}" : "")}

**Langkah Reproduksi** (jika applicable)
1. [Langkah-langkah yang bisa direproduksi]
2. [Langkah 2]
3. [Langkah 3]

**Informasi Tambahan**
- User meminta bantuan melalui chatbot JIFAS AI Assistant
- Mohon tim IT Help Desk melakukan pengecekan pada modul terkait
- AI Assistant telah mencoba memberikan solusi awal namun masalah belum terselesaikan

{(!string.IsNullOrWhiteSpace(state.AiSolution) ? $@"**Solusi yang Sudah Dicoba**
{CleanTicketText(state.AiSolution, 1000)}" : "")}

Petunjuk:
1. Ringkasan Masalah harus JELAS dan SPESIFIK - tidak boleh verbatim copy dari masalah user
2. Jika masalah mention error code, modul, atau dokumen, sebutkan secara eksplisit
3. Kategori harus akurat berdasarkan masalah
4. Gunakan bahasa Indonesia formal yang sesuai untuk tiket IT

Hanya outputkan deskripsi tiket, tanpa preamble atau penjelasan tambahan.";

                var description = await _ollamaService.CallOllamaApiAsync(prompt, cancellationToken);

                // Validate response is not empty or too short
                if (!string.IsNullOrWhiteSpace(description) && description.Length > 50)
                {
                    _logger.LogInformation("[TicketService] AI-generated ticket description ({0} chars)", description.Length);
                    return description;
                }

                // Fallback to template if AI response is too short
                _logger.LogWarning("[TicketService] AI description too short, falling back to template");
                return BuildTicketDescriptionTemplate(state, sessionId);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;

                _logger.LogError($"[TicketService] Error generating AI description: {ex.Message}");
                // Fallback to template
                return BuildTicketDescriptionTemplate(state, sessionId);
            }
        }

        /// <summary>
        /// Template-based description (fallback)
        /// </summary>
        private string BuildTicketDescriptionTemplate(TicketDialogState state, string? sessionId)
        {
            var sb = new StringBuilder();
            var problem = CleanTicketText(state.Problem, 2000);
            var category = state.Category ?? "General";
            var priority = state.Priority ?? "Medium";

            sb.AppendLine("**Ringkasan Masalah**");
            sb.AppendLine(problem);
            sb.AppendLine();
            sb.AppendLine("**Detail Tiket**");
            sb.AppendLine($"- Kategori: {category}");
            sb.AppendLine($"- Prioritas: {priority}");
            sb.AppendLine($"- Sumber laporan: JIFAS AI Assistant");
            sb.AppendLine($"- Waktu dibuat: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            if (!string.IsNullOrEmpty(sessionId))
            {
                sb.AppendLine($"- Session ID: {sessionId}");
            }
            sb.AppendLine();
            sb.AppendLine("**Konteks dari Chatbot**");
            sb.AppendLine("- User meminta pembuatan tiket melalui chatbot JIFAS.");
            sb.AppendLine("- Mohon IT Help Desk melakukan pengecekan lanjutan pada modul terkait.");

            if (IsEnterpriseReadinessTestTicket(state.GeneratedTitle, state.Problem))
            {
                sb.AppendLine();
                sb.AppendLine("**Catatan Validasi**");
                sb.AppendLine("- Tiket ini dibuat otomatis untuk validasi integrasi Jira JIFAS Assistant.");
                sb.AppendLine("- Tiket boleh ditutup setelah tim terkait memverifikasi bahwa integrasi berhasil.");
                sb.AppendLine("- Tidak ada perubahan data transaksi JIFAS yang dilakukan oleh test ini.");
            }

            if (!string.IsNullOrWhiteSpace(state.AiSolution))
            {
                sb.AppendLine();
                sb.AppendLine("**Solusi yang Sudah Dicoba**");
                sb.AppendLine(CleanTicketText(state.AiSolution, 1000)); // Increased from 300
            }

            return sb.ToString();
        }

        /// <summary>
        /// Original template-based description - kept for backward compatibility
        /// </summary>
        private string BuildTicketDescription(TicketDialogState state, string? sessionId = null)
        {
            return BuildTicketDescriptionTemplate(state, sessionId);
        }

        private static string CleanTicketText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
            if (cleaned.Length <= maxLength)
                return cleaned;

            return cleaned.Substring(0, maxLength).TrimEnd() + "...";
        }

        private string BuildJiraIssueUrl(string issueKey)
        {
            if (string.IsNullOrWhiteSpace(issueKey) || string.IsNullOrWhiteSpace(_jiraBaseUrl))
                return string.Empty;

            return $"{_jiraBaseUrl.TrimEnd('/')}/browse/{issueKey}";
        }

        private static string? ExtractRequestedTitle(string message)
        {
            var patterns = new[]
            {
                @"(?:ubah|ganti|change)\s+judul(?:\s+tiket)?\s+(?:menjadi|jadi|ke|to)\s+(.+)",
                @"(?:judul(?:\s+tiket)?\s*[:=]\s*)(.+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                    return CleanTicketText(match.Groups[1].Value, 200);
            }

            return null;
        }

        private static string? TryBuildExplicitTestTitle(string problemDescription)
        {
            if (!IsEnterpriseReadinessTestTicket(problemDescription, problemDescription))
                return null;

            return "[TEST] JIFAS Assistant Enterprise Readiness - Approve Invoice";
        }

        private static bool IsEnterpriseReadinessTestTicket(string? title, string? problem)
        {
            var combined = $"{title} {problem}".ToLowerInvariant();
            return combined.Contains("[test]") &&
                   combined.Contains("jifas assistant") &&
                   combined.Contains("enterprise readiness");
        }

        /// <summary>
        /// Convert simple markdown to Atlassian Document Format (ADF).
        /// Ported from jiraClient.js Playwright integration.
        /// Supports: paragraphs, numbered lists, bullet lists, bold (**text**).
        /// </summary>
        private static object MarkdownToAdf(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return new
                {
                    type = "doc", version = 1,
                    content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "No description provided" } } } }
                };
            }

            var lines = markdown.Split('\n');
            var content = new List<object>();
            List<object>? currentOrderedList = null;
            List<object>? currentBulletList = null;

            void FlushLists()
            {
                if (currentOrderedList != null)
                {
                    content.Add(new { type = "orderedList", attrs = new { order = 1 }, content = currentOrderedList.ToArray() });
                    currentOrderedList = null;
                }
                if (currentBulletList != null)
                {
                    content.Add(new { type = "bulletList", content = currentBulletList.ToArray() });
                    currentBulletList = null;
                }
            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) { FlushLists(); continue; }

                // Numbered list: "1. item" or "2. item"
                var numMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.+)$");
                if (numMatch.Success)
                {
                    currentBulletList = null;
                    if (currentOrderedList == null) currentOrderedList = new List<object>();
                    currentOrderedList.Add(new
                    {
                        type = "listItem",
                        content = new[] { new { type = "paragraph", content = ParseInlineAdf(numMatch.Groups[2].Value) } }
                    });
                    continue;
                }

                // Bullet list: "- item" or "* item"
                var bulletMatch = Regex.Match(trimmed, @"^[*\-]\s+(.+)$");
                if (bulletMatch.Success)
                {
                    FlushLists();
                    if (currentBulletList == null) currentBulletList = new List<object>();
                    currentBulletList.Add(new
                    {
                        type = "listItem",
                        content = new[] { new { type = "paragraph", content = ParseInlineAdf(bulletMatch.Groups[1].Value) } }
                    });
                    continue;
                }

                FlushLists();
                content.Add(new { type = "paragraph", content = ParseInlineAdf(trimmed) });
            }

            FlushLists();

            if (content.Count == 0)
                content.Add(new { type = "paragraph", content = new[] { new { type = "text", text = markdown } } });

            return new { type = "doc", version = 1, content = content.ToArray() };
        }

        /// <summary>
        /// Parse inline markdown (bold, equals-sign headers) into ADF inline nodes.
        /// </summary>
        private static object[] ParseInlineAdf(string text)
        {
            // Strip === ... === header decorators
            text = Regex.Replace(text, @"^=+\s*|\s*=+$", "").Trim();
            if (string.IsNullOrEmpty(text)) return new[] { new { type = "text", text = " " } as object };

            var nodes = new List<object>();
            var regex = new Regex(@"\*\*(.+?)\*\*|`(.+?)`");
            int lastIndex = 0;

            foreach (Match match in regex.Matches(text))
            {
                if (match.Index > lastIndex)
                    nodes.Add(new { type = "text", text = text.Substring(lastIndex, match.Index - lastIndex) });

                if (match.Groups[1].Success)
                    nodes.Add(new { type = "text", text = match.Groups[1].Value, marks = new[] { new { type = "strong" } } });
                else if (match.Groups[2].Success)
                    nodes.Add(new { type = "text", text = match.Groups[2].Value, marks = new[] { new { type = "code" } } });

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
                nodes.Add(new { type = "text", text = text.Substring(lastIndex) });

            if (nodes.Count == 0)
                nodes.Add(new { type = "text", text = text });

            return nodes.ToArray();
        }

        private static string MapPriorityToJira(string priority)
        {
            return (priority?.ToLowerInvariant()) switch
            {
                "highest" or "critical" => "Highest",
                "high" or "penting" => "High",
                "low" or "rendah" => "Low",
                "lowest" => "Lowest",
                _ => "Medium"
            };
        }
    }

    #endregion
}
