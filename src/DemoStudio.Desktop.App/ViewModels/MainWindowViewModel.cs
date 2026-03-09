using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly RecorderSessionEngine _sessionEngine;
    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly DesktopWindowCatalogService _windowCatalogService;
    private readonly DesktopLaunchProfileService _launchProfileService;
    private readonly DesktopTargetLauncher _targetLauncher;
    private readonly DesktopCapturePreflightService _preflightService;
    private readonly DesktopWindowFocusService _windowFocusService;
    private readonly DesktopSessionHistoryService _sessionHistoryService;
    private readonly DesktopDiagnosticsBundleService _diagnosticsBundleService;
    private readonly DesktopPerformanceMetricsService _performanceMetricsService;
    private readonly DesktopSmokeCheckService _smokeCheckService;
    private readonly DesktopComposeManifestService _composeManifestService;
    private readonly DesktopVideoComposeService _videoComposeService;
    private readonly DesktopNarrationCoordinator _narrationCoordinator;
    private readonly DesktopPublishPackageService _publishPackageService;
    private readonly OnboardingViewModel _onboarding;
    private readonly DesktopDemoTemplateService _demoTemplateService;
    private readonly DesktopSessionRecoveryService _sessionRecoveryService;
    private readonly DesktopPresenterViewService _presenterViewService;
    private readonly DesktopProcessRunner _processRunner;
    private readonly TargetingLaunchViewModel _targeting;
    private readonly SessionHistoryViewModel _history;
    private readonly ProductionWorkspaceViewModel _production;
    private readonly ClipCurationViewModel _curation;
    private readonly WorkflowStateViewModel _workflow;
    private readonly SessionStateViewModel _sessionState;
    private readonly CommandStateCoordinator _commandState = new();
    private readonly DesktopCaptureMediaCoordinator _captureMediaCoordinator;
    private readonly DesktopCaptureWatchdogCoordinator _captureWatchdogCoordinator;
    private readonly DesktopClipCurationCoordinator _clipCurationCoordinator;
    private readonly DesktopDependencyHealthService _dependencyHealthService;
    private readonly DesktopFfmpegOperationQueue _ffmpegOperationQueue;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private RecorderSessionSnapshot _snapshot;
    private bool _isBusy;
    private bool _startClipInFlight;
    private bool _startCancellationRequested;
    private CancellationTokenSource? _startClipCancellation;

    private string _lastRuntimeMessage = "Ready";

    private string _smokeCheckStatus = "Smoke check not run.";

    private string _performanceSummary = "Perf: no samples yet.";
    private readonly DispatcherTimer _liveClipTimer;
    private readonly DispatcherTimer _telemetryTimer;
    private readonly DispatcherTimer _draftAutosaveTimer;
    private string _lastDraftFingerprint = string.Empty;
    private string _memoryWorkingSetText = "Working Set: -";
    private string _memoryPrivateText = "Private Memory: -";
    private string _captureWriteRateText = "Capture Write: -";
    private string _captureFileSizeText = "Capture Size: -";
    private string _composeLastRunText = "Compose Last Run: -";
    private long _telemetryLastBytes;
    private DateTimeOffset _telemetryLastUtc = DateTimeOffset.MinValue;
    private string _performanceHealthLabel = "Healthy";
    private string _performanceHealthDetail = "All runtime metrics are within budget.";
    private string _performanceHealthBackground = "#EAF9EE";
    private string _performanceHealthBorder = "#9BD3A9";
    private DesktopDependencyHealthSnapshot _dependencyHealthSnapshot = DesktopDependencyHealthSnapshot.Uninitialized();
    private DateTimeOffset _dependencyHealthLastRefreshUtc = DateTimeOffset.MinValue;
    private bool _dependencyHealthRefreshInFlight;
    private int _telemetryRefreshInFlight;
    private int _draftAutosaveInFlight;
    private int _clipThumbnailBackfillInFlight;
    private long _lastBackgroundFailureTicks;
    private bool _isInitialized;
    private bool _isDisposed;

    private string _lastOutputPath
    {
        get => _sessionState.LastOutputPath;
        set => _sessionState.LastOutputPath = value;
    }

    private Guid _lastFinalizedSessionId
    {
        get => _sessionState.LastFinalizedSessionId;
        set => _sessionState.LastFinalizedSessionId = value;
    }

    private string? _lastFailureCode
    {
        get => _sessionState.LastFailureCode;
        set => _sessionState.LastFailureCode = value;
    }

    private string? _lastDiagnosticsPath
    {
        get => _sessionState.LastDiagnosticsPath;
        set => _sessionState.LastDiagnosticsPath = value;
    }

    public MainWindowViewModel(RecorderSessionEngine sessionEngine)
        : this(
            sessionEngine,
            new DesktopCaptureRuntime(new DesktopRecorderOptions()),
            new DesktopWindowCatalogService(),
            new DesktopLaunchProfileService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")),
            new DesktopTargetLauncher(),
            new DesktopCapturePreflightService(),
            new DesktopWindowFocusService(),
            new DesktopSessionHistoryService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")),
            new DesktopDiagnosticsBundleService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")),
            new DesktopPerformanceMetricsService(),
            new DesktopSmokeCheckService(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop"),
                "ffmpeg"),
            new DesktopComposeManifestService(),
            new DesktopVideoComposeService(),
            new DesktopPublishPackageService(),
            new DesktopOnboardingService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")),
            new DesktopDemoTemplateService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")),
            new DesktopSessionRecoveryService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")),
            new DesktopPresenterViewService())
    {
    }

    public MainWindowViewModel(
        RecorderSessionEngine sessionEngine,
        DesktopCaptureRuntime captureRuntime,
        DesktopWindowCatalogService windowCatalogService,
        DesktopLaunchProfileService launchProfileService,
        DesktopTargetLauncher targetLauncher,
        DesktopCapturePreflightService preflightService,
        DesktopWindowFocusService windowFocusService,
        DesktopSessionHistoryService sessionHistoryService,
        DesktopDiagnosticsBundleService diagnosticsBundleService,
        DesktopPerformanceMetricsService performanceMetricsService,
        DesktopSmokeCheckService smokeCheckService,
        DesktopComposeManifestService composeManifestService,
        DesktopVideoComposeService videoComposeService,
        DesktopPublishPackageService publishPackageService,
        DesktopOnboardingService onboardingService,
        DesktopDemoTemplateService demoTemplateService,
        DesktopSessionRecoveryService sessionRecoveryService,
        DesktopPresenterViewService presenterViewService,
        DesktopProcessRunner? processRunner = null,
        DesktopCaptureMediaCoordinator? captureMediaCoordinator = null,
        DesktopNarrationCoordinator? narrationCoordinator = null,
        DesktopCaptureWatchdogCoordinator? captureWatchdogCoordinator = null,
        DesktopClipCurationCoordinator? clipCurationCoordinator = null,
        DesktopDependencyHealthService? dependencyHealthService = null,
        DesktopFfmpegOperationQueue? ffmpegOperationQueue = null)
    {
        _sessionEngine = sessionEngine ?? throw new ArgumentNullException(nameof(sessionEngine));
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _windowCatalogService = windowCatalogService ?? throw new ArgumentNullException(nameof(windowCatalogService));
        _launchProfileService = launchProfileService ?? throw new ArgumentNullException(nameof(launchProfileService));
        _targetLauncher = targetLauncher ?? throw new ArgumentNullException(nameof(targetLauncher));
        _preflightService = preflightService ?? throw new ArgumentNullException(nameof(preflightService));
        _windowFocusService = windowFocusService ?? throw new ArgumentNullException(nameof(windowFocusService));
        _sessionHistoryService = sessionHistoryService ?? throw new ArgumentNullException(nameof(sessionHistoryService));
        _diagnosticsBundleService = diagnosticsBundleService ?? throw new ArgumentNullException(nameof(diagnosticsBundleService));
        _performanceMetricsService = performanceMetricsService ?? throw new ArgumentNullException(nameof(performanceMetricsService));
        _smokeCheckService = smokeCheckService ?? throw new ArgumentNullException(nameof(smokeCheckService));
        _composeManifestService = composeManifestService ?? throw new ArgumentNullException(nameof(composeManifestService));
        _videoComposeService = videoComposeService ?? throw new ArgumentNullException(nameof(videoComposeService));
        _publishPackageService = publishPackageService ?? throw new ArgumentNullException(nameof(publishPackageService));
        var onboardingServiceRequired = onboardingService ?? throw new ArgumentNullException(nameof(onboardingService));
        _onboarding = new OnboardingViewModel(onboardingServiceRequired);
        _onboarding.PropertyChanged += OnOnboardingPropertyChanged;
        _demoTemplateService = demoTemplateService ?? throw new ArgumentNullException(nameof(demoTemplateService));
        _sessionRecoveryService = sessionRecoveryService ?? throw new ArgumentNullException(nameof(sessionRecoveryService));
        _presenterViewService = presenterViewService ?? throw new ArgumentNullException(nameof(presenterViewService));
        _processRunner = processRunner ?? new DesktopProcessRunner();
        _targeting = new TargetingLaunchViewModel();
        _targeting.PropertyChanged += OnTargetingPropertyChanged;
        _history = new SessionHistoryViewModel();
        _history.PropertyChanged += OnHistoryPropertyChanged;
        _production = new ProductionWorkspaceViewModel();
        _production.PropertyChanged += OnProductionPropertyChanged;
        _curation = new ClipCurationViewModel();
        _curation.PropertyChanged += OnCurationPropertyChanged;
        _workflow = new WorkflowStateViewModel();
        _workflow.PropertyChanged += OnWorkflowPropertyChanged;
        _sessionState = new SessionStateViewModel();
        _sessionState.PropertyChanged += OnSessionStatePropertyChanged;
        _captureMediaCoordinator = captureMediaCoordinator ?? new DesktopCaptureMediaCoordinator(_captureRuntime, _processRunner);
        _narrationCoordinator = narrationCoordinator ?? new DesktopNarrationCoordinator(_captureRuntime, new DesktopClipNarrationService(), new DesktopAiNarrationService(), _processRunner);
        _captureWatchdogCoordinator = captureWatchdogCoordinator ?? new DesktopCaptureWatchdogCoordinator();
        _clipCurationCoordinator = clipCurationCoordinator ?? new DesktopClipCurationCoordinator();
        _dependencyHealthService = dependencyHealthService ?? new DesktopDependencyHealthService(_captureRuntime, _processRunner);
        _ffmpegOperationQueue = ffmpegOperationQueue ?? new DesktopFfmpegOperationQueue();
        _dependencyHealthSnapshot = _dependencyHealthService.Current;
        _snapshot = _sessionEngine.Snapshot();
        _liveClipTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _liveClipTimer.Tick += OnLiveClipTimerTick;
        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryTimer.Tick += OnTelemetryTimerTick;
        _draftAutosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _draftAutosaveTimer.Tick += OnDraftAutosaveTimerTick;
        var defaults = _captureRuntime.GetDefaultTargetSettings();
        _targeting.InitializeFromDefaults(defaults);
        _production.InitializeMicrophoneSettings(_captureRuntime.CaptureMicrophoneEnabled, _captureRuntime.MicrophoneDeviceName ?? string.Empty);
        RefreshWorkflowState();

        InitializeCommands();
    }

    public async Task<MainWindowInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return new MainWindowInitializationResult(false, new[] { "View model is disposed." });
        }

        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return MainWindowInitializationResult.Success();
            }

            var failures = new List<string>();
            await TryInitializeStepAsync("Window catalog", () =>
            {
                RefreshWindowCandidates();
                return Task.CompletedTask;
            }, failures);
            await TryInitializeStepAsync("Launch profiles", RefreshLaunchProfilesAsync, failures);
            await TryInitializeStepAsync("Preflight", RunPreflightAsync, failures);
            await TryInitializeStepAsync("Session history", RefreshSessionHistoryAsync, failures);
            await TryInitializeStepAsync("Demo templates", RefreshDemoTemplatesAsync, failures);
            await TryInitializeStepAsync("Session recovery", RestoreDraftStateAsync, failures);
            await TryInitializeStepAsync("Dependency health", () => RefreshDependencyHealthAsync(force: true), failures);

            _onboarding.Initialize();
            OnPropertyChanged(nameof(IsOnboardingVisible));

            if (!_draftAutosaveTimer.IsEnabled)
            {
                _draftAutosaveTimer.Start();
            }

            if (!_telemetryTimer.IsEnabled)
            {
                _telemetryTimer.Start();
            }

            _isInitialized = true;
            return failures.Count == 0
                ? MainWindowInitializationResult.Success()
                : new MainWindowInitializationResult(false, failures);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task TryInitializeStepAsync(string stepName, Func<Task> step, List<string> failures)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            failures.Add($"{stepName}: {ex.Message}");
            await ReportBackgroundFailureAsync(
                "DS-DESK-INIT-001",
                $"Startup step failed: {stepName}.",
                ex,
                TimeSpan.FromSeconds(2));
        }
    }

    private void InitializeCommands()
    {
        StartClipCommand = new RelayCommand(_ => StartClipAsync(), _ => CanStartClip);
        PauseClipCommand = new RelayCommand(_ => PauseClip(), _ => CanPauseClip);
        StopSessionCommand = new RelayCommand(_ => StopSessionAsync(), _ => CanStopSession);
        RefreshWindowsCommand = new RelayCommand(_ => RefreshWindowCandidates(), _ => CanEditTargetSettings);
        UseSelectedWindowCommand = new RelayCommand(_ => UseSelectedWindowAsync(), _ => CanUseSelectedWindow);
        FocusTargetCommand = new RelayCommand(_ => FocusTargetAsync(), _ => CanFocusTarget);
        RefreshLaunchProfilesCommand = new RelayCommand(_ => RefreshLaunchProfilesAsync(), _ => CanEditTargetSettings);
        SaveLaunchProfileCommand = new RelayCommand(_ => SaveLaunchProfileAsync(), _ => CanSaveLaunchProfile);
        DeleteLaunchProfileCommand = new RelayCommand(_ => DeleteSelectedLaunchProfileAsync(), _ => CanDeleteLaunchProfile);
        LoadLaunchProfileCommand = new RelayCommand(_ => LoadSelectedLaunchProfile(), _ => CanLoadLaunchProfile);
        LaunchTargetCommand = new RelayCommand(_ => LaunchTargetAsync(), _ => CanLaunchTarget);
        RunPreflightCommand = new RelayCommand(_ => RunPreflightAsync(), _ => CanEditTargetSettings);
        RefreshHistoryCommand = new RelayCommand(_ => RefreshSessionHistoryAsync(), _ => true);
        OpenSessionFolderCommand = new RelayCommand(_ => OpenSelectedSessionFolder(), _ => CanOpenSelectedSessionFolder);
        DeleteSessionCommand = new RelayCommand(_ => DeleteSelectedSessionAsync(), _ => CanDeleteSelectedSession);
        RunSmokeCheckCommand = new RelayCommand(_ => RunSmokeCheckAsync(), _ => CanRunSmokeCheck);
        MoveClipUpCommand = new RelayCommand(_ => MoveSelectedClipUp(), _ => CanMoveSelectedClipUp);
        MoveClipDownCommand = new RelayCommand(_ => MoveSelectedClipDown(), _ => CanMoveSelectedClipDown);
        ComposeManifestCommand = new RelayCommand(_ => ComposeVideoAsync(), _ => CanComposeManifest);
        PrimaryWorkflowCommand = new RelayCommand(_ => ExecutePrimaryWorkflowAsync(), _ => CanExecutePrimaryWorkflow);
        OpenCurationCommand = new RelayCommand(_ => ExpandClipCuration(), _ => CanOpenCuration);
        OpenLatestOutputFolderCommand = new RelayCommand(_ => OpenLatestOutputFolder(), _ => CanOpenLatestOutputFolder);
        CreatePublishPackageCommand = new RelayCommand(_ => CreatePublishPackageAsync(), _ => CanCreatePublishPackage);
        SaveDemoTemplateCommand = new RelayCommand(_ => SaveDemoTemplateAsync(), _ => CanSaveDemoTemplate);
        ApplyDemoTemplateCommand = new RelayCommand(_ => ApplySelectedTemplateAsync(), _ => CanApplyDemoTemplate);
        DeleteDemoTemplateCommand = new RelayCommand(_ => DeleteSelectedTemplateAsync(), _ => CanDeleteDemoTemplate);
        RefreshDemoTemplatesCommand = new RelayCommand(_ => RefreshDemoTemplatesAsync(), _ => true);
        CopyShareSummaryCommand = new RelayCommand(_ => CopyShareSummary(), _ => CanCopyShareSummary);
        OpenPublishZipCommand = new RelayCommand(_ => OpenPublishZip(), _ => CanOpenPublishZip);
        OpenComposeHealthCommand = new RelayCommand(_ => OpenComposeHealth(), _ => CanOpenComposeHealth);
        CopyFixHintCommand = new RelayCommand(_ => CopySelectedFixHint(), _ => CanCopyFixHint);
        StartNewSessionCommand = new RelayCommand(_ => StartNewSessionAsync(), _ => CanStartNewSession);
        SaveSessionDraftCommand = new RelayCommand(_ => SaveSessionDraftAsync(), _ => CanSaveSessionDraft);
        CloseSessionCommand = new RelayCommand(_ => CloseSessionAsync(), _ => CanCloseSession);
        PlayClipPreviewCommand = new RelayCommand(
            parameter => PlayClipPreviewAsync(parameter as CurrentSessionClipItem),
            parameter => parameter is CurrentSessionClipItem && CanPlayClipPreview);
        RecordNarrationCommand = new RelayCommand(_ => RecordNarrationForSelectedClipAsync(), _ => CanRecordNarration);
        GenerateAiNarrationCommand = new RelayCommand(_ => GenerateAiNarrationForSelectedClipAsync(), _ => CanGenerateAiNarration);
        PlayNarrationCommand = new RelayCommand(_ => PlayNarrationForSelectedClip(), _ => CanPlayNarration);
        ClearNarrationCommand = new RelayCommand(_ => ClearNarrationForSelectedClip(), _ => CanClearNarration);

        _commandState.Register(
            StartClipCommand,
            PauseClipCommand,
            StopSessionCommand,
            RefreshWindowsCommand,
            UseSelectedWindowCommand,
            FocusTargetCommand,
            RefreshLaunchProfilesCommand,
            SaveLaunchProfileCommand,
            DeleteLaunchProfileCommand,
            LoadLaunchProfileCommand,
            LaunchTargetCommand,
            RunPreflightCommand,
            RefreshHistoryCommand,
            OpenSessionFolderCommand,
            DeleteSessionCommand,
            RunSmokeCheckCommand,
            MoveClipUpCommand,
            MoveClipDownCommand,
            ComposeManifestCommand,
            PrimaryWorkflowCommand,
            OpenCurationCommand,
            OpenLatestOutputFolderCommand,
            CreatePublishPackageCommand,
            SaveDemoTemplateCommand,
            ApplyDemoTemplateCommand,
            DeleteDemoTemplateCommand,
            RefreshDemoTemplatesCommand,
            CopyShareSummaryCommand,
            OpenPublishZipCommand,
            OpenComposeHealthCommand,
            CopyFixHintCommand,
            StartNewSessionCommand,
            SaveSessionDraftCommand,
            CloseSessionCommand,
            PlayClipPreviewCommand,
            RecordNarrationCommand,
            GenerateAiNarrationCommand,
            PlayNarrationCommand,
            ClearNarrationCommand);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand StartClipCommand { get; private set; } = null!;

    public ICommand PauseClipCommand { get; private set; } = null!;

    public ICommand StopSessionCommand { get; private set; } = null!;

    public ICommand RefreshWindowsCommand { get; private set; } = null!;

    public ICommand UseSelectedWindowCommand { get; private set; } = null!;

    public ICommand FocusTargetCommand { get; private set; } = null!;

    public ICommand RefreshLaunchProfilesCommand { get; private set; } = null!;

    public ICommand SaveLaunchProfileCommand { get; private set; } = null!;

    public ICommand DeleteLaunchProfileCommand { get; private set; } = null!;

    public ICommand LoadLaunchProfileCommand { get; private set; } = null!;

    public ICommand LaunchTargetCommand { get; private set; } = null!;

    public ICommand RunPreflightCommand { get; private set; } = null!;

    public ICommand RefreshHistoryCommand { get; private set; } = null!;

    public ICommand OpenSessionFolderCommand { get; private set; } = null!;

    public ICommand DeleteSessionCommand { get; private set; } = null!;

    public ICommand RunSmokeCheckCommand { get; private set; } = null!;

    public ICommand MoveClipUpCommand { get; private set; } = null!;

    public ICommand MoveClipDownCommand { get; private set; } = null!;

    public ICommand ComposeManifestCommand { get; private set; } = null!;

    public ICommand PrimaryWorkflowCommand { get; private set; } = null!;

    public ICommand OpenCurationCommand { get; private set; } = null!;

    public ICommand OpenLatestOutputFolderCommand { get; private set; } = null!;

    public ICommand CreatePublishPackageCommand { get; private set; } = null!;

    public ICommand SaveDemoTemplateCommand { get; private set; } = null!;
    public ICommand ApplyDemoTemplateCommand { get; private set; } = null!;
    public ICommand DeleteDemoTemplateCommand { get; private set; } = null!;
    public ICommand RefreshDemoTemplatesCommand { get; private set; } = null!;
    public ICommand CopyShareSummaryCommand { get; private set; } = null!;
    public ICommand OpenPublishZipCommand { get; private set; } = null!;
    public ICommand OpenComposeHealthCommand { get; private set; } = null!;
    public ICommand CopyFixHintCommand { get; private set; } = null!;
    public ICommand StartNewSessionCommand { get; private set; } = null!;
    public ICommand SaveSessionDraftCommand { get; private set; } = null!;
    public ICommand CloseSessionCommand { get; private set; } = null!;
    public ICommand PlayClipPreviewCommand { get; private set; } = null!;
    public ICommand RecordNarrationCommand { get; private set; } = null!;
    public ICommand GenerateAiNarrationCommand { get; private set; } = null!;
    public ICommand PlayNarrationCommand { get; private set; } = null!;
    public ICommand ClearNarrationCommand { get; private set; } = null!;

    public string WindowTitle => "DemoStudio Recorder Desktop";

    public string SessionId => _snapshot.SessionId.ToString();

    public string StateText => _snapshot.State.ToString();

    public int ClipCount => Math.Max(_snapshot.ClipCount, CurrentSessionClips.Count);

    public string DurationText
    {
        get
        {
            var snapshotSeconds = _snapshot.CapturedDuration.TotalSeconds;
            var clipsSeconds = CurrentSessionClips.Sum(x => Math.Max(0d, x.DurationSeconds));
            var total = TimeSpan.FromSeconds(Math.Max(snapshotSeconds, clipsSeconds));
            return $"{total:mm\\:ss}";
        }
    }

    public bool IsBusy => _isBusy;

    public string LastOutputPath => _lastOutputPath;

    public string LastRuntimeMessage => _lastRuntimeMessage;

    public string PerformanceSummary => _performanceSummary;
    public string MemoryWorkingSetText => _memoryWorkingSetText;
    public string MemoryPrivateText => _memoryPrivateText;
    public string CaptureWriteRateText => _captureWriteRateText;
    public string CaptureFileSizeText => _captureFileSizeText;
    public string ComposeLastRunText => _composeLastRunText;
    public string PerformanceHealthLabel => _performanceHealthLabel;
    public string PerformanceHealthDetail => _performanceHealthDetail;
    public string PerformanceHealthBackground => _performanceHealthBackground;
    public string PerformanceHealthBorder => _performanceHealthBorder;
    public string DependencyHealthLabel => _dependencyHealthSnapshot.IsHealthy ? "Dependencies: Healthy" : "Dependencies: Degraded";
    public string DependencyHealthDetail => _dependencyHealthSnapshot.Summary;
    public string DependencyHealthBackground => _dependencyHealthSnapshot.IsHealthy ? "#EAF9EE" : "#FDECEC";
    public string DependencyHealthBorder => _dependencyHealthSnapshot.IsHealthy ? "#9BD3A9" : "#E09A9A";

    public string StorageRoot => _captureRuntime.StorageRoot;

    public string ReadinessLastChecked
    {
        get => _targeting.ReadinessLastChecked;
        private set => _targeting.ReadinessLastChecked = value;
    }

    public bool IsClipCurationExpanded
    {
        get => _curation.IsClipCurationExpanded;
        set
        {
            if (value == _curation.IsClipCurationExpanded)
            {
                return;
            }

            _curation.IsClipCurationExpanded = value;
        }
    }

    public bool CanEditTargetSettings => !_isBusy && _snapshot.State == RecorderSessionState.Armed;

    public string CaptureMode
    {
        get => _targeting.CaptureMode;
        set
        {
            _targeting.CaptureMode = value;
        }
    }

    public string WindowTitleContains
    {
        get => _targeting.WindowTitleContains;
        set
        {
            _targeting.WindowTitleContains = value;
        }
    }

    public string WindowProcessName
    {
        get => _targeting.WindowProcessName;
        set
        {
            _targeting.WindowProcessName = value;
        }
    }

    public string WindowHandleHex
    {
        get => _targeting.WindowHandleHex;
        set
        {
            _targeting.WindowHandleHex = value;
        }
    }

    public bool FallbackToDesktop
    {
        get => _targeting.FallbackToDesktop;
        set
        {
            _targeting.FallbackToDesktop = value;
        }
    }

    public bool CaptureNarration
    {
        get => _production.CaptureNarration;
        set
        {
            if (value == _production.CaptureNarration)
            {
                return;
            }

            _production.CaptureNarration = value;
            _captureRuntime.SetMicrophoneCapture(_production.CaptureNarration, _production.MicrophoneDeviceName);
        }
    }

    public string MicrophoneDeviceName
    {
        get => _production.MicrophoneDeviceName;
        set
        {
            if (value == _production.MicrophoneDeviceName)
            {
                return;
            }

            _production.MicrophoneDeviceName = value;
            _captureRuntime.SetMicrophoneCapture(_production.CaptureNarration, _production.MicrophoneDeviceName);
        }
    }

    public bool PresenterViewEnabled
    {
        get => _production.PresenterViewEnabled;
        set
        {
            _production.PresenterViewEnabled = value;
        }
    }

    public string PresenterNotes
    {
        get => _production.PresenterNotes;
        set
        {
            _production.PresenterNotes = value;
        }
    }

    public string AiNarrationProvider
    {
        get => _production.AiNarrationProvider;
        set
        {
            _production.AiNarrationProvider = value;
        }
    }

    public string AiNarrationBaseUrl
    {
        get => _production.AiNarrationBaseUrl;
        set
        {
            _production.AiNarrationBaseUrl = value;
        }
    }

    public string AiNarrationModel
    {
        get => _production.AiNarrationModel;
        set
        {
            _production.AiNarrationModel = value;
        }
    }

    public string AiNarrationVoice
    {
        get => _production.AiNarrationVoice;
        set
        {
            _production.AiNarrationVoice = value;
        }
    }

    public string AiNarrationApiKey
    {
        get => _production.AiNarrationApiKey;
        set
        {
            _production.AiNarrationApiKey = value;
        }
    }

    public bool AiAutoTrimScript
    {
        get => _production.AiAutoTrimScript;
        set
        {
            _production.AiAutoTrimScript = value;
        }
    }

    public double AiWordsPerSecond
    {
        get => _production.AiWordsPerSecond;
        set
        {
            _production.AiWordsPerSecond = value;
        }
    }

    public ObservableCollection<DesktopWindowCandidate> WindowCandidates => _targeting.WindowCandidates;

    public DesktopWindowCandidate? SelectedWindowCandidate
    {
        get => _targeting.SelectedWindowCandidate;
        set
        {
            _targeting.SelectedWindowCandidate = value;
            if (CanEditTargetSettings && value is not null)
            {
                EnsureWindowTargetLockedFromSelection();
            }
        }
    }

    public bool CanUseSelectedWindow => CanEditTargetSettings && SelectedWindowCandidate is not null;
    public bool CanFocusTarget
        => !_isBusy
            && string.Equals(CaptureMode, "Window", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(WindowHandleHex) || !string.IsNullOrWhiteSpace(WindowTitleContains));

    public string WindowSelectionStatus
    {
        get => _targeting.WindowSelectionStatus;
        private set => _targeting.WindowSelectionStatus = value;
    }

    public string LaunchProfileName
    {
        get => _targeting.LaunchProfileName;
        set
        {
            _targeting.LaunchProfileName = value;
        }
    }

    public string LaunchExecutablePath
    {
        get => _targeting.LaunchExecutablePath;
        set
        {
            _targeting.LaunchExecutablePath = value;
        }
    }

    public string LaunchArguments
    {
        get => _targeting.LaunchArguments;
        set
        {
            _targeting.LaunchArguments = value;
        }
    }

    public string LaunchWorkingDirectory
    {
        get => _targeting.LaunchWorkingDirectory;
        set
        {
            _targeting.LaunchWorkingDirectory = value;
        }
    }

    public int LaunchStartupDelaySeconds
    {
        get => _targeting.LaunchStartupDelaySeconds;
        set
        {
            _targeting.LaunchStartupDelaySeconds = value;
        }
    }

    public List<DesktopLaunchProfile> LaunchProfiles => _targeting.LaunchProfiles;

    public DesktopLaunchProfile? SelectedLaunchProfile
    {
        get => _targeting.SelectedLaunchProfile;
        set
        {
            _targeting.SelectedLaunchProfile = value;
        }
    }

    public string LaunchStatus
    {
        get => _targeting.LaunchStatus;
        private set => _targeting.LaunchStatus = value;
    }

    public string PreflightStatus
    {
        get => _targeting.PreflightStatus;
        private set => _targeting.PreflightStatus = value;
    }

    public SessionHistoryViewModel History => _history;

    public ObservableCollection<DesktopSessionRecord> SessionHistory => _history.Records;

    public DesktopSessionRecord? SelectedSessionRecord
    {
        get => _history.SelectedRecord;
        set
        {
            _history.SelectedRecord = value;
        }
    }

    public string SessionHistoryStatus
    {
        get => _history.Status;
        private set => _history.Status = value;
    }

    public ObservableCollection<DesktopDemoTemplate> DemoTemplates => _production.DemoTemplates;

    public string TemplateName
    {
        get => _production.TemplateName;
        set
        {
            _production.TemplateName = value;
        }
    }

    public string SelectedTemplateName
    {
        get => _production.SelectedTemplateName;
        set
        {
            _production.SelectedTemplateName = value;
        }
    }

    public bool CanSaveDemoTemplate => !_isBusy && !string.IsNullOrWhiteSpace(TemplateName);
    public bool CanApplyDemoTemplate => !_isBusy && !string.IsNullOrWhiteSpace(SelectedTemplateName);
    public bool CanDeleteDemoTemplate => !_isBusy && !string.IsNullOrWhiteSpace(SelectedTemplateName);

    public bool CanOpenSelectedSessionFolder => SelectedSessionRecord is not null && !string.IsNullOrWhiteSpace(SelectedSessionRecord.RawVideoPath);

    public bool CanDeleteSelectedSession => SelectedSessionRecord is not null;

    public bool CanRunSmokeCheck => !_isBusy && _snapshot.State == RecorderSessionState.Armed;
    public bool CanCopyFixHint => !_isBusy && SelectedSessionRecord is not null && !string.IsNullOrWhiteSpace(SelectedSessionRecord.FixHint);

    public string SmokeCheckStatus => _smokeCheckStatus;

    public string NextClipLabel
    {
        get => _curation.NextClipLabel;
        set
        {
            _curation.NextClipLabel = value;
        }
    }

    public ObservableCollection<CurrentSessionClipItem> CurrentSessionClips => _curation.CurrentSessionClips;

    public CurrentSessionClipItem? SelectedCurrentSessionClip
    {
        get => _curation.SelectedCurrentSessionClip;
        set
        {
            _curation.SelectedCurrentSessionClip = value;
        }
    }

    public bool CanMoveSelectedClipUp
        => !_isBusy
            && SelectedCurrentSessionClip is not null
            && CurrentSessionClips.IndexOf(SelectedCurrentSessionClip) > 0;

    public bool CanMoveSelectedClipDown
        => !_isBusy
            && SelectedCurrentSessionClip is not null
            && CurrentSessionClips.IndexOf(SelectedCurrentSessionClip) >= 0
            && CurrentSessionClips.IndexOf(SelectedCurrentSessionClip) < CurrentSessionClips.Count - 1;

    public bool CanComposeManifest
        => !_isBusy
            && CurrentSessionClips.Count > 0
            && !string.IsNullOrWhiteSpace(_lastOutputPath)
            && _lastOutputPath != "-"
            && File.Exists(_lastOutputPath)
            && _snapshot.State != RecorderSessionState.Recording;

    public bool CanOpenCuration => !_isBusy && CurrentSessionClips.Count > 0;
    public bool CanOpenLatestOutputFolder => !_isBusy && !string.IsNullOrWhiteSpace(_lastOutputPath) && _lastOutputPath != "-";
    public bool HasSessionActions => !_isBusy && _lastFinalizedSessionId != Guid.Empty;
    public bool CanSaveSessionDraft
        => !_isBusy
            && (_snapshot.State is RecorderSessionState.Recording or RecorderSessionState.Paused
                || CurrentSessionClips.Count > 0
                || _lastOutputPath != "-");
    public bool CanCloseSession
        => !_isBusy
            && _snapshot.State != RecorderSessionState.Recording
            && _snapshot.State != RecorderSessionState.Paused
            && (CurrentSessionClips.Count > 0 || _lastOutputPath != "-" || _lastFinalizedSessionId != Guid.Empty);
    public bool CanStartNewSession
        => !_isBusy
            && _snapshot.State != RecorderSessionState.Recording
            && _snapshot.State != RecorderSessionState.Paused
            && (CurrentSessionClips.Count > 0 || _lastOutputPath != "-" || _lastFinalizedSessionId != Guid.Empty);
    public bool CanCreatePublishPackage
        => !_isBusy
            && CurrentSessionClips.Count > 0
            && !string.IsNullOrWhiteSpace(_lastOutputPath)
            && _lastOutputPath != "-"
            && File.Exists(_lastOutputPath)
            && _snapshot.State != RecorderSessionState.Recording;
    public bool CanPlayClipPreview
        => !_isBusy
            && !string.IsNullOrWhiteSpace(_lastOutputPath)
            && _lastOutputPath != "-"
            && File.Exists(_lastOutputPath);
    public bool CanRecordNarration
        => !_isBusy
            && _snapshot.State != RecorderSessionState.Recording
            && SelectedCurrentSessionClip is not null
            && SelectedCurrentSessionClip.DurationSeconds > 0.15d;
    public bool CanPlayNarration
        => !_isBusy
            && SelectedCurrentSessionClip is not null
            && SelectedCurrentSessionClip.HasNarration;
    public bool CanGenerateAiNarration
        => !_isBusy
            && _snapshot.State != RecorderSessionState.Recording
            && (SelectedCurrentSessionClip?.DurationSeconds > 0.15d
                || CurrentSessionClips.Any(x => x.DurationSeconds > 0.15d));
    public bool CanClearNarration
        => !_isBusy
            && SelectedCurrentSessionClip is not null
            && !string.IsNullOrWhiteSpace(SelectedCurrentSessionClip.NarrationAudioPath);
    public IReadOnlyList<string> ComposeQualityPresets => _production.ComposeQualityPresets;
    public IReadOnlyList<string> ExportStylePresets => _production.ExportStylePresets;

    public string SelectedComposeQualityPreset
    {
        get => _production.SelectedComposeQualityPreset;
        set
        {
            _production.SelectedComposeQualityPreset = value;
        }
    }

    public string SelectedExportStyle
    {
        get => _production.SelectedExportStyle;
        set
        {
            _production.SelectedExportStyle = value;
        }
    }

    public string ComposeStatus
    {
        get => _production.ComposeStatus;
        private set => _production.ComposeStatus = value;
    }

    public string PublishStatus
    {
        get => _production.PublishStatus;
        private set => _production.PublishStatus = value;
    }

    public string ShareSummary
    {
        get => _production.ShareSummary;
        private set => _production.ShareSummary = value;
    }
    public bool CanCopyShareSummary => !_isBusy && !string.IsNullOrWhiteSpace(_production.ShareSummary);
    public bool CanOpenPublishZip => !_isBusy && !string.IsNullOrWhiteSpace(_production.LastPublishPackagePath) && File.Exists(_production.LastPublishPackagePath);
    public bool CanOpenComposeHealth => !_isBusy && File.Exists(GetComposeHealthPath());
    public bool IsOnboardingVisible => _onboarding.IsVisible;
    public TargetingLaunchViewModel Targeting => _targeting;
    public ProductionWorkspaceViewModel Production => _production;
    public ClipCurationViewModel Curation => _curation;
    public SessionStateViewModel SessionState => _sessionState;
    public OnboardingViewModel Onboarding => _onboarding;
    public int OnboardingStepNumber => _onboarding.StepNumber;
    public string OnboardingTitle => _onboarding.Title;
    public string OnboardingDetail => _onboarding.Detail;
    public string OnboardingNextLabel => _onboarding.NextLabel;
    public bool CanPreviousOnboardingStep => _onboarding.CanMovePrevious;
    public bool CanNextOnboardingStep => _onboarding.CanMoveNext;

    public bool CanSaveLaunchProfile => CanEditTargetSettings && !string.IsNullOrWhiteSpace(LaunchProfileName);

    public bool CanDeleteLaunchProfile => CanEditTargetSettings && SelectedLaunchProfile is not null;

    public bool CanLoadLaunchProfile => CanEditTargetSettings && SelectedLaunchProfile is not null;

    public bool CanLaunchTarget => CanEditTargetSettings && !string.IsNullOrWhiteSpace(LaunchExecutablePath);

    public WorkflowStateViewModel Workflow => _workflow;
    public string GuidanceText => _workflow.GuidanceText;
    public string WorkflowStepTitle => _workflow.WorkflowStepTitle;
    public string WorkflowStepDetail => _workflow.WorkflowStepDetail;
    public string PrimaryWorkflowActionText => _workflow.PrimaryWorkflowActionText;
    public bool CanExecutePrimaryWorkflow => _workflow.CanExecutePrimaryWorkflow;

    public bool CanStartClip
    {
        get
        {
            if (_isBusy)
            {
                return false;
            }

            if (_snapshot.State == RecorderSessionState.Paused)
            {
                return true;
            }

            if (_snapshot.State != RecorderSessionState.Armed)
            {
                return false;
            }

            if (string.Equals(CaptureMode, "Desktop", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(WindowHandleHex)
                || SelectedWindowCandidate is not null
                || !string.IsNullOrWhiteSpace(WindowTitleContains);
        }
    }

    public bool CanPauseClip => !_isBusy && _snapshot.State == RecorderSessionState.Recording;

    public bool CanStopSession
        => _startClipInFlight
            || (!_isBusy && _snapshot.State is RecorderSessionState.Armed or RecorderSessionState.Recording or RecorderSessionState.Paused);

    public string Step1Background => _workflow.Step1Background;
    public string Step2Background => _workflow.Step2Background;
    public string Step3Background => _workflow.Step3Background;
    public string Step4Background => _workflow.Step4Background;
    public string Step1TextColor => _workflow.Step1TextColor;
    public string Step2TextColor => _workflow.Step2TextColor;
    public string Step3TextColor => _workflow.Step3TextColor;
    public string Step4TextColor => _workflow.Step4TextColor;

    private void SetBusy(bool busy)
    {
        if (_isBusy == busy)
        {
            return;
        }

        _isBusy = busy;
        RefreshWorkflowState();
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanStartClip));
        OnPropertyChanged(nameof(CanPauseClip));
        OnPropertyChanged(nameof(CanStopSession));
        OnPropertyChanged(nameof(WorkflowStepTitle));
        OnPropertyChanged(nameof(WorkflowStepDetail));
        OnPropertyChanged(nameof(PrimaryWorkflowActionText));
        OnPropertyChanged(nameof(CanExecutePrimaryWorkflow));
        OnPropertyChanged(nameof(CanEditTargetSettings));
        OnPropertyChanged(nameof(CanUseSelectedWindow));
        OnPropertyChanged(nameof(CanFocusTarget));
        OnPropertyChanged(nameof(CanSaveLaunchProfile));
        OnPropertyChanged(nameof(CanDeleteLaunchProfile));
        OnPropertyChanged(nameof(CanLoadLaunchProfile));
        OnPropertyChanged(nameof(CanLaunchTarget));
        OnPropertyChanged(nameof(PreflightStatus));
        OnPropertyChanged(nameof(ReadinessLastChecked));
        OnPropertyChanged(nameof(CanOpenSelectedSessionFolder));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(CanCopyFixHint));
        OnPropertyChanged(nameof(CanSaveDemoTemplate));
        OnPropertyChanged(nameof(CanApplyDemoTemplate));
        OnPropertyChanged(nameof(CanDeleteDemoTemplate));
        OnPropertyChanged(nameof(CanRunSmokeCheck));
        OnPropertyChanged(nameof(SmokeCheckStatus));
        OnPropertyChanged(nameof(NextClipLabel));
        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(SelectedCurrentSessionClip));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
        OnPropertyChanged(nameof(CanComposeManifest));
        OnPropertyChanged(nameof(CanOpenCuration));
        OnPropertyChanged(nameof(CanOpenLatestOutputFolder));
        OnPropertyChanged(nameof(HasSessionActions));
        OnPropertyChanged(nameof(CanSaveSessionDraft));
        OnPropertyChanged(nameof(CanCloseSession));
        OnPropertyChanged(nameof(CanStartNewSession));
        OnPropertyChanged(nameof(CanCreatePublishPackage));
        OnPropertyChanged(nameof(CanPlayClipPreview));
        OnPropertyChanged(nameof(CanRecordNarration));
        OnPropertyChanged(nameof(CanPlayNarration));
        OnPropertyChanged(nameof(CanGenerateAiNarration));
        OnPropertyChanged(nameof(CanClearNarration));
        OnPropertyChanged(nameof(SelectedComposeQualityPreset));
        OnPropertyChanged(nameof(SelectedExportStyle));
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step4Background));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step4TextColor));
        OnPropertyChanged(nameof(ComposeStatus));
        OnPropertyChanged(nameof(PublishStatus));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(CanCopyShareSummary));
        OnPropertyChanged(nameof(CanOpenPublishZip));
        OnPropertyChanged(nameof(CanOpenComposeHealth));
        OnPropertyChanged(nameof(MemoryWorkingSetText));
        OnPropertyChanged(nameof(MemoryPrivateText));
        OnPropertyChanged(nameof(CaptureWriteRateText));
        OnPropertyChanged(nameof(CaptureFileSizeText));
        OnPropertyChanged(nameof(ComposeLastRunText));
        OnPropertyChanged(nameof(PerformanceHealthLabel));
        OnPropertyChanged(nameof(PerformanceHealthDetail));
        OnPropertyChanged(nameof(PerformanceHealthBackground));
        OnPropertyChanged(nameof(PerformanceHealthBorder));
        OnPropertyChanged(nameof(DependencyHealthLabel));
        OnPropertyChanged(nameof(DependencyHealthDetail));
        OnPropertyChanged(nameof(DependencyHealthBackground));
        OnPropertyChanged(nameof(DependencyHealthBorder));
        OnPropertyChanged(nameof(IsOnboardingVisible));
        OnPropertyChanged(nameof(OnboardingStepNumber));
        OnPropertyChanged(nameof(OnboardingTitle));
        OnPropertyChanged(nameof(OnboardingDetail));
        OnPropertyChanged(nameof(OnboardingNextLabel));
        OnPropertyChanged(nameof(CanPreviousOnboardingStep));
        OnPropertyChanged(nameof(CanNextOnboardingStep));
        RaiseCommandState();
    }

    private void RaiseAll()
    {
        RefreshWorkflowState();
        OnPropertyChanged(nameof(SessionId));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ClipCount));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(CanStartClip));
        OnPropertyChanged(nameof(CanPauseClip));
        OnPropertyChanged(nameof(CanStopSession));
        OnPropertyChanged(nameof(WorkflowStepTitle));
        OnPropertyChanged(nameof(WorkflowStepDetail));
        OnPropertyChanged(nameof(PrimaryWorkflowActionText));
        OnPropertyChanged(nameof(CanExecutePrimaryWorkflow));
        OnPropertyChanged(nameof(LastOutputPath));
        OnPropertyChanged(nameof(LastRuntimeMessage));
        OnPropertyChanged(nameof(PerformanceSummary));
        OnPropertyChanged(nameof(MemoryWorkingSetText));
        OnPropertyChanged(nameof(MemoryPrivateText));
        OnPropertyChanged(nameof(CaptureWriteRateText));
        OnPropertyChanged(nameof(CaptureFileSizeText));
        OnPropertyChanged(nameof(ComposeLastRunText));
        OnPropertyChanged(nameof(PerformanceHealthLabel));
        OnPropertyChanged(nameof(PerformanceHealthDetail));
        OnPropertyChanged(nameof(PerformanceHealthBackground));
        OnPropertyChanged(nameof(PerformanceHealthBorder));
        OnPropertyChanged(nameof(DependencyHealthLabel));
        OnPropertyChanged(nameof(DependencyHealthDetail));
        OnPropertyChanged(nameof(DependencyHealthBackground));
        OnPropertyChanged(nameof(DependencyHealthBorder));
        OnPropertyChanged(nameof(StorageRoot));
        OnPropertyChanged(nameof(CanEditTargetSettings));
        OnPropertyChanged(nameof(CaptureMode));
        OnPropertyChanged(nameof(WindowTitleContains));
        OnPropertyChanged(nameof(WindowProcessName));
        OnPropertyChanged(nameof(WindowHandleHex));
        OnPropertyChanged(nameof(FallbackToDesktop));
        OnPropertyChanged(nameof(CaptureNarration));
        OnPropertyChanged(nameof(MicrophoneDeviceName));
        OnPropertyChanged(nameof(PresenterViewEnabled));
        OnPropertyChanged(nameof(PresenterNotes));
        OnPropertyChanged(nameof(WindowSelectionStatus));
        OnPropertyChanged(nameof(CanUseSelectedWindow));
        OnPropertyChanged(nameof(CanFocusTarget));
        OnPropertyChanged(nameof(LaunchProfileName));
        OnPropertyChanged(nameof(LaunchExecutablePath));
        OnPropertyChanged(nameof(LaunchArguments));
        OnPropertyChanged(nameof(LaunchWorkingDirectory));
        OnPropertyChanged(nameof(LaunchStartupDelaySeconds));
        OnPropertyChanged(nameof(LaunchProfiles));
        OnPropertyChanged(nameof(SelectedLaunchProfile));
        OnPropertyChanged(nameof(LaunchStatus));
        OnPropertyChanged(nameof(PreflightStatus));
        OnPropertyChanged(nameof(ReadinessLastChecked));
        OnPropertyChanged(nameof(CanSaveLaunchProfile));
        OnPropertyChanged(nameof(CanDeleteLaunchProfile));
        OnPropertyChanged(nameof(CanLoadLaunchProfile));
        OnPropertyChanged(nameof(CanLaunchTarget));
        OnPropertyChanged(nameof(SelectedSessionRecord));
        OnPropertyChanged(nameof(SessionHistory));
        OnPropertyChanged(nameof(SessionHistoryStatus));
        OnPropertyChanged(nameof(CanOpenSelectedSessionFolder));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(CanCopyFixHint));
        OnPropertyChanged(nameof(DemoTemplates));
        OnPropertyChanged(nameof(TemplateName));
        OnPropertyChanged(nameof(SelectedTemplateName));
        OnPropertyChanged(nameof(CanSaveDemoTemplate));
        OnPropertyChanged(nameof(CanApplyDemoTemplate));
        OnPropertyChanged(nameof(CanDeleteDemoTemplate));
        OnPropertyChanged(nameof(CanRunSmokeCheck));
        OnPropertyChanged(nameof(SmokeCheckStatus));
        OnPropertyChanged(nameof(NextClipLabel));
        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(SelectedCurrentSessionClip));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
        OnPropertyChanged(nameof(CanComposeManifest));
        OnPropertyChanged(nameof(CanOpenCuration));
        OnPropertyChanged(nameof(CanOpenLatestOutputFolder));
        OnPropertyChanged(nameof(HasSessionActions));
        OnPropertyChanged(nameof(CanSaveSessionDraft));
        OnPropertyChanged(nameof(CanCloseSession));
        OnPropertyChanged(nameof(CanStartNewSession));
        OnPropertyChanged(nameof(CanCreatePublishPackage));
        OnPropertyChanged(nameof(CanPlayClipPreview));
        OnPropertyChanged(nameof(CanRecordNarration));
        OnPropertyChanged(nameof(CanPlayNarration));
        OnPropertyChanged(nameof(CanGenerateAiNarration));
        OnPropertyChanged(nameof(CanClearNarration));
        OnPropertyChanged(nameof(SelectedComposeQualityPreset));
        OnPropertyChanged(nameof(SelectedExportStyle));
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step4Background));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step4TextColor));
        OnPropertyChanged(nameof(ComposeStatus));
        OnPropertyChanged(nameof(PublishStatus));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(CanCopyShareSummary));
        OnPropertyChanged(nameof(CanOpenPublishZip));
        OnPropertyChanged(nameof(CanOpenComposeHealth));
        OnPropertyChanged(nameof(IsOnboardingVisible));
        OnPropertyChanged(nameof(OnboardingStepNumber));
        OnPropertyChanged(nameof(OnboardingTitle));
        OnPropertyChanged(nameof(OnboardingDetail));
        OnPropertyChanged(nameof(OnboardingNextLabel));
        OnPropertyChanged(nameof(CanPreviousOnboardingStep));
        OnPropertyChanged(nameof(CanNextOnboardingStep));
        RaiseCommandState();
    }

    private void RaiseWorkflowAndClipState()
    {
        RefreshWorkflowState();
        OnPropertyChanged(nameof(SessionId));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ClipCount));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(CanStartClip));
        OnPropertyChanged(nameof(CanPauseClip));
        OnPropertyChanged(nameof(CanStopSession));
        OnPropertyChanged(nameof(WorkflowStepTitle));
        OnPropertyChanged(nameof(WorkflowStepDetail));
        OnPropertyChanged(nameof(PrimaryWorkflowActionText));
        OnPropertyChanged(nameof(CanExecutePrimaryWorkflow));
        OnPropertyChanged(nameof(LastOutputPath));
        OnPropertyChanged(nameof(LastRuntimeMessage));
        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(SelectedCurrentSessionClip));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
        OnPropertyChanged(nameof(CanComposeManifest));
        OnPropertyChanged(nameof(CanOpenCuration));
        OnPropertyChanged(nameof(CanOpenLatestOutputFolder));
        OnPropertyChanged(nameof(HasSessionActions));
        OnPropertyChanged(nameof(CanSaveSessionDraft));
        OnPropertyChanged(nameof(CanCloseSession));
        OnPropertyChanged(nameof(CanStartNewSession));
        OnPropertyChanged(nameof(CanCreatePublishPackage));
        OnPropertyChanged(nameof(CanPlayClipPreview));
        OnPropertyChanged(nameof(CanRecordNarration));
        OnPropertyChanged(nameof(CanPlayNarration));
        OnPropertyChanged(nameof(CanGenerateAiNarration));
        OnPropertyChanged(nameof(CanClearNarration));
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step4Background));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step4TextColor));
        OnPropertyChanged(nameof(ComposeStatus));
        OnPropertyChanged(nameof(PublishStatus));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(CanCopyShareSummary));
        OnPropertyChanged(nameof(CanOpenPublishZip));
        OnPropertyChanged(nameof(CanOpenComposeHealth));
        RaiseCommandState();
    }

    private void RaiseTargetingAndLaunchState()
    {
        OnPropertyChanged(nameof(CaptureMode));
        OnPropertyChanged(nameof(WindowTitleContains));
        OnPropertyChanged(nameof(WindowProcessName));
        OnPropertyChanged(nameof(WindowHandleHex));
        OnPropertyChanged(nameof(WindowSelectionStatus));
        OnPropertyChanged(nameof(CanUseSelectedWindow));
        OnPropertyChanged(nameof(CanFocusTarget));
        OnPropertyChanged(nameof(LaunchProfileName));
        OnPropertyChanged(nameof(LaunchExecutablePath));
        OnPropertyChanged(nameof(LaunchArguments));
        OnPropertyChanged(nameof(LaunchWorkingDirectory));
        OnPropertyChanged(nameof(LaunchStartupDelaySeconds));
        OnPropertyChanged(nameof(LaunchProfiles));
        OnPropertyChanged(nameof(SelectedLaunchProfile));
        OnPropertyChanged(nameof(LaunchStatus));
        OnPropertyChanged(nameof(PreflightStatus));
        OnPropertyChanged(nameof(ReadinessLastChecked));
        OnPropertyChanged(nameof(LastRuntimeMessage));
        OnPropertyChanged(nameof(CanSaveLaunchProfile));
        OnPropertyChanged(nameof(CanDeleteLaunchProfile));
        OnPropertyChanged(nameof(CanLoadLaunchProfile));
        OnPropertyChanged(nameof(CanLaunchTarget));
        RaiseCommandState();
    }

    private void RaiseProductionState()
    {
        OnPropertyChanged(nameof(CaptureNarration));
        OnPropertyChanged(nameof(MicrophoneDeviceName));
        OnPropertyChanged(nameof(PresenterViewEnabled));
        OnPropertyChanged(nameof(PresenterNotes));
        OnPropertyChanged(nameof(DemoTemplates));
        OnPropertyChanged(nameof(TemplateName));
        OnPropertyChanged(nameof(SelectedTemplateName));
        OnPropertyChanged(nameof(CanSaveDemoTemplate));
        OnPropertyChanged(nameof(CanApplyDemoTemplate));
        OnPropertyChanged(nameof(CanDeleteDemoTemplate));
        OnPropertyChanged(nameof(SelectedComposeQualityPreset));
        OnPropertyChanged(nameof(SelectedExportStyle));
        OnPropertyChanged(nameof(ComposeStatus));
        OnPropertyChanged(nameof(PublishStatus));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(CanCopyShareSummary));
        OnPropertyChanged(nameof(CanOpenPublishZip));
        OnPropertyChanged(nameof(CanOpenComposeHealth));
        OnPropertyChanged(nameof(AiNarrationProvider));
        OnPropertyChanged(nameof(AiNarrationBaseUrl));
        OnPropertyChanged(nameof(AiNarrationModel));
        OnPropertyChanged(nameof(AiNarrationVoice));
        OnPropertyChanged(nameof(AiNarrationApiKey));
        OnPropertyChanged(nameof(AiAutoTrimScript));
        OnPropertyChanged(nameof(AiWordsPerSecond));
        RaiseCommandState();
    }

    private void RaiseCurationState()
    {
        RefreshWorkflowState();
        OnPropertyChanged(nameof(IsClipCurationExpanded));
        OnPropertyChanged(nameof(NextClipLabel));
        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(SelectedCurrentSessionClip));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
        OnPropertyChanged(nameof(CanOpenCuration));
        OnPropertyChanged(nameof(CanRecordNarration));
        OnPropertyChanged(nameof(CanPlayNarration));
        OnPropertyChanged(nameof(CanGenerateAiNarration));
        OnPropertyChanged(nameof(CanClearNarration));
        RaiseCommandState();
    }

    private void RaiseOnboardingState()
    {
        OnPropertyChanged(nameof(IsOnboardingVisible));
        OnPropertyChanged(nameof(OnboardingStepNumber));
        OnPropertyChanged(nameof(OnboardingTitle));
        OnPropertyChanged(nameof(OnboardingDetail));
        OnPropertyChanged(nameof(OnboardingNextLabel));
        OnPropertyChanged(nameof(CanPreviousOnboardingStep));
        OnPropertyChanged(nameof(CanNextOnboardingStep));
        RaiseCommandState();
    }
    private void RaiseCommandState()
    {
        _commandState.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string BuildFailureDisplay(string code, string summary, string? detail = null)
    {
        var envelope = new DesktopFailureEnvelope(
            Code: code,
            Summary: summary,
            Detail: detail,
            FixHint: BuildFixHint(code));
        return envelope.ToDisplayText();
    }

    private void SetRuntimeFailure(string code, string summary, Exception? exception = null)
    {
        _lastRuntimeMessage = BuildFailureDisplay(code, summary, exception?.Message);
        OnPropertyChanged(nameof(LastRuntimeMessage));
    }

    private bool ShouldReportBackgroundFailure(TimeSpan minInterval)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var previousTicks = Interlocked.Read(ref _lastBackgroundFailureTicks);
        if (previousTicks != 0)
        {
            var elapsedTicks = nowTicks - previousTicks;
            if (elapsedTicks > 0 && elapsedTicks < minInterval.Ticks)
            {
                return false;
            }
        }

        Interlocked.Exchange(ref _lastBackgroundFailureTicks, nowTicks);
        return true;
    }

    private async Task ReportBackgroundFailureAsync(string code, string summary, Exception ex, TimeSpan minInterval)
    {
        if (!ShouldReportBackgroundFailure(minInterval))
        {
            return;
        }

        await RunOnUiThreadAsync(() => SetRuntimeFailure(code, summary, ex));
    }

    private void RecordOperationMetric(string operationName, TimeSpan elapsed)
    {
        _performanceSummary = _performanceMetricsService.Record(operationName, elapsed);
        if (string.Equals(operationName, "ComposeVideo", StringComparison.OrdinalIgnoreCase))
        {
            _composeLastRunText = $"Compose Last Run: {elapsed:mm\\:ss}";
            OnPropertyChanged(nameof(ComposeLastRunText));
        }

        OnPropertyChanged(nameof(PerformanceSummary));
    }


    private Task RunOnUiThreadAsync(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null || app.Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return app.Dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task;
    }

    private void RefreshWorkflowState()
    {
        _workflow.Refresh(
            _snapshot.State,
            _snapshot.FailureReason,
            CurrentSessionClips.Count,
            CanComposeManifest,
            CanStartClip,
            CanPauseClip);
    }

    private void ExpandClipCuration()
    {
        if (!CanOpenCuration)
        {
            return;
        }

        IsClipCurationExpanded = true;
    }

    private void OnOnboardingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseOnboardingState();
    }

    private void OnHistoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SessionHistory));
        OnPropertyChanged(nameof(SelectedSessionRecord));
        OnPropertyChanged(nameof(SessionHistoryStatus));
        OnPropertyChanged(nameof(CanOpenSelectedSessionFolder));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(CanCopyFixHint));
        RaiseCommandState();
    }

    private void OnTargetingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseTargetingAndLaunchState();
    }

    private void OnProductionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseProductionState();
    }

    private void OnCurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ClipCurationViewModel.IsClipCurationExpanded), StringComparison.Ordinal)
            && _curation.IsClipCurationExpanded)
        {
            _ = BackfillClipEditorThumbnailsAsync();
        }

        RaiseCurationState();
    }

    private void OnWorkflowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(WorkflowStepTitle));
        OnPropertyChanged(nameof(WorkflowStepDetail));
        OnPropertyChanged(nameof(PrimaryWorkflowActionText));
        OnPropertyChanged(nameof(CanExecutePrimaryWorkflow));
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step4Background));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step4TextColor));
        RaiseCommandState();
    }

    private void OnSessionStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LastOutputPath));
        OnPropertyChanged(nameof(HasSessionActions));
        OnPropertyChanged(nameof(CanComposeManifest));
        OnPropertyChanged(nameof(CanOpenLatestOutputFolder));
        OnPropertyChanged(nameof(CanCreatePublishPackage));
        OnPropertyChanged(nameof(CanPlayClipPreview));
        OnPropertyChanged(nameof(CanSaveSessionDraft));
        OnPropertyChanged(nameof(CanCloseSession));
        OnPropertyChanged(nameof(CanStartNewSession));
        OnPropertyChanged(nameof(CanOpenComposeHealth));
        RaiseCommandState();
    }

    private void OnLiveClipTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        UpdateLiveClipPreview();
    }

    private void OnDraftAutosaveTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _draftAutosaveInFlight, 1) == 1)
        {
            return;
        }

        _ = RunDraftAutosaveAsync();
    }

    private async Task RunDraftAutosaveAsync()
    {
        try
        {
            await SaveDraftStateAsync();
        }
        catch (Exception ex)
        {
            await ReportBackgroundFailureAsync(
                "DS-DESK-RECOVERY-003",
                "Draft autosave failed.",
                ex,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            Interlocked.Exchange(ref _draftAutosaveInFlight, 0);
        }
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifecycleCancellation.Cancel();

        _draftAutosaveTimer.Stop();
        _telemetryTimer.Stop();
        _liveClipTimer.Stop();

        _draftAutosaveTimer.Tick -= OnDraftAutosaveTimerTick;
        _telemetryTimer.Tick -= OnTelemetryTimerTick;
        _liveClipTimer.Tick -= OnLiveClipTimerTick;

        try
        {
            await StopTargetWatchdogAsync();
        }
        catch
        {
        }

        _captureWatchdogCoordinator.Dispose();
        _onboarding.PropertyChanged -= OnOnboardingPropertyChanged;
        _history.PropertyChanged -= OnHistoryPropertyChanged;
        _targeting.PropertyChanged -= OnTargetingPropertyChanged;
        _production.PropertyChanged -= OnProductionPropertyChanged;
        _curation.PropertyChanged -= OnCurationPropertyChanged;
        _workflow.PropertyChanged -= OnWorkflowPropertyChanged;
        _sessionState.PropertyChanged -= OnSessionStatePropertyChanged;
        _initializeGate.Dispose();
        _lifecycleCancellation.Dispose();
    }
}


