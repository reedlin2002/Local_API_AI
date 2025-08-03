using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalAIAgentAPI.Interfaces;

namespace LocalAIAgentAPI.Services
{
    public class AgentService : IAgentService
    {
        private readonly HttpClient _httpClient;

        public AgentService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken)
        {
            var requestPayload = new
            {
                model = "phi3",
                prompt = prompt,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestPayload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("http://localhost:11434/api/generate", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(rawResponse))
                return "(no response)";

            try
            {
                using var doc = JsonDocument.Parse(rawResponse);
                var root = doc.RootElement;

                if (root.TryGetProperty("response", out var resp))
                {
                    var text = resp.GetString() ?? "(empty)";
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
