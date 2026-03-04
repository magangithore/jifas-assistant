using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Jifas.Assistant.Tests
{
    /// <summary>
    /// Test harness untuk memvalidasi koneksi dengan Local AI Server (Ollama)
    /// Run ini untuk menguji konfigurasi local AI sebelum deploy
    /// </summary>
    public class LocalAITestHarness
    {
        private readonly string _baseUrl = "http://10.0.12.54:11434";
        private readonly string _model = "qwen3:8b";
        private readonly HttpClient _httpClient;

        public LocalAITestHarness()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>
        /// Test 1: Check jika Local AI Server tersedia
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                Console.WriteLine("?? [Test 1] Checking Local AI Server availability...");
                Console.WriteLine($"   Endpoint: {_baseUrl}");

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("? [Test 1] PASSED - Server is available");
                    return true;
                }
                else
                {
                    Console.WriteLine($"? [Test 1] FAILED - Server returned: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? [Test 1] FAILED - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 2: Check jika model qwen3:8b tersedia
        /// </summary>
        public async Task<bool> TestModelAvailabilityAsync()
        {
            try
            {
                Console.WriteLine($"\n?? [Test 2] Checking if model '{_model}' is available...");
                
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                var content = await response.Content.ReadAsStringAsync();
                
                if (content.Contains(_model))
                {
                    Console.WriteLine($"? [Test 2] PASSED - Model '{_model}' is available");
                    return true;
                }
                else
                {
                    Console.WriteLine($"? [Test 2] FAILED - Model '{_model}' not found");
                    Console.WriteLine($"   Response: {content}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? [Test 2] FAILED - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 3: Simple prompt test
        /// </summary>
        public async Task<bool> TestSimplePromptAsync()
        {
            try
            {
                Console.WriteLine("\n?? [Test 3] Testing simple prompt...");
                
                var prompt = "Siapa yang membuat Anda? Jawab dalam 1 kalimat saja.";
                var result = await CallLocalAIAsync(prompt);
                
                if (!string.IsNullOrEmpty(result))
                {
                    Console.WriteLine($"? [Test 3] PASSED");
                    Console.WriteLine($"   Prompt: {prompt}");
                    Console.WriteLine($"   Response: {result.Substring(0, Math.Min(100, result.Length))}...");
                    return true;
                }
                else
                {
                    Console.WriteLine($"? [Test 3] FAILED - Empty response");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? [Test 3] FAILED - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 4: JIFAS-specific knowledge test
        /// </summary>
        public async Task<bool> TestJIFASKnowledgeAsync()
        {
            try
            {
                Console.WriteLine("\n?? [Test 4] Testing JIFAS-specific query...");
                
                var prompt = @"Apa itu JIFAS? Jelaskan dalam maksimal 2 kalimat.";
                var result = await CallLocalAIAsync(prompt);
                
                if (!string.IsNullOrEmpty(result))
                {
                    Console.WriteLine($"? [Test 4] PASSED");
                    Console.WriteLine($"   Prompt: {prompt}");
                    Console.WriteLine($"   Response: {result}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"? [Test 4] FAILED - Empty response");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? [Test 4] FAILED - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test 5: Response speed test
        /// </summary>
        public async Task<bool> TestResponseSpeedAsync()
        {
            try
            {
                Console.WriteLine("\n?? [Test 5] Testing response speed...");
                
                var startTime = DateTime.Now;
                var prompt = "Halo! Apa kabar?";
                var result = await CallLocalAIAsync(prompt);
                var duration = DateTime.Now - startTime;
                
                if (!string.IsNullOrEmpty(result))
                {
                    Console.WriteLine($"? [Test 5] PASSED");
                    Console.WriteLine($"   Response time: {duration.TotalSeconds:F2} seconds");
                    Console.WriteLine($"   Response: {result}");
                    return duration.TotalSeconds < 30; // Should be reasonably fast
                }
                else
                {
                    Console.WriteLine($"? [Test 5] FAILED - Empty response");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? [Test 5] FAILED - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Utility: Call Local AI API dengan detailed logging
        /// </summary>
        private async Task<string> CallLocalAIAsync(string prompt)
        {
            try
            {
                var endpoint = $"{_baseUrl}/api/generate";
                
                var requestBody = new
                {
                    model = _model,
                    prompt = prompt,
                    stream = false,
                    temperature = 0.7,
                    top_p = 0.9,
                    top_k = 40
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                Console.WriteLine($"   ?? Sending request to: {endpoint}");
                Console.WriteLine($"   ?? Model: {_model}");
                Console.WriteLine($"   ??  Timeout: {_httpClient.Timeout.TotalSeconds}s");
                Console.WriteLine($"   ? Waiting for response...");

                var startTime = DateTime.Now;
                var response = await _httpClient.PostAsync(endpoint, content);
                var responseTime = DateTime.Now - startTime;

                Console.WriteLine($"   ? HTTP Status: {response.StatusCode}");
                Console.WriteLine($"   ??  Response time: {responseTime.TotalSeconds:F2}s");

                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"   ?? Response size: {responseText.Length} bytes");

                var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseText);
                var result = jsonResponse["response"]?.ToString() ?? string.Empty;

                return result.Trim();
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"   ? HTTP Error: {httpEx.Message}");
                throw new Exception($"HTTP Error calling Local AI: {httpEx.Message}", httpEx);
            }
            catch (TaskCanceledException timeoutEx)
            {
                Console.WriteLine($"   ? Timeout: Request took too long (>{_httpClient.Timeout.TotalSeconds}s)");
                throw new Exception($"Timeout calling Local AI: {timeoutEx.Message}", timeoutEx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ? Error: {ex.Message}");
                throw new Exception($"Error calling Local AI: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Run all tests
        /// </summary>
        public async Task<int> RunAllTestsAsync()
        {
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine("?   LOCAL AI SERVER - VALIDATION TEST SUITE          ?");
            Console.WriteLine("?   Base URL: http://10.0.12.54:11434               ?");
            Console.WriteLine("?   Model: qwen3:8b                                  ?");
            Console.WriteLine("??????????????????????????????????????????????????????\n");

            var passedTests = 0;
            var totalTests = 5;

            // Run tests
            if (await TestConnectionAsync()) passedTests++;
            if (await TestModelAvailabilityAsync()) passedTests++;
            if (await TestSimplePromptAsync()) passedTests++;
            if (await TestJIFASKnowledgeAsync()) passedTests++;
            if (await TestResponseSpeedAsync()) passedTests++;

            // Summary
            Console.WriteLine("\n??????????????????????????????????????????????????????");
            Console.WriteLine($"?   TEST SUMMARY: {passedTests}/{totalTests} PASSED                      ?");
            
            if (passedTests == totalTests)
            {
                Console.WriteLine("?   Status: ? ALL TESTS PASSED - READY FOR USE    ?");
            }
            else if (passedTests >= 3)
            {
                Console.WriteLine("?   Status: ??  PARTIAL - Some issues found        ?");
            }
            else
            {
                Console.WriteLine("?   Status: ? FAILED - Check configuration         ?");
            }
            
            Console.WriteLine("??????????????????????????????????????????????????????\n");

            return passedTests;
        }
    }

    /// <summary>
    /// Console app untuk run test
    /// Usage: dotnet run --project Jifas.Assistant.Tests
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var testHarness = new LocalAITestHarness();
            var passedTests = await testHarness.RunAllTestsAsync();
            
            Environment.Exit(passedTests == 5 ? 0 : 1);
        }
    }
}
