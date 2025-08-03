using System.Threading;
using System.Threading.Tasks;

namespace LocalAIAgentAPI.Interfaces
{
    public interface IAgentService
    {
        Task<string> AskAsync(string prompt, CancellationToken cancellationToken);
    }
}
