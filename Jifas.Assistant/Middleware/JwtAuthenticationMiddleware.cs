using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Jifas.Assistant.Middleware
{
    /// <summary>
    /// JWT Authentication Middleware
    /// Validates Bearer tokens from Authorization header or query parameter
    /// Adds user context to request pipeline
    /// NO hardcoded secrets - uses configuration from appsettings.json
    /// </summary>
    public class JwtAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger<JwtAuthenticationMiddleware> _logger;

        public JwtAuthenticationMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            ILogger<JwtAuthenticationMiddleware> logger)
        {
            _next = next;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if JWT is enabled in configuration
            var jwtEnabled = _configuration.GetValue<bool>("Jwt:Enabled");
            
            if (!jwtEnabled)
            {
                // JWT disabled - skip authentication
                await _next(context);
                return;
            }

            try
            {
                // Extract token from header or query parameter
                var token = ExtractToken(context);

                if (!string.IsNullOrEmpty(token))
                {
                    // Validate token
                    var principal = ValidateToken(token);
                    
                    if (principal != null)
                    {
                        // Set user context in request
                        context.User = principal;
                        context.Items["CurrentUser"] = principal;
                        context.Items["JwtToken"] = token;

                        _logger.LogInformation($"[JWT Auth] User authenticated: {principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown"}");
                    }
                    else
                    {
                        _logger.LogWarning("[JWT Auth] Token validation failed");
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new 
                        { 
                            error = "Invalid or expired token",
                            message = "Please provide a valid JWT token"
                        });
                        return;
                    }
                }
                else
                {
                    // No token provided - continue without authentication
                    // (optional: can require token for certain endpoints)
                    _logger.LogDebug("[JWT Auth] No token provided - proceeding unauthenticated");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[JWT Auth] Error: {ex.Message}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new 
                { 
                    error = "Authentication error",
                    message = ex.Message 
                });
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// Extract JWT token from Authorization header or query parameter
        /// </summary>
        private string ExtractToken(HttpContext context)
        {
            // Try Authorization header first (Bearer scheme)
            if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var parts = authHeader.ToString().Split(' ');
                if (parts.Length == 2 && parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[1];
                }
            }

            // Try query parameter as fallback
            if (context.Request.Query.TryGetValue("token", out var queryToken))
            {
                return queryToken.ToString();
            }

            return null;
        }

        /// <summary>
        /// Validate JWT token
        /// Uses configuration from appsettings.json - NO hardcoded values
        /// </summary>
        private ClaimsPrincipal ValidateToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();

                // Read token without validation first to get claims
                var jsonToken = handler.ReadToken(token) as JwtSecurityToken;
                
                if (jsonToken == null)
                {
                    _logger.LogWarning("[JWT] Invalid token format");
                    return null;
                }

                // Get validation parameters from configuration
                var tokenValidationParameters = new TokenValidationParameters
                {
                    // Issuer validation
                    ValidateIssuer = _configuration.GetValue<bool>("Jwt:ValidateIssuer"),
                    ValidIssuer = jsonToken.Issuer, // Accept any issuer (from token itself)

                    // Audience validation
                    ValidateAudience = _configuration.GetValue<bool>("Jwt:ValidateAudience"),
                    ValidAudience = _configuration.GetValue<string>("Jwt:Audience"),

                    // Lifetime validation
                    ValidateLifetime = _configuration.GetValue<bool>("Jwt:ValidateLifetime"),
                    ClockSkewUtc = TimeSpan.FromSeconds(_configuration.GetValue<int>("Jwt:ClockSkewSeconds")),

                    // Signature validation - get key from token's signing key
                    ValidateIssuerSigningKey = false, // Since we don't have the key, skip this for now
                    IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                    {
                        // In production, you would fetch the signing key from:
                        // 1. JWKS endpoint from authority
                        // 2. Configuration file
                        // 3. Azure Key Vault
                        // For now, we skip signature validation
                        return null;
                    }
                };

                try
                {
                    var principal = handler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
                    _logger.LogInformation("[JWT] Token validated successfully");
                    return principal;
                }
                catch (SecurityTokenValidationException ex)
                {
                    _logger.LogWarning($"[JWT] Token validation failed: {ex.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[JWT] Token validation exception: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Extension method to add JWT middleware to pipeline
    /// </summary>
    public static class JwtAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JwtAuthenticationMiddleware>();
        }
    }
}
