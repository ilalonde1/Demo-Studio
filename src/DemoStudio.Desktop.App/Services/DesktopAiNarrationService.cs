using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopAiNarrationService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);
    private readonly HttpClient _httpClient;

    public DesktopAiNarrationService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<DesktopClipNarrationResult> GenerateAsync(
        DesktopAiNarrationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return DesktopClipNarrationResult.Failure("AI narration failed: request is missing.");
        }

        var script = request.Script;
        var outputPath = request.OutputPath;
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "OpenAI" : request.Provider.Trim();
        if (string.IsNullOrWhiteSpace(script))
        {
            return DesktopClipNarrationResult.Failure("AI narration failed: script is empty.");
        }

        if (!string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return DesktopClipNarrationResult.Failure($"AI narration provider '{provider}' is not supported yet.");
        }

        var apiKey = FirstNonEmpty(request.ApiKey, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return DesktopClipNarrationResult.Failure("AI narration is not configured. Set OPENAI_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return DesktopClipNarrationResult.Failure("AI narration failed: output path is missing.");
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return DesktopClipNarrationResult.Failure("AI narration failed: output directory is invalid.");
        }

        Directory.CreateDirectory(directory);
        try
        {
            File.Delete(outputPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var baseUrl = FirstNonEmpty(request.BaseUrl, Environment.GetEnvironmentVariable("OPENAI_BASE_URL"));
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://api.openai.com/v1";
        }

        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        var voice = FirstNonEmpty(request.Voice, Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE"));
        if (string.IsNullOrWhiteSpace(voice))
        {
            voice = "alloy";
        }

        var model = FirstNonEmpty(request.Model, Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL"));
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini-tts";
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "audio/speech"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model,
                voice,
                input = script.Trim(),
                format = "mp3"
            }),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);
            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var failure = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var compact = string.IsNullOrWhiteSpace(failure) ? response.ReasonPhrase : failure;
                if (string.IsNullOrWhiteSpace(compact))
                {
                    compact = "Unknown API error.";
                }

                if (compact.Length > 260)
                {
                    compact = compact[..260];
                }

                return DesktopClipNarrationResult.Failure($"AI narration failed: {compact}");
            }

            var audioBytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
            await File.WriteAllBytesAsync(outputPath, audioBytes, timeoutCts.Token);
            return DesktopClipNarrationResult.Success(outputPath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DesktopClipNarrationResult.Failure("AI narration request timed out. Check network and API settings.");
        }
        catch (Exception ex)
        {
            return DesktopClipNarrationResult.Failure($"AI narration request failed: {ex.Message}");
        }
    }

    private static string? FirstNonEmpty(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a))
        {
            return a.Trim();
        }

        if (!string.IsNullOrWhiteSpace(b))
        {
            return b.Trim();
        }

        return null;
    }
}

public sealed record DesktopAiNarrationRequest(
    string Script,
    string OutputPath,
    string? Provider,
    string? BaseUrl,
    string? Model,
    string? Voice,
    string? ApiKey);
