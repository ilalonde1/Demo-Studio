using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed record MainWindowInitializationResult(bool Succeeded, IReadOnlyList<string> Failures)
{
    public static MainWindowInitializationResult Success() => new(true, Array.Empty<string>());
}

internal sealed record TelemetrySnapshot(
    double WorkingSetMb,
    double PrivateMb,
    string MemoryWorkingSetText,
    string MemoryPrivateText,
    string CaptureWriteRateText,
    string CaptureFileSizeText,
    long TelemetryBytes,
    DateTimeOffset TelemetrySampleUtc);

public sealed class CurrentSessionClipItem : INotifyPropertyChanged
{
    private int _order;
    private string _label = string.Empty;
    private string _durationDisplay = string.Empty;
    private string _bannerText = string.Empty;
    private double _startSeconds;
    private double _durationSeconds;
    private double _timelineWidth;
    private bool _includeNarration = true;
    private string? _thumbnailPath;
    private ImageSource? _thumbnailImage;
    private string? _narrationAudioPath;
    private string _narrationSource = string.Empty;
    private string _narrationScript = string.Empty;

    public CurrentSessionClipItem(
        int sequence,
        string label,
        string durationDisplay,
        string bannerText,
        double startSeconds,
        double durationSeconds,
        string? narrationAudioPath = null)
    {
        Sequence = sequence;
        Label = label;
        DurationDisplay = durationDisplay;
        BannerText = bannerText;
        StartSeconds = startSeconds;
        DurationSeconds = durationSeconds;
        NarrationAudioPath = narrationAudioPath;
        TimelineWidth = ComputeTimelineWidth(durationSeconds);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Sequence { get; }

    public int Order
    {
        get => _order;
        set
        {
            if (_order == value)
            {
                return;
            }

            _order = value;
            OnPropertyChanged();
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (value == _label)
            {
                return;
            }

            _label = value;
            OnPropertyChanged();
        }
    }

    public string DurationDisplay
    {
        get => _durationDisplay;
        set
        {
            if (value == _durationDisplay)
            {
                return;
            }

            _durationDisplay = value;
            OnPropertyChanged();
        }
    }

    public string BannerText
    {
        get => _bannerText;
        set
        {
            if (value == _bannerText)
            {
                return;
            }

            _bannerText = value;
            OnPropertyChanged();
        }
    }

    public double StartSeconds
    {
        get => _startSeconds;
        set
        {
            if (Math.Abs(_startSeconds - value) < 0.001d)
            {
                return;
            }

            _startSeconds = value;
            OnPropertyChanged();
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            if (Math.Abs(_durationSeconds - value) < 0.001d)
            {
                return;
            }

            _durationSeconds = value;
            OnPropertyChanged();
        }
    }

    public double TimelineWidth
    {
        get => _timelineWidth;
        set
        {
            if (Math.Abs(_timelineWidth - value) < 0.1d)
            {
                return;
            }

            _timelineWidth = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeNarration
    {
        get => _includeNarration;
        set
        {
            if (_includeNarration == value)
            {
                return;
            }

            _includeNarration = value;
            OnPropertyChanged();
        }
    }

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (string.Equals(_thumbnailPath, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _thumbnailPath = value;
            _thumbnailImage = LoadThumbnailImage(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
            OnPropertyChanged(nameof(ThumbnailImage));
        }
    }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(_thumbnailPath) && File.Exists(_thumbnailPath);

    public ImageSource? ThumbnailImage => _thumbnailImage;

    public string? NarrationAudioPath
    {
        get => _narrationAudioPath;
        set
        {
            if (string.Equals(_narrationAudioPath, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _narrationAudioPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNarration));
            OnPropertyChanged(nameof(NarrationStatus));
        }
    }

    public bool HasNarration => !string.IsNullOrWhiteSpace(_narrationAudioPath) && File.Exists(_narrationAudioPath);

    public string NarrationSource
    {
        get => _narrationSource;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.Equals(_narrationSource, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _narrationSource = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NarrationStatus));
        }
    }

    public string NarrationScript
    {
        get => _narrationScript;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_narrationScript, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _narrationScript = normalized;
            OnPropertyChanged();
        }
    }

    public string NarrationStatus
    {
        get
        {
            if (!HasNarration)
            {
                return "None";
            }

            return string.IsNullOrWhiteSpace(NarrationSource) ? "Recorded" : NarrationSource;
        }
    }

    private static double ComputeTimelineWidth(double seconds)
    {
        var width = 120d + (Math.Max(0d, seconds) * 8d);
        if (width < 120d)
        {
            return 120d;
        }

        if (width > 420d)
        {
            return 420d;
        }

        return width;
    }

    private static ImageSource? LoadThumbnailImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
