using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class PublishWorkflowViewModel : INotifyPropertyChanged
{
    private readonly IDesktopPublishWorkflowUseCase _publishWorkflowUseCase;
    private readonly IDesktopShellIntegrationUseCase _shellIntegrationUseCase;
    private readonly DesktopDemoStepSynthesizer _demoStepSynthesizer;
    private readonly DesktopDemoNarrationGenerator _demoNarrationGenerator;
    private readonly DesktopTutorialExporter _tutorialExporter;
    private readonly ILogger<PublishWorkflowViewModel> _logger;
    private ProductionWorkspaceViewModel? _production;

    public PublishWorkflowViewModel(
        IDesktopPublishWorkflowUseCase publishWorkflowUseCase,
        IDesktopShellIntegrationUseCase shellIntegrationUseCase,
        DesktopDemoStepSynthesizer demoStepSynthesizer,
        DesktopDemoNarrationGenerator demoNarrationGenerator,
        DesktopTutorialExporter tutorialExporter,
        ILogger<PublishWorkflowViewModel> logger)
    {
        _publishWorkflowUseCase = publishWorkflowUseCase ?? throw new ArgumentNullException(nameof(publishWorkflowUseCase));
        _shellIntegrationUseCase = shellIntegrationUseCase ?? throw new ArgumentNullException(nameof(shellIntegrationUseCase));
        _demoStepSynthesizer = demoStepSynthesizer ?? throw new ArgumentNullException(nameof(demoStepSynthesizer));
        _demoNarrationGenerator = demoNarrationGenerator ?? throw new ArgumentNullException(nameof(demoNarrationGenerator));
        _tutorialExporter = tutorialExporter ?? throw new ArgumentNullException(nameof(tutorialExporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PublishStatus => _production?.PublishStatus ?? "Publish package not generated.";

    public string ShareSummary => _production?.ShareSummary ?? "Share summary not generated yet.";

    public string LastPublishPackagePath => _production?.LastPublishPackagePath ?? string.Empty;

    public string LastTutorialHtmlPath => _production?.LastTutorialHtmlPath ?? string.Empty;

    public void AttachProductionWorkspace(ProductionWorkspaceViewModel production)
    {
        ArgumentNullException.ThrowIfNull(production);
        if (ReferenceEquals(_production, production))
        {
            return;
        }

        if (_production is not null)
        {
            _production.PropertyChanged -= OnProductionPropertyChanged;
        }

        _production = production;
        _production.PropertyChanged += OnProductionPropertyChanged;
        OnPropertyChanged(nameof(PublishStatus));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(LastPublishPackagePath));
        OnPropertyChanged(nameof(LastTutorialHtmlPath));
    }

    public async Task CreatePublishPackageAsync(
        DesktopPublishWorkflowRequest context,
        CancellationToken cancellationToken,
        Action<bool> setBusy,
        Action<string> setLastRuntimeMessage,
        Action<string> setSessionHistoryStatus,
        Func<string, string, string?, string> buildFailureDisplay)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(setBusy);
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setSessionHistoryStatus);
        ArgumentNullException.ThrowIfNull(buildFailureDisplay);

        var production = RequireProduction();
        setBusy(true);
        try
        {
            var result = await _publishWorkflowUseCase.CreateAsync(context, cancellationToken);
            production.PublishStatus = result.PublishStatus;
            setSessionHistoryStatus(result.SessionHistoryStatus);
            production.LastPublishPackagePath = string.Empty;
            production.LastTutorialHtmlPath = string.Empty;
            production.ShareSummary = "Share summary not generated yet.";

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.PackagePath))
            {
                production.LastPublishPackagePath = result.PackagePath!;
                production.LastTutorialHtmlPath = await TryGenerateTutorialAsync(
                    context,
                    result.PackageDirectoryPath,
                    cancellationToken).ConfigureAwait(false) ?? string.Empty;
                production.ShareSummary = result.ShareSummary ?? $"Demo package ready: {Path.GetFileName(result.PackagePath)}";
                _ = _shellIntegrationUseCase.RevealPath(result.PackagePath!);
            }

            setLastRuntimeMessage(
                !string.IsNullOrWhiteSpace(production.LastTutorialHtmlPath)
                    ? "Tutorial generated. You can now view or export it."
                    : result.RuntimeMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish package creation failed.");
            var failure = buildFailureDisplay("DS-DESK-PUB-001", "Publish package failed.", ex.Message);
            production.PublishStatus = failure;
            setLastRuntimeMessage(failure);
            setSessionHistoryStatus(failure);
        }
        finally
        {
            setBusy(false);
        }
    }

    public void CopyShareSummary(Action<string> setLastRuntimeMessage, Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            var packageName = string.IsNullOrWhiteSpace(LastPublishPackagePath) ? "-" : Path.GetFileName(LastPublishPackagePath);
            var summary = $"{ShareSummary} | Package: {packageName}";
            Clipboard.SetText(summary);
            setLastRuntimeMessage("Share summary copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy share summary failed.");
            setRuntimeFailure("DS-DESK-SHARE-001", "Copy share summary failed.", ex);
        }
    }

    public void OpenPublishZip(Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            var openResult = _shellIntegrationUseCase.RevealPath(LastPublishPackagePath);
            if (!openResult.Succeeded)
            {
                throw new InvalidOperationException(openResult.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open publish package failed.");
            setRuntimeFailure("DS-DESK-PUB-002", "Open package failed.", ex);
        }
    }

    public void ViewTutorial(Action<string> setLastRuntimeMessage, Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            var openResult = _shellIntegrationUseCase.OpenPath(LastTutorialHtmlPath);
            if (!openResult.Succeeded)
            {
                throw new InvalidOperationException(openResult.Message);
            }

            setLastRuntimeMessage("Opened tutorial in your default browser.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open tutorial failed.");
            setRuntimeFailure("DS-DESK-TUTOR-001", "Open tutorial failed.", ex);
        }
    }

    public void OpenComposeHealth(string healthPath, Action<string> setLastRuntimeMessage, Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(healthPath);
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            var openResult = _shellIntegrationUseCase.RevealPath(healthPath);
            if (!openResult.Succeeded)
            {
                throw new InvalidOperationException(openResult.Message);
            }

            setLastRuntimeMessage("Opened compose health snapshot.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open compose health failed.");
            setRuntimeFailure("DS-DESK-COMP-001", "Open compose health failed.", ex);
        }
    }

    public string GetComposeHealthPath(string? lastOutputPath, DesktopSessionRecord? selectedSessionRecord)
        => _publishWorkflowUseCase.GetComposeHealthPath(lastOutputPath, selectedSessionRecord);

    private async Task<string?> TryGenerateTutorialAsync(
        DesktopPublishWorkflowRequest context,
        string? packageDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageDirectoryPath) || !Directory.Exists(packageDirectoryPath))
        {
            return null;
        }

        try
        {
            var steps = await _demoStepSynthesizer.SynthesizeAsync(includeDiagnostics: false, cancellationToken).ConfigureAwait(false);
            var script = new DemoScript(steps);
            var narration = new DemoNarration(_demoNarrationGenerator.Generate(script));
            var tutorialPath = DesktopStoragePaths.GetTutorialHtmlPath(packageDirectoryPath);
            var title = context.SelectedSessionRecord is null
                ? "DemoStudio Recording"
                : $"Demo {context.SelectedSessionRecord.SessionId:N}";
            var tutorial = await _tutorialExporter.ExportTutorialHtml(script, narration, tutorialPath, title, cancellationToken).ConfigureAwait(false);
            return tutorial.HtmlPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tutorial export artifact generation failed for package directory {PackageDirectoryPath}.", packageDirectoryPath);
            return null;
        }
    }

    private ProductionWorkspaceViewModel RequireProduction()
    {
        return _production ?? throw new InvalidOperationException("Publish workflow is not attached to a production workspace.");
    }

    private void OnProductionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.PublishStatus), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(PublishStatus));
        }
        else if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.ShareSummary), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ShareSummary));
        }
        else if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.LastPublishPackagePath), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(LastPublishPackagePath));
        }
        else if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.LastTutorialHtmlPath), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(LastTutorialHtmlPath));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
