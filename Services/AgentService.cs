using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Services;

public sealed record AgentAnswer(string Answer, string Model, IReadOnlyList<string> ToolsUsed);

public interface IAgentService
{
    Task<AgentAnswer> AskAsync(string prompt, byte[]? image, CancellationToken cancellationToken);
}

/// <summary>
/// A small tool-calling loop on Ollama's /api/chat. When an image is attached, the model may call
/// <c>classify_image</c> and <c>read_text_in_image</c>; for models without tool support the tools are
/// run up front and their output is placed in the prompt instead.
/// </summary>
public sealed class AgentService(
    IOllamaClient ollama,
    IImageClassifier classifier,
    IOcrService ocr,
    IOptions<OllamaOptions> options,
    ILogger<AgentService> logger) : IAgentService
{
    public const string ClassifyTool = "classify_image";
    public const string OcrTool = "read_text_in_image";

    private static readonly IReadOnlyList<ToolDefinition> Tools =
    [
        ToolDefinition.Create(ClassifyTool, "Identify the main object in the attached image. Returns the top ImageNet labels with probabilities."),
        ToolDefinition.Create(OcrTool, "Read printed text (English or Traditional Chinese) from the attached image using OCR."),
    ];

    private const string SystemPrompt =
        "You are a helpful local assistant. Answer in the same language as the user. " +
        "If an image is attached you cannot see it directly; use the provided tools to inspect it, then answer.";

    private const string GroundedSystemPrompt =
        "You are a helpful local assistant. Answer in the same language as the user. " +
        "The user's message includes classifier labels and OCR text extracted from their image; answer from that information directly.";

    public async Task<AgentAnswer> AskAsync(string prompt, byte[]? image, CancellationToken cancellationToken)
    {
        if (image is not null)
            ImageDecoding.EnsureValid(image);

        var o = options.Value;
        var toolsUsed = new List<string>();
        var messages = new List<ChatMessage>
        {
            new("system", SystemPrompt),
            new("user", image is null ? prompt : prompt + "\n\n(An image is attached.)"),
        };

        if (image is null)
        {
            var reply = await ollama.ChatAsync(o.AgentModel, messages, null, cancellationToken);
            return new AgentAnswer(reply.Content.Trim(), o.AgentModel, toolsUsed);
        }

        try
        {
            for (var step = 0; step < o.MaxAgentSteps; step++)
            {
                var reply = await ollama.ChatAsync(o.AgentModel, messages, Tools, cancellationToken);
                if (reply.ToolCalls is not { Count: > 0 })
                {
                    if (toolsUsed.Count > 0)
                        return new AgentAnswer(reply.Content.Trim(), o.AgentModel, toolsUsed);

                    // Small models often describe a tool call in prose instead of making it.
                    // An answer about an image that never looked at the image is useless, so ground it ourselves.
                    logger.LogInformation("Model {Model} answered without calling a tool; running tools up front", o.AgentModel);
                    break;
                }

                messages.Add(reply);
                foreach (var call in reply.ToolCalls)
                {
                    var name = call.Function.Name;
                    toolsUsed.Add(name);
                    messages.Add(new ChatMessage("tool", await RunToolAsync(name, image, cancellationToken), ToolName: name));
                }

                if (step == o.MaxAgentSteps - 1)
                {
                    // Out of steps: ask for a final answer without offering tools again.
                    var final = await ollama.ChatAsync(o.AgentModel, messages, null, cancellationToken);
                    return new AgentAnswer(final.Content.Trim(), o.AgentModel, toolsUsed);
                }
            }
        }
        catch (OllamaToolsNotSupportedException)
        {
            logger.LogInformation("Model {Model} has no tool support; running tools up front", o.AgentModel);
        }

        messages.RemoveRange(2, messages.Count - 2);
        toolsUsed.Clear();

        var context = new List<string>();
        foreach (var tool in Tools)
        {
            var name = tool.Function.Name;
            toolsUsed.Add(name);
            context.Add($"[{name}]\n{await RunToolAsync(name, image, cancellationToken)}");
        }

        messages[0] = new ChatMessage("system", GroundedSystemPrompt);
        messages[^1] = new ChatMessage("user",
            $"{prompt}\n\nInformation extracted from the attached image:\n{string.Join("\n\n", context)}");
        var answer = await ollama.ChatAsync(o.AgentModel, messages, null, cancellationToken);
        return new AgentAnswer(answer.Content.Trim(), o.AgentModel, toolsUsed);
    }

    private async Task<string> RunToolAsync(string name, byte[] image, CancellationToken cancellationToken)
    {
        switch (name)
        {
            case ClassifyTool:
                var result = await classifier.ClassifyAsync(image, 3, cancellationToken);
                return string.Join("\n", result.Top.Select(p => $"{p.Label}: {p.Confidence:P1}"));
            case OcrTool:
                var text = await ocr.RecognizeTextAsync(image, cancellationToken);
                return string.IsNullOrWhiteSpace(text) ? "(no text found)" : text;
            default:
                return $"Unknown tool '{name}'. Available tools: {ClassifyTool}, {OcrTool}.";
        }
    }
}
