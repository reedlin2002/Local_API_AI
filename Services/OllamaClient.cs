using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Services;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("images")] IReadOnlyList<string>? Images = null)
{
    public static ChatMessage User(string content, byte[]? image = null) =>
        new("user", content, image is null ? null : [Convert.ToBase64String(image)]);
}

public interface IOllamaClient
{
    Task<string> GenerateAsync(string model, string prompt, IReadOnlyList<byte[]>? images, CancellationToken cancellationToken);

    Task<string> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);

    /// <summary>Asks Ollama to load the model into memory without generating anything.</summary>
    Task WarmUpAsync(string model, CancellationToken cancellationToken);
}

/// <summary>Typed HttpClient for the Ollama REST API (base address and timeout come from <see cref="OllamaOptions"/>).</summary>
public sealed class OllamaClient(HttpClient http, IOptions<OllamaOptions> options) : IOllamaClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<string> GenerateAsync(string model, string prompt, IReadOnlyList<byte[]>? images, CancellationToken cancellationToken)
    {
        var request = new
        {
            model,
            prompt,
            stream = false,
            think = false,
            keep_alive = options.Value.KeepAlive,
            images = images?.Select(Convert.ToBase64String).ToArray(),
            options = Sampling(hasImage: images is { Count: > 0 }),
        };

        using var doc = await PostAsync("api/generate", model, request, cancellationToken);
        return doc.RootElement.TryGetProperty("response", out var response)
            ? (response.GetString() ?? string.Empty).Trim()
            : string.Empty;
    }

    public async Task<string> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var request = new
        {
            model,
            messages,
            stream = false,
            think = false,
            keep_alive = options.Value.KeepAlive,
            options = Sampling(hasImage: messages.Any(m => m.Images is { Count: > 0 })),
        };

        using var doc = await PostAsync("api/chat", model, request, cancellationToken);
        return doc.RootElement.TryGetProperty("message", out var message)
               && message.TryGetProperty("content", out var content)
            ? (content.GetString() ?? string.Empty).Trim()
            : string.Empty;
    }

    public async Task WarmUpAsync(string model, CancellationToken cancellationToken)
    {
        using var _ = await PostAsync("api/generate", model, new { model, keep_alive = options.Value.KeepAlive }, cancellationToken);
    }

    /// <summary>
    /// Qwen3.5 non-thinking settings from the model card. Thinking stays off: the 2B model is prone to
    /// thinking loops and it would multiply latency on a laptop GPU.
    /// </summary>
    private object Sampling(bool hasImage) => hasImage
        ? new { temperature = 0.7, top_p = 0.8, top_k = 20, presence_penalty = 1.5, num_ctx = options.Value.ContextLength }
        : new { temperature = 1.0, top_p = 1.0, top_k = 20, presence_penalty = 2.0, num_ctx = options.Value.ContextLength };

    private async Task<JsonDocument> PostAsync(string path, string model, object payload, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(path, payload, Json, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new UpstreamUnavailableException(
                $"無法連線到 Ollama（{http.BaseAddress}）。請確認已安裝並執行 `ollama serve`。", e);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(body);

            var error = ReadError(body);
            if (response.StatusCode == HttpStatusCode.NotFound && error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                throw new OllamaModelNotFoundException(model);

            throw new UpstreamUnavailableException($"Ollama 回應 {(int)response.StatusCode}：{error}");
        }
    }

    private static string ReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? body;
        }
        catch (JsonException)
        {
        }

        return body;
    }
}
