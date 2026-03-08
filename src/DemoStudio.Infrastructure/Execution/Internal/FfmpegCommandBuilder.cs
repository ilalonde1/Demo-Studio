namespace DemoStudio.Infrastructure.Execution.Internal;

using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using System.Runtime.InteropServices;

internal static class FfmpegCommandBuilder
{
    private const string EvenDimensionsFilter = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    internal static string BuildFullDesktopCaptureArguments(FfmpegCaptureOptions options, string outputPath)
    {
        var captureBounds = TryGetPrimaryMonitorBounds();
        var filterChain = options.HighlightCursor
            ? "drawbox=x=mouse_x-10:y=mouse_y-10:w=20:h=20:color=yellow@0.6:t=fill"
            : null;
        return BuildDesktopBase(options, outputPath, filterChain, captureBounds);
    }

    internal static string BuildWindowCaptureArguments(FfmpegCaptureOptions options, IntPtr hwnd, string outputPath)
    {
        var bounds = Win32WindowEnumerator.GetBounds(hwnd);
        if (!bounds.IsValid)
        {
            throw new InvalidOperationException("Window bounds are invalid.");
        }

        return BuildWindowCaptureArguments(options, bounds, outputPath);
    }

    internal static string BuildWindowCaptureArguments(FfmpegCaptureOptions options, WindowBounds bounds, string outputPath)
    {
        if (!bounds.IsValid)
        {
            throw new InvalidOperationException("Window bounds are invalid.");
        }

        var filterChain = BuildFilterChain(options, bounds);
        return BuildDesktopBase(options, outputPath, filterChain, null);
    }

    private static string BuildDesktopBase(FfmpegCaptureOptions options, string outputPath, string? filterChain, WindowBounds? captureBounds)
    {
        var arguments = new List<string>
        {
            "-y",
            "-f gdigrab",
            "-draw_mouse 1",
            $"-framerate {options.FrameRate}"
        };

        if (captureBounds is { IsValid: true } bounds)
        {
            arguments.Add($"-offset_x {bounds.X}");
            arguments.Add($"-offset_y {bounds.Y}");
            arguments.Add($"-video_size {bounds.Width}x{bounds.Height}");
        }

        arguments.Add("-i desktop");
        if (options.CaptureMicrophone)
        {
            arguments.Add("-f dshow");
            arguments.Add($"-i audio={QuoteForDshow(string.IsNullOrWhiteSpace(options.MicrophoneDeviceName) ? "default" : options.MicrophoneDeviceName!.Trim())}");
        }

        var safeFilterChain = BuildSafeFilterChain(filterChain);
        arguments.Add($"-vf {Quote(safeFilterChain)}");

        arguments.Add($"-t {options.MaxDurationSeconds}");
        if (options.CaptureMicrophone)
        {
            arguments.Add("-c:a aac");
            arguments.Add("-b:a 160k");
            arguments.Add("-ac 1");
            arguments.Add("-ar 44100");
            arguments.Add("-map 0:v:0");
            arguments.Add("-map 1:a:0");
        }
        else
        {
            arguments.Add("-an");
        }
        arguments.Add($"-c:v {options.VideoCodec}");
        arguments.Add($"-preset {options.Preset}");
        arguments.Add($"-crf {options.Crf}");
        arguments.Add("-pix_fmt yuv420p");
        arguments.Add(Quote(outputPath));

        return string.Join(" ", arguments);
    }

    private static string BuildFilterChain(FfmpegCaptureOptions options, WindowBounds captureBounds)
    {
        var filters = new List<string>
        {
            $"crop={captureBounds.Width}:{captureBounds.Height}:{captureBounds.X}:{captureBounds.Y}"
        };

        if (options.HighlightCursor)
        {
            filters.Add("drawbox=x=mouse_x-10:y=mouse_y-10:w=20:h=20:color=yellow@0.6:t=fill");
        }

        return string.Join(",", filters);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string QuoteForDshow(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"default\"";
        }

        return $"\"{value.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
    }

    private static string BuildSafeFilterChain(string? filterChain)
    {
        if (string.IsNullOrWhiteSpace(filterChain))
        {
            return EvenDimensionsFilter;
        }

        if (filterChain.Contains(EvenDimensionsFilter, StringComparison.Ordinal))
        {
            return filterChain;
        }

        return $"{filterChain},{EvenDimensionsFilter}";
    }

    private static WindowBounds? TryGetPrimaryMonitorBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var width = GetSystemMetrics(SmCxScreen);
            var height = GetSystemMetrics(SmCyScreen);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // Primary monitor in Windows virtual coordinates anchors at 0,0.
            return new WindowBounds(0, 0, width, height);
        }
        catch
        {
            return null;
        }
    }

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
