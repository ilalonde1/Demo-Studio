using System.Collections.ObjectModel;
using DemoStudio.Desktop.App.ViewModels;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopClipCurationCoordinator
{
    public bool TryMoveSelectedUp(ObservableCollection<CurrentSessionClipItem> clips, CurrentSessionClipItem? selectedClip)
    {
        if (selectedClip is null || clips.Count < 2)
        {
            return false;
        }

        var index = clips.IndexOf(selectedClip);
        if (index <= 0)
        {
            return false;
        }

        clips.Move(index, index - 1);
        ReindexClipOrders(clips);
        return true;
    }

    public bool TryMoveSelectedDown(ObservableCollection<CurrentSessionClipItem> clips, CurrentSessionClipItem? selectedClip)
    {
        if (selectedClip is null || clips.Count < 2)
        {
            return false;
        }

        var index = clips.IndexOf(selectedClip);
        if (index < 0 || index >= clips.Count - 1)
        {
            return false;
        }

        clips.Move(index, index + 1);
        ReindexClipOrders(clips);
        return true;
    }

    public CurrentSessionClipItem? TryReorder(ObservableCollection<CurrentSessionClipItem> clips, int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= clips.Count || toIndex >= clips.Count || fromIndex == toIndex)
        {
            return null;
        }

        var selected = clips[fromIndex];
        clips.Move(fromIndex, toIndex);
        ReindexClipOrders(clips);
        return selected;
    }

    public void ReindexClipOrders(ObservableCollection<CurrentSessionClipItem> clips)
    {
        for (var i = 0; i < clips.Count; i++)
        {
            clips[i].Order = i + 1;
        }
    }
}
