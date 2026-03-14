using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Options.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public static class DesktopRecorderOptionsLoader
{
    public static DesktopRecorderOptions Load(string baseDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetFullPath(baseDirectory))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<IValidateOptions<DesktopRecorderOptions>, DesktopRecorderOptionsValidator>();
        services.AddSingleton<IValidateOptions<FfmpegCaptureOptions>, FfmpegCaptureOptionsValidator>();
        services.AddOptions<DesktopRecorderOptions>()
            .Bind(configuration.GetSection("DesktopRecorder"))
            .PostConfigure(DesktopRecorderOptionsNormalizer.Normalize);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DesktopRecorderOptions>>().Value;
    }
}
