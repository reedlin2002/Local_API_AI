using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Services;

public sealed record ImageDescription(string Description, string Method, string Model);

public interface IImageDescriber
{
    Task<ImageDescription> DescribeAsync(byte[] image, CancellationToken cancellationToken);
}

/// <summary>
/// Sends the image itself to an Ollama vision model. If that model is not installed, falls back to
/// describing the classifier's top labels with the text model and says so in <see cref="ImageDescription.Method"/>.
/// </summary>
public sealed class ImageDescriber(
    IOllamaClient ollama,
    IImageClassifier classifier,
    IOptions<OllamaOptions> options,
    ILogger<ImageDescriber> logger) : IImageDescriber
{
    public const string VisionMethod = "vision";
    public const string LabelFallbackMethod = "label-fallback";

    public async Task<ImageDescription> DescribeAsync(byte[] image, CancellationToken cancellationToken)
    {
        ImageDecoding.EnsureValid(image);
        var o = options.Value;

        try
        {
            var text = await ollama.GenerateAsync(o.VisionModel, "Describe this image in detail.", [image], cancellationToken);
            return new ImageDescription(text, VisionMethod, o.VisionModel);
        }
        catch (OllamaModelNotFoundException)
        {
            logger.LogWarning("Vision model {Model} is not installed; falling back to label-based description", o.VisionModel);
        }

        var classification = await classifier.ClassifyAsync(image, 3, cancellationToken);
        var labels = string.Join("; ", classification.Top.Select(p => $"{p.Label} ({p.Confidence:P0})"));
        var prompt =
            $"An image classifier looked at a photo and reported these labels with probabilities: {labels}. " +
            "In 2-3 sentences, describe what the photo most likely shows. Only use what the labels support and do not invent details.";

        var description = await ollama.GenerateAsync(o.TextModel, prompt, null, cancellationToken);
        return new ImageDescription(description, LabelFallbackMethod, o.TextModel);
    }
}
