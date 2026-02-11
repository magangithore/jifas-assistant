using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jifas.Assistant
{
    /// <summary>
    /// Middleware untuk logging semua HTTP requests dan responses
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Log incoming request
                await LogRequest(context);

                // Copy body for reading later
                var originalBody = context.Response.Body;
                using (var memoryStream = new MemoryStream())
                {
                    context.Response.Body = memoryStream;

                    // Call next middleware
                    await _next(context);

                    // Log response
                    stopwatch.Stop();
                    await LogResponse(context, stopwatch.ElapsedMilliseconds);

                    // Copy response back to original stream
                    await memoryStream.CopyToAsync(originalBody);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "An unhandled exception occurred during request processing. Duration: {ElapsedMs}ms",
                    stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        private async Task LogRequest(HttpContext context)
        {
            var request = context.Request;
            var body = "";

            // Only log body for POST/PUT requests
            if (request.ContentLength > 0 && (request.Method == "POST" || request.Method == "PUT"))
            {
                request.EnableBuffering();
                var reader = new StreamReader(request.Body, Encoding.UTF8);
                body = await reader.ReadToEndAsync();
                request.Body.Position = 0;
            }

            _logger.LogInformation(
                "HTTP Request: {Method} {Path} | QueryString: {QueryString} | Body: {Body}",
                request.Method,
                request.Path,
                request.QueryString,
                string.IsNullOrEmpty(body) ? "(empty)" : body);
        }

        private async Task LogResponse(HttpContext context, long elapsedMs)
        {
            var response = context.Response;
            response.Body.Seek(0, SeekOrigin.Begin);
            var body = await new StreamReader(response.Body).ReadToEndAsync();
            response.Body.Seek(0, SeekOrigin.Begin);

            _logger.LogInformation(
                "HTTP Response: {StatusCode} | Path: {Path} | Duration: {ElapsedMs}ms | Body: {Body}",
                response.StatusCode,
                context.Request.Path,
                elapsedMs,
                string.IsNullOrEmpty(body) ? "(empty)" : body);
        }
    }
}
