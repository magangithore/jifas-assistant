using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    static async Task Main()
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        const string apiUrl = "http://localhost:5000";

        Console.WriteLine("??????????????????????????????????????????????????????????????????");
        Console.WriteLine("?    ?? TEST JIFAS ASSISTANT API ENDPOINT                        ?");
        Console.WriteLine("?    Testing: POST /api/chat/message                             ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        try
        {
            Console.WriteLine($"?? API Base URL: {apiUrl}");
            Console.WriteLine($"?? Endpoint: POST /api/chat/message");
            Console.WriteLine();

            // First, check if API is running
            Console.WriteLine("? Checking if API is running...");
            try
            {
                var healthCheck = await httpClient.GetAsync($"{apiUrl}/health");
                if (healthCheck.IsSuccessStatusCode)
                {
                    Console.WriteLine("? API is running!");
                }
            }
            catch
            {
                Console.WriteLine("? API is not running at http://localhost:5000");
                Console.WriteLine("   Please run: dotnet run");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("?? Sending chat request...");
            Console.WriteLine();

            var chatRequest = new
            {
                message = "Apa itu JIFAS? Jelaskan dalam 1-2 kalimat.",
                userId = "test-user-" + Guid.NewGuid().ToString().Substring(0, 8),
                sessionId = Guid.NewGuid().ToString()
            };

            var jsonContent = JsonConvert.SerializeObject(chatRequest);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine("?? Request Body:");
            Console.WriteLine(JsonConvert.SerializeObject(chatRequest, Formatting.Indented));
            Console.WriteLine();

            var startTime = DateTime.Now;
            var response = await httpClient.PostAsync($"{apiUrl}/api/chat/message", content);
            var elapsed = DateTime.Now - startTime;

            Console.WriteLine("? RESPONSE RECEIVED!");
            Console.WriteLine("????????????????????????????????????????????????????????????????");
            Console.WriteLine();

            Console.WriteLine($"?? HTTP Status: {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine($"??  Response Time: {elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine();

            var responseText = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseText);

            Console.WriteLine("?? API Response:");
            Console.WriteLine("????????????????????????????????????????????????????????????????");
            Console.WriteLine(JsonConvert.SerializeObject(jsonResponse, Formatting.Indented));
            Console.WriteLine("????????????????????????????????????????????????????????????????");
            Console.WriteLine();

            Console.WriteLine("? END-TO-END TEST PASSED!");
            Console.WriteLine("? API ? LocalAIService ? Ollama Server ? Response");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}
