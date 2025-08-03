using System.IO;
using System.Threading.Tasks;
using Tesseract;
using Microsoft.AspNetCore.Http;

namespace LocalAIAgentAPI.Services
{
    public class OcrService : IOcrService
    {
        public async Task<string> RecognizeTextAsync(IFormFile imageFile)
        {
            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);
            ms.Position = 0;

            using var engine = new TesseractEngine("./tessdata", "eng+chi_tra", EngineMode.LstmOnly);
            using var img = Pix.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(img);
            return page.GetText();
        }
    }
}
