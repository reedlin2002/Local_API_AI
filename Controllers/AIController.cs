using Microsoft.AspNetCore.Mvc;
using LocalAIAgentAPI.Services;
using LocalAIAgentAPI.Interfaces;  // 加入介面命名空間
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace LocalAIAgentAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly AIServiceFactory _serviceFactory;
        private readonly IOcrService _ocrService;
        private readonly IAIService _imageClassifierService;
        private readonly IAgentService _agentService;  // 新增 AgentService 欄位
        private readonly ILogger<AIController> _logger;

        public AIController(
            AIServiceFactory serviceFactory,
            IOcrService ocrService,
            IAIService imageClassifierService,
            IAgentService agentService,   // 建構子注入 IAgentService
            ILogger<AIController> logger)
        {
            _serviceFactory = serviceFactory;
            _ocrService = ocrService;
            _imageClassifierService = imageClassifierService;
            _agentService = agentService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessRequest(
            [FromForm] string model,
            [FromForm] IFormFile? file,
            [FromForm] string? prompt,
            [FromQuery] string outputFormat = "json",
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("ProcessRequest 被呼叫, model={Model}, prompt={Prompt}, outputFormat={OutputFormat}", model, prompt, outputFormat);

            if (string.IsNullOrWhiteSpace(model))
                return BadRequest("Model is required.");

            var service = _serviceFactory.GetService(model);

            // 只對 imageclassifier 和 textgeneration 用 serviceFactory，agent直接用注入的 service
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(60000);

                object result;

                if (model.ToLower() == "imageclassifier")
                {
                    if (file == null)
                        return BadRequest("File is required for image classification.");

                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, cancellationToken);

                    var imgService = service as IAIService;
                    if (imgService == null)
                        return BadRequest("Invalid service implementation for imageclassifier.");

                    result = await imgService.ProcessAsync(ms.ToArray(), timeoutCts.Token);
                }
                else if (model.ToLower() == "textgeneration")
                {
                    if (string.IsNullOrWhiteSpace(prompt))
                        return BadRequest("Prompt is required for text generation.");

                    var textService = service as ITextGenerationService;
                    if (textService == null)
                        return BadRequest("Invalid service implementation for textgeneration.");

                    result = await textService.ProcessAsync(prompt, timeoutCts.Token);
                }
                else if (model.ToLower() == "agent")  // 新增 Agent 支援
                {
                    if (string.IsNullOrWhiteSpace(prompt))
                        return BadRequest("Prompt is required for agent.");

                    result = await _agentService.AskAsync(prompt, timeoutCts.Token);
                }
                else
                {
                    return BadRequest("Unsupported model type.");
                }

                _logger.LogInformation("模型處理成功，結果={@Result}", result);

                if (outputFormat.ToLower() == "text")
                {
                    return Content(result?.ToString() ?? "(empty)", "text/plain");
                }

                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("請求超時，model={Model}", model);
                return StatusCode(504, "Request timed out.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "模型處理失敗");
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        // 其餘方法保持不變（可以直接用你提供的）
        [HttpPost("describe-image")]
        public async Task<IActionResult> DescribeImage(
            [FromForm] IFormFile file,
            [FromQuery] string outputFormat = "json",
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                return BadRequest("File is required.");

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, cancellationToken);
                var imageBytes = ms.ToArray();

                var imgService = _serviceFactory.GetService("imageclassifier") as IAIService;
                if (imgService == null)
                    return BadRequest("ImageClassifier Service not available.");

                var classificationResult = await imgService.ProcessAsync(imageBytes, cancellationToken);
                string labelInfo = classificationResult?.ToString() ?? "unknown";

                string prompt = $"Please write a detailed description of an image of {labelInfo}.";

                var textService = _serviceFactory.GetService("textgeneration") as ITextGenerationService;
                if (textService == null)
                    return BadRequest("TextGeneration Service not available.");

                var textResult = await textService.ProcessAsync(prompt, cancellationToken);

                var response = new { description = textResult?.ToString() ?? "(empty)" };

                if (outputFormat.ToLower() == "text")
                    return Content(response.description, "text/plain");

                return Ok(response);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(504, "Request timed out.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "DescribeImage 發生錯誤");
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        [HttpPost("batch-classify")]
        public async Task<IActionResult> BatchClassify(
            [FromForm] List<IFormFile> files,
            [FromQuery] string outputFormat = "json",
            CancellationToken cancellationToken = default)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files provided.");

            var results = new List<object>();

            foreach (var file in files)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, cancellationToken);
                var imageBytes = ms.ToArray();

                var label = await _imageClassifierService.ProcessAsync(imageBytes, cancellationToken);

                results.Add(new
                {
                    file = file.FileName,
                    label = label?.ToString() ?? "(unknown)"
                });
            }

            if (outputFormat.ToLower() == "text")
            {
                var lines = results.ConvertAll(r => $"{((dynamic)r).file}: {((dynamic)r).label}");
                return Content(string.Join("\n", lines), "text/plain");
            }

            return Ok(results);
        }

        [HttpPost("ocr")]
        public async Task<IActionResult> RecognizeText(
            [FromForm] IFormFile file,
            [FromQuery] string outputFormat = "json",
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                return BadRequest("File is required.");

            try
            {
                var text = await _ocrService.RecognizeTextAsync(file);
                var result = new { text = text?.Trim() ?? "(empty)" };

                if (outputFormat.ToLower() == "text")
                    return Content(result.text, "text/plain");

                return Ok(result);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "OCR 發生錯誤");
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }
        [HttpPost("ask")]
        public async Task<IActionResult> AskAgent(
            [FromForm] string prompt,
            [FromQuery] string outputFormat = "json",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return BadRequest("Prompt is required.");

            try
            {
                var result = await _agentService.AskAsync(prompt, cancellationToken);

                if (outputFormat.ToLower() == "text")
                    return Content(result ?? "(empty)", "text/plain");

                return Ok(new { answer = result });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(504, "Request timed out.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "AskAgent 發生錯誤");
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

    }
}
