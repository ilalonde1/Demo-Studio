using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshDemoTemplatesAsync()
    {
        var list = await _demoTemplateService.ListAsync();
        DemoTemplates.Clear();
        foreach (var item in list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            DemoTemplates.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(SelectedTemplateName) &&
            DemoTemplates.Any(x => string.Equals(x.Name, SelectedTemplateName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedTemplateName = DemoTemplates.FirstOrDefault()?.Name ?? string.Empty;
        RaiseCommandState();
    }

    private async Task SaveDemoTemplateAsync()
    {
        if (!CanSaveDemoTemplate)
        {
            return;
        }

        var template = new DesktopDemoTemplate(
            Name: TemplateName.Trim(),
            CaptureMode: CaptureMode,
            WindowTitleContains: string.IsNullOrWhiteSpace(WindowTitleContains) ? null : WindowTitleContains.Trim(),
            FallbackToDesktop: FallbackToDesktop,
            CaptureNarration: CaptureNarration,
            MicrophoneDeviceName: string.IsNullOrWhiteSpace(MicrophoneDeviceName) ? null : MicrophoneDeviceName.Trim(),
            QualityPreset: SelectedComposeQualityPreset,
            ExportStyle: SelectedExportStyle,
            ClipRules: CurrentSessionClips
                .OrderBy(x => x.Order)
                .Select(x => new DesktopTemplateClipRule(x.Order, x.Label, x.BannerText, x.IncludeNarration))
                .ToArray());

        await _demoTemplateService.SaveAsync(template);
        _lastRuntimeMessage = $"Template saved: {template.Name}";
        OnPropertyChanged(nameof(LastRuntimeMessage));
        await RefreshDemoTemplatesAsync();
        SelectedTemplateName = template.Name;
    }

    private Task ApplySelectedTemplateAsync()
    {
        if (!CanApplyDemoTemplate)
        {
            return Task.CompletedTask;
        }

        var template = DemoTemplates.FirstOrDefault(x => string.Equals(x.Name, SelectedTemplateName, StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            _lastRuntimeMessage = "Template not found.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return Task.CompletedTask;
        }

        CaptureMode = template.CaptureMode;
        WindowTitleContains = template.WindowTitleContains ?? string.Empty;
        FallbackToDesktop = template.FallbackToDesktop;
        CaptureNarration = template.CaptureNarration;
        MicrophoneDeviceName = template.MicrophoneDeviceName ?? string.Empty;
        SelectedComposeQualityPreset = template.QualityPreset;
        SelectedExportStyle = template.ExportStyle;

        if (_snapshot.State == DemoStudio.Desktop.Core.Sessions.RecorderSessionState.Armed)
        {
            CurrentSessionClips.Clear();
            foreach (var rule in template.ClipRules.OrderBy(x => x.Order))
            {
                CurrentSessionClips.Add(new CurrentSessionClipItem(
                    sequence: rule.Order,
                    label: rule.Label,
                    durationDisplay: "00:00",
                    bannerText: rule.BannerText ?? string.Empty,
                    startSeconds: 0,
                    durationSeconds: 0)
                {
                    Order = rule.Order,
                    IncludeNarration = rule.IncludeNarration
                });
            }
        }

        _lastRuntimeMessage = $"Template applied: {template.Name}";
        OnPropertyChanged(nameof(LastRuntimeMessage));
        RaiseWorkflowAndClipState();
        return Task.CompletedTask;
    }

    private async Task DeleteSelectedTemplateAsync()
    {
        if (!CanDeleteDemoTemplate)
        {
            return;
        }

        var name = SelectedTemplateName;
        await _demoTemplateService.DeleteAsync(name);
        _lastRuntimeMessage = $"Template deleted: {name}";
        OnPropertyChanged(nameof(LastRuntimeMessage));
        await RefreshDemoTemplatesAsync();
    }
}
