using LocalAIAgentAPI.Infrastructure;
using LocalAIAgentAPI.Services;
using SkiaSharp;

namespace LocalAIAgentAPI.Tests;

public sealed class AssistantServiceTests
{
    private readonly FakeOllama _ollama = new();

    [Fact]
    public async Task Text_only_question_is_one_chat_call_without_images()
    {
        _ollama.OnChat = (_, _) => "42";
        var assistant = new AssistantService(_ollama, new FakeOcr(), Samples.OllamaOptions());

        var answer = await assistant.AskAsync("meaning of life?", null, default);

        Assert.Equal("42", answer.Answer);
        Assert.Empty(answer.Steps);
        var call = Assert.Single(_ollama.ChatCalls);
        Assert.Equal("qwen3.5:2b", call.Model);
        Assert.All(call.Messages, m => Assert.Null(m.Images));
    }

    [Fact]
    public async Task Image_question_sends_the_image_and_the_ocr_text_in_one_call()
    {
        var ocr = new FakeOcr("你好啊，這是測試");
        var assistant = new AssistantService(_ollama, ocr, Samples.OllamaOptions());

        var answer = await assistant.AskAsync("圖上寫什麼？", Samples.Read("ocr-chi.png"), default);

        Assert.Equal([AssistantService.OcrStep, AssistantService.VisionStep], answer.Steps);
        Assert.Equal(1, ocr.Calls);
        var user = Assert.Single(_ollama.ChatCalls).Messages.Last();
        Assert.Equal("user", user.Role);
        Assert.Single(user.Images!);
        Assert.Contains("圖上寫什麼？", user.Content);
        Assert.Contains("你好啊，這是測試", user.Content);
    }

    [Fact]
    public async Task Says_when_ocr_found_no_text()
    {
        var assistant = new AssistantService(_ollama, new FakeOcr(""), Samples.OllamaOptions());

        await assistant.AskAsync("這是什麼動物？", Samples.Read("cat.jpg"), default);

        Assert.Contains("沒有在圖片中找到文字", _ollama.ChatCalls[0].Messages.Last().Content);
    }

    [Fact]
    public async Task Uses_the_assistant_override_model_when_configured()
    {
        var assistant = new AssistantService(_ollama, new FakeOcr(), Samples.OllamaOptions(o => o.AssistantModel = "qwen3.5:4b"));

        var answer = await assistant.AskAsync("hi", null, default);

        Assert.Equal("qwen3.5:4b", answer.Model);
    }

    [Fact]
    public async Task Rejects_non_images_before_calling_ocr_or_ollama()
    {
        var ocr = new FakeOcr();
        var assistant = new AssistantService(_ollama, ocr, Samples.OllamaOptions());

        await Assert.ThrowsAsync<InvalidImageException>(() => assistant.AskAsync("?", "not an image"u8.ToArray(), default));

        Assert.Equal(0, ocr.Calls);
        Assert.Empty(_ollama.ChatCalls);
    }
}

public sealed class ImageDescriberTests
{
    [Fact]
    public async Task Sends_the_image_to_the_vision_model_with_the_given_instruction()
    {
        var ollama = new FakeOllama();
        var describer = new ImageDescriber(ollama, Samples.OllamaOptions());

        var result = await describer.DescribeAsync(Samples.Read("dog.jpeg"), "請用繁體中文描述", default);

        var call = Assert.Single(ollama.GenerateCalls);
        Assert.Equal("qwen3.5:2b", call.Model);
        Assert.Equal("請用繁體中文描述", call.Prompt);
        Assert.Single(call.Images!);
        Assert.Equal("qwen3.5:2b", result.Model);
    }

    [Fact]
    public async Task Missing_model_surfaces_as_a_pull_hint()
    {
        var ollama = new FakeOllama { OnGenerate = (model, _) => throw new OllamaModelNotFoundException(model) };
        var describer = new ImageDescriber(ollama, Samples.OllamaOptions());

        var e = await Assert.ThrowsAsync<OllamaModelNotFoundException>(() => describer.DescribeAsync(Samples.Read("cat.jpg"), null, default));
        Assert.Contains("ollama pull qwen3.5:2b", e.Message);
    }
}

public sealed class ShrinkForVisionTests
{
    [Fact]
    public void Large_images_are_downscaled_to_the_max_side_keeping_aspect_ratio()
    {
        var dog = Samples.Read("dog.jpeg"); // 1000 x 667

        var shrunk = ImageDecoding.ShrinkForVision(dog, 768);

        using var bitmap = SKBitmap.Decode(shrunk);
        Assert.Equal(768, bitmap.Width);
        Assert.InRange(bitmap.Height, 511, 513);
    }

    [Fact]
    public void Small_images_pass_through_untouched()
    {
        var cat = Samples.Read("cat.jpg"); // 612 x 531

        Assert.Same(cat, ImageDecoding.ShrinkForVision(cat, 768));
    }

    [Fact]
    public void Png_stays_png_so_text_stays_crisp()
    {
        var png = Samples.Read("ocr-chi.png"); // 426 x 43

        var shrunk = ImageDecoding.ShrinkForVision(png, 200);

        using var codec = SKCodec.Create(new MemoryStream(shrunk));
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.Equal(200, codec.Info.Width);
    }
}
