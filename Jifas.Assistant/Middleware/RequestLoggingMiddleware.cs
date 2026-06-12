using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Jifas.Assistant.Services;

namespace Jifas.Assistant
{
    /// <summary>
    /// Enhanced middleware untuk logging HTTP requests, responses, dan exception handling
    /// Includes correlation ID tracking untuk audit trail
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private const string CorrelationIdHeader = "X-Correlation-ID";

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ILoggerService loggerService)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Skip logging middleware untuk Swagger/OpenAPI endpoints dan root
            if (context.Request.Path.StartsWithSegments("/swagger") || 
                context.Request.Path.StartsWithSegments("/swagger-ui") ||
                context.Request.Path.StartsWithSegments("/swagger.json") ||
                context.Request.Path.StartsWithSegments("/health") ||
                context.Request.Path == "/" ||
                context.Request.Path == "/index.html")
            {
                await _next(context);
                return;
            }
            
            // Generate atau ambil correlation ID
            var correlationId = ExtractOrCreateCorrelationId(context);
            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers[CorrelationIdHeader] = correlationId;

            try
            {
                // Log incoming request dengan correlation ID
                await LogRequest(context, correlationId, loggerService);

                // Copy body untuk reading later (untuk response logging)
                var originalBody = context.Response.Body;
                using (var memoryStream = new MemoryStream())
                {
                    context.Response.Body = memoryStream;

                    try
                    {
                        // Call next middleware
                        await _next(context);

                        // Log response sukses
                        stopwatch.Stop();
                        await LogResponse(context, correlationId, stopwatch.ElapsedMilliseconds, loggerService);
                    }
                    catch (Exception ex)
                    {
                        // Log exception dengan context
                        stopwatch.Stop();
                        LogRequestException(context, ex, correlationId, stopwatch.ElapsedMilliseconds, loggerService);

                        context.Response.Clear();

                        // Set error response
                        context.Response.ContentType = "application/json";
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        
                        var errorResponse = new
                        {
                            success = false,
                            message = "Maaf, terjadi kesalahan teknis dalam memproses permintaan Anda.",
                            correlationId = correlationId,
                            timestamp = DateTime.UtcNow
                        };

                        var json = JsonSerializer.Serialize(errorResponse);
                        await context.Response.WriteAsync(json);
                        return;
                    }
                    finally
                    {
                        // Copy response back ke original stream
                        try
                        {
                            context.Response.Body = originalBody;
                            memoryStream.Position = 0;  // Reset position ke awal sebelum copy
                            await memoryStream.CopyToAsync(originalBody);
                        }
                        catch
                        {
                            // Silently fail jika response sudah ditulis
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                loggerService?.LogErrorWithCorrelation(correlationId, 
                    $"Unhandled exception in request pipeline (Duration: {stopwatch.ElapsedMilliseconds}ms)", ex);
                throw;
            }
        }

        private string ExtractOrCreateCorrelationId(HttpContext context)
        {
            const string correlationIdKey = "X-Correlation-ID";
            
            // Try to extract dari header
            if (context.Request.Headers.TryGetValue(correlationIdKey, out var correlationIdValue))
            {
                var correlationId = correlationIdValue.FirstOrDefault();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    return correlationId;
                }
            }

            // Generate baru jika tidak ada
            return Guid.NewGuid().ToString();
        }

        private async Task LogRequest(HttpContext context, string correlationId, ILoggerService loggerService)
        {
            try
            {
                var request = context.Request;
                var body = "";

                // Hanya log body untuk POST/PUT requests dan bukan content length > 1MB
                if (request.ContentLength > 0 && request.ContentLength < 1024 * 1024 && 
                    (request.Method == "POST" || request.Method == "PUT"))
                {
                    request.EnableBuffering();
                    using (var reader = new StreamReader(request.Body, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                    request.Body.Position = 0;
                }

                var message = $"HTTP {request.Method} {request.Path.Value}";
                if (!string.IsNullOrEmpty(request.QueryString.Value))
                {
                    message += $"?{request.QueryString.Value}";
                }
                if (!string.IsNullOrEmpty(body))
                {
                    message += $" | Body: {(body.Length > 200 ? body[..200] + "..." : body)}";
                }

                loggerService?.LogInformationWithCorrelation(correlationId, $"[REQUEST] {message}");
                
                // Log audit untuk POST/PUT (data modification)
                if (request.Method == "POST" || request.Method == "PUT")
                {
                    var userId = context.User?.Identity?.Name ?? "Unknown";
                    loggerService?.LogAudit(userId, request.Method, request.Path.Value ?? string.Empty, correlationId);
                }
            }
            catch (Exception ex)
            {
                loggerService?.LogErrorWithCorrelation(correlationId, "[REQUEST LOGGING] Error logging request", ex);
            }
        }

        private async Task LogResponse(HttpContext context, string correlationId, long elapsedMs, ILoggerService loggerService)
        {
            try
            {
                var response = context.Response;
                var isSuccessful = response.StatusCode >= 200 && response.StatusCode < 300;
                
                // Jangan log response body jika > 1MB
                var body = "";
                if (response.Body.CanSeek && response.Body.Length < 1024 * 1024)
                {
                    try
                    {
                        response.Body.Seek(0, SeekOrigin.Begin);
                        using (var reader = new StreamReader(response.Body))
                        {
                            body = await reader.ReadToEndAsync();
                        }
                        response.Body.Seek(0, SeekOrigin.Begin);
                    }
                    catch
                    {
                        body = "(unavailable)";
                    }
                }

                var message = $"HTTP {response.StatusCode} {context.Request.Path.Value} | Duration: {elapsedMs}ms";
                if (!string.IsNullOrEmpty(body))
                {
                    message += $" | Body: {(body.Length > 200 ? body[..200] + "..." : body)}";
                }

                if (isSuccessful)
                {
                    loggerService?.LogInformationWithCorrelation(correlationId, $"[RESPONSE] {message}");
                }
                else
                {
                    loggerService?.LogWarningWithCorrelation(correlationId, $"[RESPONSE] {message}");
                }

                // Log performance metrics
                loggerService?.LogPerformance($"HTTP {context.Request.Method}", elapsedMs, correlationId);
            }
            catch (Exception ex)
            {
                loggerService?.LogErrorWithCorrelation(correlationId, "[RESPONSE LOGGING] Error logging response", ex);
            }
        }

        private void LogRequestException(HttpContext context, Exception ex, string correlationId, long elapsedMs, ILoggerService loggerService)
        {
            try
            {
                var message = $"Exception in HTTP {context.Request.Method} {context.Request.Path.Value} | Duration: {elapsedMs}ms";
                loggerService?.LogErrorWithCorrelation(correlationId, message, ex);
                
                // Log audit trail untuk error
                var userId = context.User?.Identity?.Name ?? "Unknown";
                loggerService?.LogAudit(userId, $"ERROR_{context.Request.Method}", $"{ex.GetType().Name}: {ex.Message}", correlationId);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Error logging request exception");
            }
        }
    }
}
