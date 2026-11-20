using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LocalAIAgentAPI.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 7807 problem responses. Stack traces and internal messages
/// never leave the server; only exceptions we raise on purpose expose their message.
/// </summary>
internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, title, detail) = exception switch
        {
            InvalidImageException e => (StatusCodes.Status400BadRequest, "Invalid image", e.Message),
            BadHttpRequestException e => (e.StatusCode, "Bad request", e.Message),
            UpstreamUnavailableException e => (StatusCodes.Status503ServiceUnavailable, "AI backend unavailable", e.Message),
            OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "Request timed out", "模型在時限內沒有回應，請稍後再試或調高 Ollama:TimeoutSeconds。"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error",
                environment.IsDevelopment() ? exception.Message : "伺服器發生未預期的錯誤。"),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Request {Method} {Path} failed", context.Request.Method, context.Request.Path);
        else if (status >= 500)
            logger.LogWarning("Request {Method} {Path} failed with {Status}: {Reason}", context.Request.Method, context.Request.Path, status, exception.Message);
        else
            logger.LogInformation("Request {Method} {Path} rejected with {Status}: {Reason}", context.Request.Method, context.Request.Path, status, exception.Message);

        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
