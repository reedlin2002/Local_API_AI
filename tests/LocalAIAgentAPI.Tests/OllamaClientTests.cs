using System.Net;
using System.Text;
using System.Text.Json;
using LocalAIAgentAPI.Infrastructure;
using LocalAIAgentAPI.Services;

namespace LocalAIAgentAPI.Tests;

public sealed class OllamaClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static OllamaClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test/") }, Samples.OllamaOptions());

    [Fact]
    public async Task Generate_disables_thinking_keeps_model_loaded_and_sends_base64_images()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"response":"  hello  ","done":true}"""));

        var text = await Client(handler).GenerateAsync("qwen3.5:2b", "describe", [[1, 2, 3]], default);

        Assert.Equal("hello", text);
        Assert.Equal("/api/generate", handler.LastPath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("30m", root.GetProperty("keep_alive").GetString());
        Assert.Equal("AQID", root.GetProperty("images")[0].GetString());
        Assert.Equal(4096, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(0.7, root.GetProperty("options").GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Text_requests_use_text_sampling_and_omit_images()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"response":"ok"}"""));

        await Client(handler).GenerateAsync("qwen3.5:2b", "hi", null, default);

        Assert.DoesNotContain("images", handler.LastBody);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(2.0, body.RootElement.GetProperty("options").GetProperty("presence_penalty").GetDouble());
    }

    [Fact]
    public async Task Chat_puts_images_on_the_message_and_returns_the_content()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"message":{"role":"assistant","content":" 一隻橘貓 "},"done":true}"""));

        var answer = await Client(handler).ChatAsync("qwen3.5:2b",
            [new ChatMessage("system", "s"), ChatMessage.User("這是什麼？", [9, 9])], default);

        Assert.Equal("一隻橘貓", answer);
        Assert.Equal("/api/chat", handler.LastPath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var messages = body.RootElement.GetProperty("messages");
        Assert.False(messages[0].TryGetProperty("images", out _));
        Assert.Equal("CQk=", messages[1].GetProperty("images")[0].GetString());
    }

    [Fact]
    public async Task Warm_up_loads_the_model_without_a_prompt()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"done":true}"""));

        await Client(handler).WarmUpAsync("qwen3.5:2b", default);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.False(body.RootElement.TryGetProperty("prompt", out _));
        Assert.Equal("30m", body.RootElement.GetProperty("keep_alive").GetString());
    }

    [Fact]
    public async Task Missing_model_maps_to_a_pull_hint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, """{"error":"model 'qwen3.5:2b' not found"}"""));

        var e = await Assert.ThrowsAsync<OllamaModelNotFoundException>(() =>
            Client(handler).GenerateAsync("qwen3.5:2b", "x", null, default));
        Assert.Contains("ollama pull qwen3.5:2b", e.Message);
    }

    [Fact]
    public async Task Connection_failure_maps_to_upstream_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var e = await Assert.ThrowsAsync<UpstreamUnavailableException>(() =>
            Client(handler).GenerateAsync("qwen3.5:2b", "x", null, default));
        Assert.Contains("ollama serve", e.Message);
    }
}
