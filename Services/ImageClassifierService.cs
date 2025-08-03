using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace LocalAIAgentAPI.Services
{
    public class ImageClassifierService : IAIService
    {
        private readonly InferenceSession _session;
        private readonly string[] _labels;

        public ImageClassifierService()
        {
            _session = new InferenceSession("Models/resnet50-v2-7.onnx");
            _labels = File.ReadAllLines("Models/imagenet_labels.txt");
        }

        public async Task<object> ProcessAsync(byte[] input, CancellationToken cancellationToken)
        {
            // 用 Task.Run 包裝同步程式碼，避免阻塞
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream(input);
                using var bitmap = new Bitmap(ms);
                var tensor = ImageToTensor(bitmap);

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("data", tensor)
                };

                using var results = _session.Run(inputs);
                var output = results.First().AsEnumerable<float>().ToArray();

                int maxIndex = output.ToList().IndexOf(output.Max());
                string label = _labels[maxIndex];

                return new { label, confidence = output[maxIndex] };
            }, cancellationToken);
        }

        private static Tensor<float> ImageToTensor(Bitmap bitmap)
        {
            int width = 224;
            int height = 224;

            // Resize to 224x224
            using var resized = new Bitmap(bitmap, new Size(width, height));

            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = resized.GetPixel(x, y);

                    // Normalize to [0,1]
                    tensor[0, 0, y, x] = pixel.R / 255f;
                    tensor[0, 1, y, x] = pixel.G / 255f;
                    tensor[0, 2, y, x] = pixel.B / 255f;
                }
            }

            return tensor;
        }
    }
}
