namespace DemoStudio.Infrastructure.Execution.Internal;

using DemoStudio.Infrastructure.Execution.Windows;

internal static class CaptureRegionCalculator
{
    internal static WindowBounds ExpandWithinDesktop(WindowBounds region, WindowBounds desktopBounds, int paddingPixels)
    {
        if (!region.IsValid || !desktopBounds.IsValid)
        {
            return region;
        }

        var padding = Math.Max(0, paddingPixels);

        var left = region.X - padding;
        var top = region.Y - padding;
        var right = region.X + region.Width + padding;
        var bottom = region.Y + region.Height + padding;

        var desktopLeft = desktopBounds.X;
        var desktopTop = desktopBounds.Y;
        var desktopRight = desktopBounds.X + desktopBounds.Width;
        var desktopBottom = desktopBounds.Y + desktopBounds.Height;

        if (left < desktopLeft)
        {
            left = desktopLeft;
        }

        if (top < desktopTop)
        {
            top = desktopTop;
        }

        if (right > desktopRight)
        {
            right = desktopRight;
        }

        if (bottom > desktopBottom)
        {
            bottom = desktopBottom;
        }

        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        return new WindowBounds(left, top, width, height);
    }
}

