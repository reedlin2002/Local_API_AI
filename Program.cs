using LocalAIAgentAPI.Services;
using LocalAIAgentAPI.Interfaces;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient();

builder.Services.AddScoped<IAIService, ImageClassifierService>(); // 預設實作
builder.Services.AddScoped<ITextGenerationService, OllamaTextGenerationService>();
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<AIServiceFactory>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ErrorHandlingMiddleware>();  //全域錯誤處理

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.Run();
