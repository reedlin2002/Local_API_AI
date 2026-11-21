using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace LocalAIAgentAPI.Services;

public sealed record Prediction(string Label, float Confidence);

public sealed record ClassificationResult(string Label, float Confidence, IReadOnlyList<Prediction> Top);

public interface IImageClassifier
{
    Task<ClassificationResult> ClassifyAsync(byte[] image, int topK, CancellationToken cancellationToken);
}

/// <summary>
/// ResNet50 v2 (ONNX model zoo). The session is loaded once and shared:
/// <see cref="InferenceSession.Run(IReadOnlyCollection{NamedOnnxValue})"/> is thread-safe.
/// </summary>
public sealed class OnnxImageClassifier : IImageClassifier, IDisposable
{
    private const int CropSize = 224;
    private const int ResizeShortSide = 256;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string[] _labels;

    public OnnxImageClassifier(IOptions<InferenceOptions> options, IHostEnvironment environment)
    {
        var o = options.Value;
        _session = new InferenceSession(ContentPath.Resolve(environment, o.ImageClassifierModelPath));
        _inputName = _session.InputMetadata.Keys.First();
        _labels = File.ReadAllLines(ContentPath.Resolve(environment, o.ImageNetLabelsPath))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        var classCount = _session.OutputMetadata.Values.First().Dimensions.Last();
        if (_labels.Length != classCount)
        {
            _session.Dispose();
            throw new InvalidOperationException(
                $"Label file has {_labels.Length} entries but the model outputs {classCount} classes.");
        }
    }

    public Task<ClassificationResult> ClassifyAsync(byte[] image, int topK, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var tensor = Preprocess(image);
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
            var probabilities = Softmax(results[0].AsEnumerable<float>().ToArray());

            var top = probabilities
                .Select((p, i) => new Prediction(_labels[i], p))
                .OrderByDescending(p => p.Confidence)
                .Take(Math.Clamp(topK, 1, _labels.Length))
                .ToList();

            return new ClassificationResult(top[0].Label, top[0].Confidence, top);
        }, cancellationToken);

    /// <summary>Resize the short side to 256, center-crop 224x224, normalize with ImageNet mean/std (NCHW).</summary>
    internal static DenseTensor<float> Preprocess(byte[] imageBytes)
    {
        using var source = ImageDecoding.Decode(imageBytes);

        var scale = (float)ResizeShortSide / Math.Min(source.Width, source.Height);
        var width = Math.Max(CropSize, (int)MathF.Round(source.Width * scale));
        var height = Math.Max(CropSize, (int)MathF.Round(source.Height * scale));

        // Drawing the resized image centered on a 224x224 canvas is the resize + center crop in one step.
        using var cropped = new SKBitmap(new SKImageInfo(CropSize, CropSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(cropped))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.White);
            var destination = SKRect.Create((CropSize - width) / 2f, (CropSize - height) / 2f, width, height);
            canvas.DrawImage(image, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var tensor = new DenseTensor<float>([1, 3, CropSize, CropSize]);
        var data = tensor.Buffer.Span;
        var rgba = cropped.GetPixelSpan();
        const int plane = CropSize * CropSize;

        for (var i = 0; i < plane; i++)
        {
            data[i] = (rgba[i * 4] / 255f - Mean[0]) / Std[0];
            data[plane + i] = (rgba[i * 4 + 1] / 255f - Mean[1]) / Std[1];
            data[2 * plane + i] = (rgba[i * 4 + 2] / 255f - Mean[2]) / Std[2];
        }

        return tensor;
    }

    internal static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exps = logits.Select(v => Math.Exp(v - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(e => (float)(e / sum)).ToArray();
    }

    public void Dispose() => _session.Dispose();
}

public static class ImageDecoding
{
    private const string UnsupportedMessage = "無法解析上傳的檔案，請上傳 JPG / PNG / WebP / BMP / GIF 圖片。";

    /// <summary>Decodes to RGBA and applies the EXIF orientation, so phone photos are upright.</summary>
    public static SKBitmap Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException(UnsupportedMessage);

        var info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidImageException(UnsupportedMessage);
        }

        return ApplyOrientation(bitmap, codec.EncodedOrigin);
    }

    /// <summary>Cheap header-only check used before handing bytes to OCR or Ollama.</summary>
    public static void EnsureValid(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException(UnsupportedMessage);
    }

    /// <summary>
    /// Downscales so the long side is at most <paramref name="maxSide"/> pixels (EXIF orientation applied).
    /// A vision model's prompt cost grows with pixel count, so a 4000 px phone photo would otherwise
    /// cost thousands of image tokens. Images that are already small and upright pass through unchanged.
    /// </summary>
    public static byte[] ShrinkForVision(byte[] bytes, int maxSide)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException(UnsupportedMessage);
        var format = codec.EncodedFormat;
        if (Math.Max(codec.Info.Width, codec.Info.Height) <= maxSide
            && codec.EncodedOrigin == SKEncodedOrigin.TopLeft
            && format is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png)
        {
            return bytes;
        }

        using var source = Decode(bytes);
        var scale = Math.Min(1f, (float)maxSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)MathF.Round(source.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(source.Height * scale));

        using var resized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(resized))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        // PNG keeps screenshots and text crisp; everything else becomes JPEG.
        var output = format == SKEncodedImageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var encoded = SKImage.FromBitmap(resized).Encode(output, 90);
        return encoded.ToArray();
    }

    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return bitmap;

        float w = bitmap.Width, h = bitmap.Height;
        // Maps stored pixel (x, y) to display position: x' = a*x + b*y + c, y' = d*x + e*y + f.
        var (a, b, c, d, e, f) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),
            SKEncodedOrigin.RightTop => (0f, -1f, h, 1f, 0f, 0f),
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, w),
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };

        var swapsAxes = a == 0f;
        var oriented = new SKBitmap(new SKImageInfo(
            swapsAxes ? bitmap.Height : bitmap.Width,
            swapsAxes ? bitmap.Width : bitmap.Height,
            bitmap.ColorType,
            bitmap.AlphaType));

        using (bitmap)
        using (var image = SKImage.FromBitmap(bitmap))
        using (var canvas = new SKCanvas(oriented))
        {
            canvas.SetMatrix(new SKMatrix(a, b, c, d, e, f, 0, 0, 1));
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }

        return oriented;
    }
}
