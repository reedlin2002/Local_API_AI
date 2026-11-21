using System.Diagnostics;
using LocalAIAgentAPI.Services;
using Microsoft.Extensions.Options;

namespace LocalAIAgentAPI.Infrastructure;

/// <summary>
/// Loads the Ollama model in the background when the API starts, so the first user request does not
/// wait 8-14 s for it. Failure (Ollama not running yet) is only logged.
/// </summary>
internal sealed class OllamaWarmUp(
    IServiceScopeFactory scopes,
    IOptions<OllamaOptions> options,
    ILogger<OllamaWarmUp> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WarmUpOnStart)
            return;

        var model = options.Value.Model;
        try
        {
            using var scope = scopes.CreateScope();
            var stopwatch = Stopwatch.StartNew();
            await scope.ServiceProvider.GetRequiredService<IOllamaClient>().WarmUpAsync(model, stoppingToken);
            logger.LogInformation("Ollama model {Model} loaded in {ElapsedMs} ms", model, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception e) when (e is UpstreamUnavailableException or OperationCanceledException or HttpRequestException)
        {
            logger.LogWarning("Could not preload Ollama model {Model}: {Reason}", model, e.Message);
        }
    }
}
