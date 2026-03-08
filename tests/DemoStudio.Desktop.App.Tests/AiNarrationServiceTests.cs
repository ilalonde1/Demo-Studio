using System.Net;
using System.Net.Http;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class AiNarrationServiceTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsFailure_WhenApiKeyMissing()
    {
        var previous = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        try
        {
            var service = new DesktopAiNarrationService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
            var result = await service.GenerateAsync(new DesktopAiNarrationRequest(
                Script: "Test script",
                OutputPath: path,
                Provider: "OpenAI",
                BaseUrl: null,
                Model: null,
                Voice: null,
                ApiKey: null));
            Assert.False(result.Succeeded);
            Assert.Contains("OPENAI_API_KEY", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previous);
        }
    }

    [Fact]
    public async Task GenerateAsync_WritesAudio_WhenApiReturnsSuccess()
    {
        var previous = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var previousBase = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("OPENAI_BASE_URL", "https://example.test/v1");

        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
            var output = Path.Combine(root, "ai.mp3");

        try
        {
            var handler = new FakeHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 });
                return response;
            });
            var service = new DesktopAiNarrationService(new HttpClient(handler));

            var result = await service.GenerateAsync(new DesktopAiNarrationRequest(
                Script: "Test script",
                OutputPath: output,
                Provider: "OpenAI",
                BaseUrl: "https://example.test/v1",
                Model: "gpt-4o-mini-tts",
                Voice: "alloy",
                ApiKey: "test-key"));

            Assert.True(result.Succeeded, result.Message);
            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previous);
            Environment.SetEnvironmentVariable("OPENAI_BASE_URL", previousBase);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _callback;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_callback(request));
    }
}
