using LocalAIAgentAPI.Infrastructure;
using LocalAIAgentAPI.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// The front end expects the API on localhost:5136. Use that when nothing else is configured
// (e.g. the Windows release exe); Docker sets HTTP_PORTS and `dotnet run` uses launchSettings.
if (string.IsNullOrEmpty(builder.Configuration["urls"])
    && string.IsNullOrEmpty(builder.Configuration["http_ports"])
    && string.IsNullOrEmpty(builder.Configuration["https_ports"]))
{
    builder.WebHost.UseUrls("http://localhost:5136");
}

builder.Services.Configure<InferenceOptions>(builder.Configuration.GetSection(InferenceOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));

var maxUploadBytes = builder.Configuration.GetValue<long?>("Upload:MaxBytes") ?? 10 * 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUploadBytes + 64 * 1024);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();

// Only the origins listed in configuration may call this API from a browser. Without this,
// any website the user visits could use the local models in the background.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST")
    .AllowAnyHeader()));

builder.Services.AddSingleton<IImageClassifier, OnnxImageClassifier>();
builder.Services.AddSingleton<IOcrService, PaddleOcrService>();
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});
builder.Services.AddScoped<IImageDescriber, ImageDescriber>();
builder.Services.AddScoped<IAssistantService, AssistantService>();
builder.Services.AddHostedService<OllamaWarmUp>();

var app = builder.Build();

// Load the ONNX model at startup: fail fast if it is missing and keep the first request fast.
app.Services.GetRequiredService<IImageClassifier>();

app.UseExceptionHandler();

// Lets the GitHub Pages front end reach http://localhost in browsers that still send
// Private Network Access preflights. CORS still decides whether the origin is allowed.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method)
        && context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }

    await next();
});

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
