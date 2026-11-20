namespace LocalAIAgentAPI.Infrastructure;

public sealed class InferenceOptions
{
    public const string SectionName = "Inference";

    public string ImageClassifierModelPath { get; set; } = "Models/resnet50-v2-7.onnx";
    public string ImageNetLabelsPath { get; set; } = "Models/imagenet_labels.txt";
    public string TessdataPath { get; set; } = "tessdata";
    public string OcrLanguages { get; set; } = "eng+chi_tra";
}

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string TextModel { get; set; } = "llama2";
    public string AgentModel { get; set; } = "mistral";
    public string VisionModel { get; set; } = "llava";
    public int TimeoutSeconds { get; set; } = 180;
    public int MaxAgentSteps { get; set; } = 4;
}
