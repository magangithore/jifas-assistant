using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Jifas.Assistant.Tests
{
    /// <summary>
    /// Direct Local AI Test - Simple console app untuk verify request masuk ke server
    /// </summary>
    class DirectLocalAITest
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("?????????????????????????????????????????????????????????????????");
            Console.WriteLine("?       DIRECT LOCAL AI INVOCATION TEST                         ?");
            Console.WriteLine("?       Verify: Request masuk ke server Ollama                  ?");
            Console.WriteLine("?       Server: http://10.0.12.54:11434                         ?");
            Console.WriteLine("?       Model: qwen3:8b                                         ?");
            Console.WriteLine("?????????????????????????????????????????????????????????????????\n");

            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var baseUrl = "http://10.0.12.54:11434";
            var model = "qwen3:8b";

            try
            {
                // Test 1: Verify server reachable
                Console.WriteLine("?? [Step 1] Checking server connectivity...");
                try
                {
                    var tagsResponse = await httpClient.GetAsync($"{baseUrl}/api/tags");
                    if (tagsResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("   ? Server is reachable (HTTP 200)");
                    }
                    else
                    {
                        Console.WriteLine($"   ? Server returned: {tagsResponse.StatusCode}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ? Cannot reach server: {ex.Message}");
                    return;
                }

                // Test 2: Check if model available
                Console.WriteLine("\n?? [Step 2] Checking if model 'qwen3:8b' is available...");
                var tagsContent = await httpClient.GetAsync($"{baseUrl}/api/tags");
                var tagsText = await tagsContent.Content.ReadAsStringAsync();
                if (tagsText.Contains(model))
                {
                    Console.WriteLine($"   ? Model '{model}' found on server");
                }
                else
                {
                    Console.WriteLine($"   ??  Model '{model}' not found. Available models:");
                    var models = JsonConvert.DeserializeObject<dynamic>(tagsText);
                    foreach (var m in models["models"] ?? new object[] { })
                    {
                        Console.WriteLine($"      - {m["name"]}");
                    }
                    return;
                }

                // Test 3: Make actual invocation
                Console.WriteLine("\n?? [Step 3] Making actual API invocation...");
                Console.WriteLine("   ?? Prompt: \"Apa itu JIFAS? Jelaskan singkat.\"");
                Console.WriteLine("   ? Sending request...\n");

                var requestBody = new
                {
                    model = model,
                    prompt = "Apa itu JIFAS? Jelaskan dalam 1-2 kalimat saja.",
                    stream = false,
                    temperature = 0.7,
                    top_p = 0.9,
                    top_k = 40
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var stringContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                Console.WriteLine($"   ?? Endpoint: POST {baseUrl}/api/generate");
                Console.WriteLine($"   ?? Request body size: {jsonContent.Length} bytes");
                Console.WriteLine($"   ??  HTTP Timeout: 60 seconds\n");

                var startTime = DateTime.Now;
                var response = await httpClient.PostAsync($"{baseUrl}/api/generate", stringContent);
                var elapsedTime = DateTime.Now - startTime;

                Console.WriteLine($"   ? Response received!");
                Console.WriteLine($"   ?? HTTP Status Code: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"   ??  Total time: {elapsedTime.TotalSeconds:F2} seconds");
                Console.WriteLine($"   ?? Content-Length: {response.Content.Headers.ContentLength ?? 0} bytes\n");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"   ? ERROR: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"   Response: {errorContent}");
                    return;
                }

                var responseText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"   Raw response size: {responseText.Length} bytes\n");

                try
                {
                    var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseText);
                    var answer = jsonResponse["response"]?.ToString() ?? "[No response]";
                    var totalDuration = jsonResponse["total_duration"]?.ToString() ?? "N/A";
                    var evalCount = jsonResponse["eval_count"]?.ToString() ?? "N/A";

                    Console.WriteLine("?????????????????????????????????????????????????????????");
                    Console.WriteLine("?? AI RESPONSE:");
                    Console.WriteLine("?????????????????????????????????????????????????????????");
                    Console.WriteLine(answer);
                    Console.WriteLine("?????????????????????????????????????????????????????????\n");

                    Console.WriteLine("?? Performance Metrics:");
                    Console.WriteLine($"   Server processing time: {(long.Parse(totalDuration) / 1000000000.0):F2}s");
                    Console.WriteLine($"   Tokens generated: {evalCount}");
                    Console.WriteLine($"   Total request time: {elapsedTime.TotalSeconds:F2}s\n");

                    Console.WriteLine("? SUCCESS - Request successfully made to Ollama server!");
                    Console.WriteLine("? Response successfully received and parsed!");
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"   ??  Could not parse response: {parseEx.Message}");
                    Console.WriteLine($"   Raw response:\n{responseText}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n? ERROR: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }

            Console.WriteLine("\n? Test completed. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
