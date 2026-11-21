using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RapidOcrNet;

namespace LocalAIAgentAPI.Services;

public interface IOcrService
{
    Task<string> RecognizeTextAsync(byte[] image, CancellationToken cancellationToken);
}

/// <summary>
/// PP-OCRv5 mobile (ONNX, via RapidOcrNet). On OmniDocBench its error rate is about a fifth of Tesseract's,
/// and it reads Traditional Chinese reliably. Engines are pooled because loading the models takes ~300 ms.
/// </summary>
public sealed class PaddleOcrService : IOcrService, IDisposable
{
    // The text-line orientation classifier costs 2-3x latency and only matters for upside-down text.
    private static readonly RapidOcrOptions DetectOptions = RapidOcrOptions.Default with { DoAngle = false };

    private readonly ObjectPool<RapidOcr> _engines;

    public PaddleOcrService(IOptions<InferenceOptions> options, IHostEnvironment environment)
    {
        var o = options.Value;
        var policy = new EnginePolicy(
            ContentPath.Resolve(environment, o.OcrDetectorPath),
            ContentPath.Resolve(environment, o.OcrClassifierPath),
            ContentPath.Resolve(environment, o.OcrRecognizerPath),
            ContentPath.Resolve(environment, o.OcrDictionaryPath),
            Math.Max(1, Environment.ProcessorCount / 2));
        _engines = new DefaultObjectPoolProvider { MaximumRetained = 2 }.Create(policy);
    }

    public Task<string> RecognizeTextAsync(byte[] image, CancellationToken cancellationToken)
    {
        ImageDecoding.EnsureValid(image);

        return Task.Run(() =>
        {
            using var bitmap = ImageDecoding.Decode(image);
            var engine = _engines.Get();
            try
            {
                return engine.Detect(bitmap, DetectOptions, cancellationToken).StrRes.Trim();
            }
            finally
            {
                _engines.Return(engine);
            }
        }, cancellationToken);
    }

    public void Dispose() => (_engines as IDisposable)?.Dispose();

    private sealed class EnginePolicy(string detector, string classifier, string recognizer, string dictionary, int threads)
        : PooledObjectPolicy<RapidOcr>
    {
        public override RapidOcr Create()
        {
            var engine = new RapidOcr();
            engine.InitModels(detector, classifier, recognizer, dictionary, threads);
            return engine;
        }

        public override bool Return(RapidOcr engine) => true;
    }
}
