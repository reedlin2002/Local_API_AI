using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Services;

public sealed record ImageDescription(string Description, string Model);

public interface IImageDescriber
{
    Task<ImageDescription> DescribeAsync(byte[] image, string? instruction, CancellationToken cancellationToken);
}

/// <summary>Sends the (downscaled) image to the multimodal model.</summary>
public sealed class ImageDescriber(IOllamaClient ollama, IOptions<OllamaOptions> options) : IImageDescriber
{
    public const string DefaultInstruction = "Describe this image in detail.";

    public async Task<ImageDescription> DescribeAsync(byte[] image, string? instruction, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var visionImage = ImageDecoding.ShrinkForVision(image, o.MaxImageSide);
        var prompt = string.IsNullOrWhiteSpace(instruction) ? DefaultInstruction : instruction;

        var description = await ollama.GenerateAsync(o.ForVision, prompt, [visionImage], cancellationToken);
        return new ImageDescription(description, o.ForVision);
    }
}
