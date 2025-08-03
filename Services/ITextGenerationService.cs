using System.Threading;
using System.Threading.Tasks;

namespace LocalAIAgentAPI.Services
{
    public interface ITextGenerationService
    {
        Task<object> ProcessAsync(string prompt, CancellationToken cancellationToken);
    }
}
