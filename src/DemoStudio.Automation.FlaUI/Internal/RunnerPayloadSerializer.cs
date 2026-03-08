namespace DemoStudio.Automation.FlaUI.Internal;

using System.Text.Json;
using DemoStudio.Automation.FlaUI.Models;

internal static class RunnerPayloadSerializer
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string SerializeRequest(FlaUIRunnerRequest request)
    {
        return JsonSerializer.Serialize(request, CamelCaseOptions);
    }

    internal static FlaUIRunnerResponse? DeserializeResponse(string payload)
    {
        return JsonSerializer.Deserialize<FlaUIRunnerResponse>(payload, CaseInsensitiveOptions);
    }
}

