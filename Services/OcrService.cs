using System.Text.RegularExpressions;
using LocalAIAgentAPI.Infrastructure;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Tesseract;

namespace LocalAIAgentAPI.Services;

public interface IOcrService
{
    Task<string> RecognizeTextAsync(byte[] image, CancellationToken cancellationToken);
}

/// <summary>
/// Creating a TesseractEngine loads the traineddata files (300-600 ms) and an engine is not
/// thread-safe, so engines are pooled: each request borrows one and returns it afterwards.
/// </summary>
public sealed partial class TesseractOcrService : IOcrService, IDisposable
{
    private readonly ObjectPool<TesseractEngine> _engines;

    public TesseractOcrService(IOptions<InferenceOptions> options, IHostEnvironment environment)
    {
        var tessdata = ContentPath.Resolve(environment, options.Value.TessdataPath);
        var provider = new DefaultObjectPoolProvider { MaximumRetained = Math.Max(2, Environment.ProcessorCount / 2) };
        _engines = provider.Create(new EnginePolicy(tessdata, options.Value.OcrLanguages));
    }

    public Task<string> RecognizeTextAsync(byte[] image, CancellationToken cancellationToken)
    {
        ImageDecoding.EnsureValid(image);

        return Task.Run(() =>
        {
            using var pix = LoadPix(image);
            var engine = _engines.Get();
            try
            {
                using var page = engine.Process(pix);
                return RemoveSpacesBetweenCjk(page.GetText()).Trim();
            }
            finally
            {
                _engines.Return(engine);
            }
        }, cancellationToken);
    }

    private static Pix LoadPix(byte[] image)
    {
        try
        {
            return Pix.LoadFromMemory(image);
        }
        catch (IOException e)
        {
            throw new InvalidImageException("OCR 引擎無法讀取這張圖片，請改用 PNG 或 JPG。", e);
        }
    }

    /// <summary>Tesseract's chi_tra model puts a space between every character ("你 好 ，").</summary>
    internal static string RemoveSpacesBetweenCjk(string text) => CjkGap().Replace(text, string.Empty);

    [GeneratedRegex(@"(?<=[\p{IsCJKUnifiedIdeographs}\p{IsCJKSymbolsandPunctuation}＀-￯])[ \t]+(?=[\p{IsCJKUnifiedIdeographs}\p{IsCJKSymbolsandPunctuation}＀-￯])")]
    private static partial Regex CjkGap();

    public void Dispose() => (_engines as IDisposable)?.Dispose();

    private sealed class EnginePolicy(string tessdataPath, string languages) : PooledObjectPolicy<TesseractEngine>
    {
        public override TesseractEngine Create() => new(tessdataPath, languages, EngineMode.LstmOnly);

        public override bool Return(TesseractEngine engine) => true;
    }
}
