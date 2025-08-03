using System.Threading;
using System.Threading.Tasks;

namespace LocalAIAgentAPI.Services
{
    public interface IAIService
    {
        Task<object> ProcessAsync(byte[] input, CancellationToken cancellationToken);
    }
}
