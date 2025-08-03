using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocalAIAgentAPI.Services
{
    public class OllamaTextGenerationService : ITextGenerationService
    {
        private readonly HttpClient _httpClient;

        public OllamaTextGenerationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<object> ProcessAsync(string prompt, CancellationToken cancellationToken)
        {
            var requestPayload = new
            {
                model = "llama2",
                prompt = prompt,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("http://localhost:11434/api/generate", content, cancellationToken);

            response.EnsureSuccessStatusCode();

            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            Console.WriteLine("Ollama raw response: " + rawResponse);

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return "(no response)";
            }

            try
            {
                using var doc = JsonDocument.Parse(rawResponse);
                var root = doc.RootElement;

                // 只取 response 欄位的純文字
                if (root.TryGetProperty("response", out var resp))
                {
                    var text = resp.GetString() ?? "(empty)";
                    // 移除首尾空白、換行符號等，讓輸出更乾淨
                    return text.Trim();
                }
                else
                {
                    return root.ToString();
                }
            }
            catch (JsonException)
            {
                return rawResponse;
            }
        }
    }
}
