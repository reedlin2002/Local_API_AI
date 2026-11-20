using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAIAgentAPI.Infrastructure;

namespace LocalAIAgentAPI.Services;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<ToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_name")] string? ToolName = null);

public sealed record ToolCall([property: JsonPropertyName("function")] FunctionCall Function);

public sealed record FunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonElement? Arguments = null);

public sealed record ToolDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] FunctionDefinition Function)
{
    public static ToolDefinition Create(string name, string description) =>
        new("function", new FunctionDefinition(name, description, new { type = "object", properties = new { } }));
}

public sealed record FunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] object Parameters);

public interface IOllamaClient
{
    Task<string> GenerateAsync(string model, string prompt, IReadOnlyList<byte[]>? images, CancellationToken cancellationToken);

    Task<ChatMessage> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools, CancellationToken cancellationToken);
}

/// <summary>Typed HttpClient for the Ollama REST API (base address and timeout come from <see cref="OllamaOptions"/>).</summary>
public sealed class OllamaClient(HttpClient http) : IOllamaClient
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
            images = images?.Select(Convert.ToBase64String).ToArray(),
        };

        using var doc = await PostAsync("api/generate", model, request, cancellationToken);
        return doc.RootElement.TryGetProperty("response", out var response)
            ? (response.GetString() ?? string.Empty).Trim()
            : string.Empty;
    }

    public async Task<ChatMessage> ChatAsync(string model, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools, CancellationToken cancellationToken)
    {
        var request = new { model, messages, tools, stream = false };

        using var doc = await PostAsync("api/chat", model, request, cancellationToken);
        var message = doc.RootElement.TryGetProperty("message", out var m)
            ? m.Deserialize<ChatMessage>(Json)
            : null;
        return message ?? new ChatMessage("assistant", string.Empty);
    }

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
            if (error.Contains("does not support tools", StringComparison.OrdinalIgnoreCase))
                throw new OllamaToolsNotSupportedException(model);

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
