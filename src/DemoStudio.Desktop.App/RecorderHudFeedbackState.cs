namespace DemoStudio.Desktop.App;

internal sealed class RecorderHudFeedbackState
{
    public int CurrentStepNumber { get; private set; }

    public void ResetSession()
    {
        CurrentStepNumber = 0;
    }

    public string RegisterStepCapture()
    {
        CurrentStepNumber++;
        return $"Step {CurrentStepNumber} captured";
    }
}
