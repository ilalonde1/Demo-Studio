using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.Tests;

public sealed class FfmpegOperationQueueTests
{
    [Fact]
    public async Task EnqueueAsync_SerializesOperations()
    {
        var queue = new DesktopFfmpegOperationQueue(maxConcurrentAndQueued: 2);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = queue.EnqueueAsync(
            "first",
            async _ =>
            {
                await gate.Task;
                return DesktopVideoComposeResult.Success("out-1.mp4", "Balanced");
            });

        await Task.Delay(80);

        var second = queue.EnqueueAsync(
            "second",
            async _ =>
            {
                secondEntered = true;
                await Task.Delay(10);
                return DesktopVideoComposeResult.Success("out-2.mp4", "Balanced");
            });

        await Task.Delay(80);
        Assert.False(secondEntered);

        gate.SetResult();
        var firstResult = await first;
        var secondResult = await second;

        Assert.True(firstResult.Accepted);
        Assert.True(secondResult.Accepted);
        Assert.True(secondEntered);
        Assert.True(secondResult.QueueDelay >= TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task EnqueueAsync_RejectsWhenQueueIsFull()
    {
        var queue = new DesktopFfmpegOperationQueue(maxConcurrentAndQueued: 2);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = queue.EnqueueAsync(
            "first",
            async _ =>
            {
                await gate.Task;
                return DesktopPublishPackageResult.Success("pkg-1.zip");
            });

        await Task.Delay(50);

        var second = queue.EnqueueAsync(
            "second",
            async _ =>
            {
                await Task.Delay(10);
                return DesktopPublishPackageResult.Success("pkg-2.zip");
            });

        await Task.Delay(50);

        var third = await queue.EnqueueAsync(
            "third",
            async _ =>
            {
                await Task.Delay(10);
                return DesktopPublishPackageResult.Success("pkg-3.zip");
            });

        gate.SetResult();
        await first;
        await second;

        Assert.False(third.Accepted);
        Assert.Null(third.Value);
        Assert.Contains("queue is full", third.Message, StringComparison.OrdinalIgnoreCase);
    }
}
