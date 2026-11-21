namespace LocalAIAgentAPI.Infrastructure;

public sealed class InferenceOptions
{
    public const string SectionName = "Inference";

    public string ImageClassifierModelPath { get; set; } = "Models/resnet50-v2-7.onnx";
    public string ImageNetLabelsPath { get; set; } = "Models/imagenet_labels.txt";

    /// <summary>PP-OCRv5 mobile: text detector, line-orientation classifier, and the Chinese recognizer (Simplified + Traditional Chinese, English, Japanese).</summary>
    public string OcrDetectorPath { get; set; } = "Models/ocr/ch_PP-OCRv5_mobile_det.onnx";
    public string OcrClassifierPath { get; set; } = "Models/ocr/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    public string OcrRecognizerPath { get; set; } = "Models/ocr/ch_PP-OCRv5_rec_mobile.onnx";
    public string OcrDictionaryPath { get; set; } = "Models/ocr/ppocrv5_dict.txt";
}

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// One multimodal model serves text, vision and the assistant. With 4 GB of VRAM only one model fits,
    /// and every switch between models costs a 8-17 s reload.
    /// </summary>
    public string Model { get; set; } = "qwen3.5:2b";

    /// <summary>Optional per-task overrides; empty means <see cref="Model"/>.</summary>
    public string? TextModel { get; set; }
    public string? AssistantModel { get; set; }
    public string? VisionModel { get; set; }

    public string ForText => string.IsNullOrWhiteSpace(TextModel) ? Model : TextModel;
    public string ForAssistant => string.IsNullOrWhiteSpace(AssistantModel) ? Model : AssistantModel;
    public string ForVision => string.IsNullOrWhiteSpace(VisionModel) ? Model : VisionModel;

    /// <summary>How long Ollama keeps the model in memory after a request (reloading takes 8-14 s on a laptop GPU).</summary>
    public string KeepAlive { get; set; } = "30m";

    public int ContextLength { get; set; } = 4096;

    /// <summary>Images are downscaled so the long side is at most this many pixels before reaching the model; image tokens grow with pixel count.</summary>
    public int MaxImageSide { get; set; } = 768;

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Load the model when the API starts so the first request does not pay for it.</summary>
    public bool WarmUpOnStart { get; set; } = true;
}
