using System.Diagnostics;
using LocalAIAgentAPI.Infrastructure;
using LocalAIAgentAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AIController(
    IImageClassifier classifier,
    IOcrService ocr,
    IOllamaClient ollama,
    IImageDescriber describer,
    IAgentService agent,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<AIController> logger) : ControllerBase
{
    private const int TopK = 5;

    /// <summary>Generic entry point. model = imageclassifier | textgeneration | agent.</summary>
    [HttpPost]
    public async Task<IActionResult> ProcessRequest(
        [FromForm] string model,
        IFormFile? file,
        [FromForm] string? prompt,
        [FromQuery] string outputFormat = "json",
        CancellationToken cancellationToken = default)
    {
        switch (model.Trim().ToLowerInvariant())
        {
            case "imageclassifier":
                if (file is null)
                    return BadInput("File is required for image classification.");
                return await ClassifyAsync(file, outputFormat, cancellationToken);

            case "textgeneration":
                if (string.IsNullOrWhiteSpace(prompt))
                    return BadInput("Prompt is required for text generation.");
                var textModel = ollamaOptions.Value.TextModel;
                var text = await Timed("textgeneration", () => ollama.GenerateAsync(textModel, prompt, null, cancellationToken));
                return Respond(outputFormat, new { response = text, model = textModel }, text);

            case "agent":
                if (string.IsNullOrWhiteSpace(prompt))
                    return BadInput("Prompt is required for agent.");
                return await AskAsync(prompt, file, outputFormat, cancellationToken);

            default:
                return BadInput($"Unsupported model '{model}'. Use imageclassifier, textgeneration or agent.");
        }
    }

    [HttpPost("describe-image")]
    public async Task<IActionResult> DescribeImage(
        IFormFile file,
        [FromQuery] string outputFormat = "json",
        CancellationToken cancellationToken = default)
    {
        var image = await ReadAsync(file, cancellationToken);
        var result = await Timed("describe-image", () => describer.DescribeAsync(image, cancellationToken));
        return Respond(outputFormat, result, result.Description);
    }

    [HttpPost("batch-classify")]
    public async Task<IActionResult> BatchClassify(
        List<IFormFile> files,
        [FromQuery] string outputFormat = "json",
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
            return BadInput("No files provided.");

        var results = new List<BatchItem>(files.Count);
        foreach (var file in files)
        {
            try
            {
                var result = await classifier.ClassifyAsync(await ReadAsync(file, cancellationToken), 1, cancellationToken);
                results.Add(new BatchItem(file.FileName, result.Label, result.Confidence, null));
            }
            catch (InvalidImageException e)
            {
                results.Add(new BatchItem(file.FileName, null, null, e.Message));
            }
        }

        var text = string.Join("\n", results.Select(r => r.Error is null
            ? $"{r.File}: {r.Label} ({r.Confidence:P1})"
            : $"{r.File}: ERROR {r.Error}"));
        return Respond(outputFormat, results, text);
    }

    [HttpPost("ocr")]
    public async Task<IActionResult> RecognizeText(
        IFormFile file,
        [FromQuery] string outputFormat = "json",
        CancellationToken cancellationToken = default)
    {
        var image = await ReadAsync(file, cancellationToken);
        var text = await Timed("ocr", () => ocr.RecognizeTextAsync(image, cancellationToken));
        return Respond(outputFormat, new { text }, text);
    }

    /// <summary>Ask the agent. Attach an optional image and it can call OCR / classification as tools.</summary>
    [HttpPost("ask")]
    public Task<IActionResult> AskAgent(
        [FromForm] string prompt,
        IFormFile? file,
        [FromQuery] string outputFormat = "json",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return Task.FromResult<IActionResult>(BadInput("Prompt is required."));
        return AskAsync(prompt, file, outputFormat, cancellationToken);
    }

    private async Task<IActionResult> ClassifyAsync(IFormFile file, string outputFormat, CancellationToken cancellationToken)
    {
        var image = await ReadAsync(file, cancellationToken);
        var result = await Timed("imageclassifier", () => classifier.ClassifyAsync(image, TopK, cancellationToken));
        var text = string.Join("\n", result.Top.Select((p, i) => $"{i + 1}. {p.Label} ({p.Confidence:P1})"));
        return Respond(outputFormat, result, text);
    }

    private async Task<IActionResult> AskAsync(string prompt, IFormFile? file, string outputFormat, CancellationToken cancellationToken)
    {
        var image = file is null ? null : await ReadAsync(file, cancellationToken);
        var result = await Timed("agent", () => agent.AskAsync(prompt, image, cancellationToken));
        return Respond(outputFormat, result, result.Answer);
    }

    private IActionResult Respond(string? outputFormat, object json, string text) =>
        string.Equals(outputFormat, "text", StringComparison.OrdinalIgnoreCase)
            ? Content(string.IsNullOrEmpty(text) ? "(empty)" : text, "text/plain; charset=utf-8")
            : Ok(json);

    private ObjectResult BadInput(string detail) =>
        Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Bad request");

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            throw new InvalidImageException("上傳的檔案是空的。");

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Logs which capability ran and how long it took. Prompts and results are never logged.</summary>
    private async Task<T> Timed<T>(string capability, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        logger.LogInformation("{Capability} completed in {ElapsedMs} ms", capability, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private sealed record BatchItem(string File, string? Label, float? Confidence, string? Error);
}
