using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
{
    public sealed class AdminAccessRequirement : IAuthorizationRequirement
    {
    }

    public sealed class AdminAccessAuthorizationHandler : AuthorizationHandler<AdminAccessRequirement>
    {
        private static readonly string[] AllowedRoles = { "Admin", "WMTR", "JIFAS_ADMIN" };
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;

        public AdminAccessAuthorizationHandler(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            AdminAccessRequirement requirement)
        {
            if (HasAdminRole(context.User) || HasValidAdminApiKey())
                context.Succeed(requirement);

            return Task.CompletedTask;
        }

        private static bool HasAdminRole(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            return AllowedRoles.Any(role =>
                user.IsInRole(role) ||
                user.HasClaim("role", role) ||
                user.HasClaim("roles", role) ||
                user.HasClaim("UserRole", role));
        }

        private bool HasValidAdminApiKey()
        {
            var configuredKey = _configuration["Admin:ApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
                return false;

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
                return false;

            if (!httpContext.Request.Headers.TryGetValue("X-Admin-Api-Key", out var providedKey))
                return false;

            return string.Equals(
                providedKey.ToString(),
                configuredKey,
                StringComparison.Ordinal);
        }
    }
}
