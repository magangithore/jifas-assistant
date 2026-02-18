using System;
using System.Security.Cryptography;
using System.Text;

namespace Jifas.Assistant.Utilities
{
    /// <summary>
    /// Helper class for generating stable hash codes for caching and identifiers
    /// Uses SHA256 for consistent, cross-platform hashing
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// Generate a stable SHA256-based hash string from input text
        /// Returns hex-encoded lowercase string suitable for cache keys
        /// </summary>
        public static string ToStableHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "empty";

            try
            {
                using (var sha256 = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(input);
                    var hash = sha256.ComputeHash(bytes);
                    return Convert.ToHexString(hash).ToLower();
                }
            }
            catch
            {
                // Fallback: use base64 encoding if hashing fails
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(input)).Replace("/", "-").Replace("+", "_");
            }
        }

        /// <summary>
        /// Generate a stable hash suitable for short identifiers (first 16 chars of SHA256)
        /// </summary>
        public static string ToShortStableHash(string input)
        {
            var hash = ToStableHash(input);
            return hash.Substring(0, Math.Min(16, hash.Length));
        }
    }
}
