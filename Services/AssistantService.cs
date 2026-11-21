using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Services;

public sealed record AssistantAnswer(string Answer, string Model, IReadOnlyList<string> Steps);

public interface IAssistantService
{
    Task<AssistantAnswer> AskAsync(string prompt, byte[]? image, CancellationToken cancellationToken);
}

/// <summary>
/// Answers questions, optionally about an image. The multimodal model sees the image itself; PP-OCR's
/// reading of any text in it is added to the prompt, because a 2B vision model guesses at small or
/// dense text while the OCR model rarely does.
/// </summary>
public sealed class AssistantService(IOllamaClient ollama, IOcrService ocr, IOptions<OllamaOptions> options) : IAssistantService
{
    public const string OcrStep = "ocr";
    public const string VisionStep = "vision";

    private const string SystemPrompt =
        "你是在使用者電腦上執行的本機助理。使用與使用者相同的語言回答，簡潔、準確；不確定的事情就說不確定，不要編造。";

    public async Task<AssistantAnswer> AskAsync(string prompt, byte[]? image, CancellationToken cancellationToken)
    {
        var model = options.Value.ForAssistant;
        var system = new ChatMessage("system", SystemPrompt);

        if (image is null)
        {
            var reply = await ollama.ChatAsync(model, [system, ChatMessage.User(prompt)], cancellationToken);
            return new AssistantAnswer(reply, model, []);
        }

        ImageDecoding.EnsureValid(image);
        var ocrTask = ocr.RecognizeTextAsync(image, cancellationToken);
        var visionImage = ImageDecoding.ShrinkForVision(image, options.Value.MaxImageSide);
        var text = await ocrTask;

        var grounded = text.Length == 0
            ? $"{prompt}\n\n（文字辨識沒有在圖片中找到文字。）"
            : $"{prompt}\n\n以下是文字辨識從圖片讀出的內容，引用圖中文字時以此為準：\n{text}";

        var answer = await ollama.ChatAsync(model, [system, ChatMessage.User(grounded, visionImage)], cancellationToken);
        return new AssistantAnswer(answer, model, [OcrStep, VisionStep]);
    }
}
