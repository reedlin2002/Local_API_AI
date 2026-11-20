using System.Net;
using System.Text;
using System.Text.Json;
using LocalAIAgentAPI.Infrastructure;
using LocalAIAgentAPI.Services;

namespace LocalAIAgentAPI.Tests;

public sealed class OllamaClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, LastBody ?? "");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static OllamaClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test/") });

    [Fact]
    public async Task Generate_posts_model_prompt_and_base64_images()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"response":"  hello  ","done":true}"""));

        var text = await Client(handler).GenerateAsync("llava", "describe", [[1, 2, 3]], default);

        Assert.Equal("hello", text);
        Assert.Equal("/api/generate", handler.LastPath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("llava", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("AQID", body.RootElement.GetProperty("images")[0].GetString());
    }

    [Fact]
    public async Task Generate_without_images_omits_the_field()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"response":"ok"}"""));

        await Client(handler).GenerateAsync("llama2", "hi", null, default);

        Assert.DoesNotContain("images", handler.LastBody);
    }

    [Fact]
    public async Task Chat_parses_tool_calls_and_sends_tools_in_ollama_format()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"classify_image","arguments":{}}}]},"done":true}"""));

        var reply = await Client(handler).ChatAsync("mistral", [new ChatMessage("user", "hi")],
            [ToolDefinition.Create("classify_image", "desc")], default);

        Assert.Equal("classify_image", Assert.Single(reply.ToolCalls!).Function.Name);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var tool = body.RootElement.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("object", tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
        Assert.False(body.RootElement.GetProperty("messages")[0].TryGetProperty("tool_calls", out _));
    }

    [Fact]
    public async Task Missing_model_maps_to_model_not_found()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.NotFound, """{"error":"model 'llava' not found"}"""));

        var e = await Assert.ThrowsAsync<OllamaModelNotFoundException>(() =>
            Client(handler).GenerateAsync("llava", "x", null, default));
        Assert.Contains("ollama pull llava", e.Message);
    }

    [Fact]
    public async Task Model_without_tool_support_is_detected()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.BadRequest,
            """{"error":"registry.ollama.ai/library/phi3:latest does not support tools"}"""));

        await Assert.ThrowsAsync<OllamaToolsNotSupportedException>(() =>
            Client(handler).ChatAsync("phi3", [new ChatMessage("user", "hi")], [ToolDefinition.Create("t", "d")], default));
    }

    [Fact]
    public async Task Connection_failure_maps_to_upstream_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));

        var e = await Assert.ThrowsAsync<UpstreamUnavailableException>(() =>
            Client(handler).GenerateAsync("llama2", "x", null, default));
        Assert.Contains("ollama serve", e.Message);
    }
}
