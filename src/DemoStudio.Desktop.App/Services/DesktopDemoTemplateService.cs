using System.Text.Json;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopDemoTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _storePath;

    public DesktopDemoTemplateService(string storageRoot)
    {
        Directory.CreateDirectory(storageRoot);
        _storePath = Path.Combine(storageRoot, "demo-templates.json");
    }

    public async Task<IReadOnlyList<DesktopDemoTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storePath))
        {
            return Array.Empty<DesktopDemoTemplate>();
        }

        try
        {
            var raw = await File.ReadAllTextAsync(_storePath, cancellationToken);
            var parsed = JsonSerializer.Deserialize<List<DesktopDemoTemplate>>(raw, JsonOptions);
            return parsed ?? new List<DesktopDemoTemplate>();
        }
        catch
        {
            return Array.Empty<DesktopDemoTemplate>();
        }
    }

    public async Task SaveAsync(DesktopDemoTemplate template, CancellationToken cancellationToken = default)
    {
        var list = (await ListAsync(cancellationToken)).ToList();
        var idx = list.FindIndex(x => string.Equals(x.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            list[idx] = template;
        }
        else
        {
            list.Add(template);
        }

        await PersistAsync(list, cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var list = (await ListAsync(cancellationToken)).ToList();
        list.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        await PersistAsync(list, cancellationToken);
    }

    private async Task PersistAsync(IReadOnlyList<DesktopDemoTemplate> templates, CancellationToken cancellationToken)
    {
        var raw = JsonSerializer.Serialize(templates, JsonOptions);
        await File.WriteAllTextAsync(_storePath, raw, cancellationToken);
    }
}

public sealed record DesktopDemoTemplate(
    string Name,
    string CaptureMode,
    string? WindowTitleContains,
    bool FallbackToDesktop,
    bool CaptureNarration,
    string? MicrophoneDeviceName,
    string QualityPreset,
    string ExportStyle,
    IReadOnlyList<DesktopTemplateClipRule> ClipRules);

public sealed record DesktopTemplateClipRule(int Order, string Label, string? BannerText, bool IncludeNarration);
