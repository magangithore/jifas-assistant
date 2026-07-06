using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Pages.Admin
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public LoginModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            // Display login page
        }

        public async Task<IActionResult> OnPostAsync(string apiKey, string? returnUrl = null)
        {
            var adminApiKey = _configuration["Admin:ApiKey"];
            
            if (string.IsNullOrWhiteSpace(adminApiKey))
            {
                ErrorMessage = "Konfigurasi API Key admin tidak ditemukan. Hubungi administrator sistem.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey != adminApiKey)
            {
                ErrorMessage = "API Key tidak valid. Silakan coba lagi.";
                return Page();
            }

            // Create claims for admin user
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "KnowledgeBaseAdmin"),
                new Claim("IsAdmin", "true")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = System.DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return string.IsNullOrWhiteSpace(returnUrl) 
                ? RedirectToPage("/Admin/Monitoring")
                : LocalRedirect(returnUrl);
        }
    }
}
