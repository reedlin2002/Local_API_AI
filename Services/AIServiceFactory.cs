using Microsoft.Extensions.DependencyInjection;

namespace LocalAIAgentAPI.Services
{
    public class AIServiceFactory
    {
        private readonly IServiceProvider _provider;

        public AIServiceFactory(IServiceProvider provider)
        {
            _provider = provider;
        }

        public object? GetService(string model)
        {
            return model.ToLower() switch
            {
                "imageclassifier" => _provider.GetService<IAIService>(),
                "textgeneration" => _provider.GetService<ITextGenerationService>(),
                _ => null
            };
        }
    }
}
