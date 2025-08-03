namespace LocalAIAgentAPI.Models
{
    public class AIRequest
    {
        public string Model { get; set; } = null!;
        public string? Prompt { get; set; }
        public string OutputFormat { get; set; } = "json"; // "json" or "text"
    }

    
}
