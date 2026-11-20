namespace LocalAIAgentAPI.Infrastructure;

/// <summary>The uploaded file is not an image we can decode. Maps to HTTP 400.</summary>
public sealed class InvalidImageException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>A dependency such as Ollama is down or misconfigured. Maps to HTTP 503.</summary>
public class UpstreamUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class OllamaModelNotFoundException(string model)
    : UpstreamUnavailableException($"Ollama 找不到模型 '{model}'，請先執行 `ollama pull {model}`。")
{
    public string Model { get; } = model;
}

public sealed class OllamaToolsNotSupportedException(string model)
    : UpstreamUnavailableException($"Ollama 模型 '{model}' 不支援 tool calling。")
{
    public string Model { get; } = model;
}
