using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace LocalAIAgentAPI.Services
{
    public interface IOcrService
    {
        Task<string> RecognizeTextAsync(IFormFile imageFile);
    }
}
