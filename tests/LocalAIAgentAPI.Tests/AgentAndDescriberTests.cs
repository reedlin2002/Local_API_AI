using System.Text.Json;
using LocalAIAgentAPI.Infrastructure;
using LocalAIAgentAPI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalAIAgentAPI.Tests;

public sealed class AgentServiceTests
{
    private readonly FakeOllama _ollama = new();
    private readonly FakeClassifier _classifier = new();
    private readonly FakeOcr _ocr = new();
    private readonly byte[] _image = Samples.Read("cat.jpg");

    private AgentService CreateAgent() =>
        new(_ollama, _classifier, _ocr, Samples.OllamaOptions(), NullLogger<AgentService>.Instance);

    [Fact]
    public async Task Without_an_image_it_is_a_single_chat_call_without_tools()
    {
        _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", " 42 "));

        var answer = await CreateAgent().AskAsync("meaning of life?", null, default);

        Assert.Equal("42", answer.Answer);
        Assert.Empty(answer.ToolsUsed);
        var call = Assert.Single(_ollama.ChatCalls);
        Assert.False(call.HadTools);
        Assert.Equal("mistral", call.Model);
    }

    [Fact]
    public async Task Runs_the_tool_the_model_asks_for_and_returns_its_final_answer()
    {
        _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", "",
            [new ToolCall(new FunctionCall(AgentService.ClassifyTool, JsonDocument.Parse("{}").RootElement))]));
        _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", "It is a tabby cat."));

        var answer = await CreateAgent().AskAsync("What animal is this?", _image, default);

        Assert.Equal("It is a tabby cat.", answer.Answer);
        Assert.Equal([AgentService.ClassifyTool], answer.ToolsUsed);
        Assert.Equal(1, _classifier.Calls);
        Assert.Equal(0, _ocr.Calls);

        var secondCall = _ollama.ChatCalls[1].Messages;
        var toolMessage = Assert.Single(secondCall, m => m.Role == "tool");
        Assert.Equal(AgentService.ClassifyTool, toolMessage.ToolName);
        Assert.Contains("tabby", toolMessage.Content);
    }

    [Fact]
    public async Task Falls_back_to_running_tools_up_front_when_model_has_no_tool_support()
    {
        _ollama.ChatReplies.Enqueue(() => throw new OllamaToolsNotSupportedException("phi3"));
        _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", "The sign says hello."));

        var answer = await CreateAgent().AskAsync("What does it say?", _image, default);

        Assert.Equal("The sign says hello.", answer.Answer);
        Assert.Equal(2, answer.ToolsUsed.Count);
        var finalPrompt = _ollama.ChatCalls[1].Messages.Last(m => m.Role == "user").Content;
        Assert.Contains("HELLO FROM OCR", finalPrompt);
        Assert.Contains("tabby", finalPrompt);
        Assert.False(_ollama.ChatCalls[1].HadTools);
    }

    [Fact]
    public async Task Grounds_the_answer_itself_when_the_model_ignores_the_tools()
    {
        _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", "You can use the `classify_image` function..."));
        _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", "A tabby cat."));

        var answer = await CreateAgent().AskAsync("What animal is this?", _image, default);

        Assert.Equal("A tabby cat.", answer.Answer);
        Assert.Equal(2, answer.ToolsUsed.Count);
        var finalCall = _ollama.ChatCalls[1];
        Assert.False(finalCall.HadTools);
        Assert.Equal(2, finalCall.Messages.Count);
        Assert.Contains("tabby", finalCall.Messages[^1].Content);
    }

    [Fact]
    public async Task Stops_after_max_steps_and_asks_for_a_final_answer()
    {
        for (var i = 0; i < 10; i++)
            _ollama.ChatReplies.Enqueue(() => new ChatMessage("assistant", "",
                [new ToolCall(new FunctionCall(AgentService.OcrTool))]));
        var agent = new AgentService(_ollama, _classifier, _ocr,
            Samples.OllamaOptions(o => o.MaxAgentSteps = 2), NullLogger<AgentService>.Instance);

        await agent.AskAsync("loop forever", _image, default);

        Assert.Equal(3, _ollama.ChatCalls.Count);
        Assert.False(_ollama.ChatCalls[^1].HadTools);
    }
}

public sealed class ImageDescriberTests
{
    private readonly FakeOllama _ollama = new();
    private readonly FakeClassifier _classifier = new();
    private readonly byte[] _image = Samples.Read("cat.jpg");

    private ImageDescriber CreateDescriber() =>
        new(_ollama, _classifier, Samples.OllamaOptions(), NullLogger<ImageDescriber>.Instance);

    [Fact]
    public async Task Sends_the_image_to_the_vision_model()
    {
        var result = await CreateDescriber().DescribeAsync(_image, default);

        Assert.Equal(ImageDescriber.VisionMethod, result.Method);
        var call = Assert.Single(_ollama.GenerateCalls);
        Assert.Equal("llava", call.Model);
        Assert.Equal(1, call.Images);
        Assert.Equal(0, _classifier.Calls);
    }

    [Fact]
    public async Task Falls_back_to_labels_when_vision_model_is_missing()
    {
        _ollama.OnGenerate = (model, prompt) => model == "llava"
            ? throw new OllamaModelNotFoundException(model)
            : $"described: {prompt}";

        var result = await CreateDescriber().DescribeAsync(_image, default);

        Assert.Equal(ImageDescriber.LabelFallbackMethod, result.Method);
        Assert.Equal("llama2", result.Model);
        Assert.Contains("tabby", result.Description);
    }

    [Fact]
    public async Task Rejects_non_images_before_calling_ollama()
    {
        await Assert.ThrowsAsync<InvalidImageException>(() =>
            CreateDescriber().DescribeAsync("plain text"u8.ToArray(), default));

        Assert.Empty(_ollama.GenerateCalls);
    }
}
