using Aviscribe.Core.Ocr;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class OcrInferenceAbstractionTests
{
    [Fact]
    public void ServiceUsesInjectedFactoryAndFixedNchwTensor()
    {
        var dictionaryPath = CreateDictionary();
        try
        {
            var session = new FakeSession();
            var factory = new FakeFactory(session);
            using var service = new OnnxOcrService(
                "test-model.onnx",
                dictionaryPath,
                factory);
            using var image = new Mat(
                new Size(80, 24),
                MatType.CV_8UC3,
                Scalar.Black);

            var text = service.ReadText(image);

            Assert.Equal("A", text);
            Assert.Equal("test-model.onnx", factory.ModelPath);
            Assert.NotNull(session.InputDimensions);
            Assert.Equal([1, 3, 48, 320], session.InputDimensions);
        }
        finally
        {
            File.Delete(dictionaryPath);
        }
    }

    [Fact]
    public void ServiceDisposesInjectedSession()
    {
        var dictionaryPath = CreateDictionary();
        try
        {
            var session = new FakeSession();
            var service = new OnnxOcrService(
                "test-model.onnx",
                dictionaryPath,
                new FakeFactory(session));

            service.Dispose();

            Assert.True(session.Disposed);
        }
        finally
        {
            File.Delete(dictionaryPath);
        }
    }

    private static string CreateDictionary()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"aviscribe-dict-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "A");
        return path;
    }

    private sealed class FakeFactory(IOcrInferenceSession session)
        : IOcrInferenceSessionFactory
    {
        public string? ModelPath { get; private set; }

        public IOcrInferenceSession Create(string modelPath)
        {
            ModelPath = modelPath;
            return session;
        }
    }

    private sealed class FakeSession : IOcrInferenceSession
    {
        public string InputName => "input";
        public int[]? InputDimensions { get; private set; }
        public bool Disposed { get; private set; }

        public OcrInferenceOutput Run(DenseTensor<float> input)
        {
            InputDimensions = input.Dimensions.ToArray();
            return new OcrInferenceOutput([0, 1], 1, 2);
        }

        public void Dispose() => Disposed = true;
    }
}
