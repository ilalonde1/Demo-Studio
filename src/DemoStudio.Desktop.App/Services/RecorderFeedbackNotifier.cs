namespace DemoStudio.Desktop.App.Services;

public interface IRecorderFeedbackNotifier
{
    event EventHandler<RecorderFeedbackEventArgs>? StepCaptured;

    void NotifyStepCaptured(string source);
}

public sealed class RecorderFeedbackNotifier : IRecorderFeedbackNotifier
{
    public event EventHandler<RecorderFeedbackEventArgs>? StepCaptured;

    public void NotifyStepCaptured(string source)
    {
        var effectiveSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
        StepCaptured?.Invoke(this, new RecorderFeedbackEventArgs(effectiveSource));
    }
}

public sealed class RecorderFeedbackEventArgs : EventArgs
{
    public RecorderFeedbackEventArgs(string source)
    {
        Source = source;
    }

    public string Source { get; }
}
