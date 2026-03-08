using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DemoStudio.Desktop.App.ViewModels;
namespace DemoStudio.Desktop.App;
public partial class MainWindow : Window
{
    private Point _timelineDragStartPoint;
    private CurrentSessionClipItem? _draggedClipItem;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ClipTimelineList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _timelineDragStartPoint = e.GetPosition(null);
        _draggedClipItem = FindAncestorDataContext<CurrentSessionClipItem>(e.OriginalSource as DependencyObject);
        if (sender is ListBox listBox && _draggedClipItem is not null)
        {
            listBox.SelectedItem = _draggedClipItem;
        }
    }

    private void ClipTimelineList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedClipItem is null)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _timelineDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _timelineDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CurrentSessionClipItem), _draggedClipItem), DragDropEffects.Move);
    }

    private void ClipTimelineList_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(CurrentSessionClipItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ClipTimelineList_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not ListBox listBox ||
            !e.Data.GetDataPresent(typeof(CurrentSessionClipItem)))
        {
            return;
        }

        var sourceItem = e.Data.GetData(typeof(CurrentSessionClipItem)) as CurrentSessionClipItem;
        if (sourceItem is null)
        {
            return;
        }

        var fromIndex = vm.CurrentSessionClips.IndexOf(sourceItem);
        if (fromIndex < 0)
        {
            return;
        }

        var toIndex = ResolveListDropIndex(listBox, e.GetPosition(listBox), vm, fromIndex);
        vm.ReorderClip(fromIndex, toIndex);
        _draggedClipItem = null;
    }

    private void ClipTimelineList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedClipItem = null;
    }

    private void ClipGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _timelineDragStartPoint = e.GetPosition(null);
        _draggedClipItem = FindAncestorDataContext<CurrentSessionClipItem>(e.OriginalSource as DependencyObject);
        if (sender is DataGrid dataGrid && _draggedClipItem is not null)
        {
            dataGrid.SelectedItem = _draggedClipItem;
        }
    }

    private void ClipGrid_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedClipItem is null)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _timelineDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _timelineDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CurrentSessionClipItem), _draggedClipItem), DragDropEffects.Move);
    }

    private void ClipGrid_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(CurrentSessionClipItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ClipGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not DataGrid dataGrid ||
            !e.Data.GetDataPresent(typeof(CurrentSessionClipItem)))
        {
            return;
        }

        var sourceItem = e.Data.GetData(typeof(CurrentSessionClipItem)) as CurrentSessionClipItem;
        if (sourceItem is null)
        {
            return;
        }

        var fromIndex = vm.CurrentSessionClips.IndexOf(sourceItem);
        if (fromIndex < 0)
        {
            return;
        }

        var toIndex = ResolveGridDropIndex(dataGrid, e.GetPosition(dataGrid), vm, fromIndex);
        vm.ReorderClip(fromIndex, toIndex);
        _draggedClipItem = null;
    }

    private void ClipGrid_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedClipItem = null;
    }

    private void TimelineCard_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var clip = (sender as FrameworkElement)?.DataContext as CurrentSessionClipItem
            ?? FindAncestorDataContext<CurrentSessionClipItem>(e.OriginalSource as DependencyObject);

        if (clip is null || !vm.PlayClipPreviewCommand.CanExecute(clip))
        {
            return;
        }

        vm.PlayClipPreviewCommand.Execute(clip);
        e.Handled = true;
    }

    private static T? FindAncestorDataContext<T>(DependencyObject? source) where T : class
    {
        while (source is not null)
        {
            if (source is FrameworkElement fe && fe.DataContext is T dataContext)
            {
                return dataContext;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static int ResolveListDropIndex(ListBox listBox, Point dropPoint, MainWindowViewModel vm, int fromIndex)
    {
        if (vm.CurrentSessionClips.Count == 0)
        {
            return fromIndex;
        }

        for (var i = 0; i < vm.CurrentSessionClips.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
            {
                continue;
            }

            var bounds = VisualTreeHelper.GetDescendantBounds(container);
            var origin = container.TranslatePoint(new Point(0, 0), listBox);
            var rect = new Rect(origin, bounds.Size);
            if (!rect.Contains(dropPoint))
            {
                continue;
            }

            if (dropPoint.X > rect.Left + (rect.Width / 2d))
            {
                return Math.Min(vm.CurrentSessionClips.Count - 1, i + 1);
            }

            return i;
        }

        return vm.CurrentSessionClips.Count - 1;
    }

    private static int ResolveGridDropIndex(DataGrid dataGrid, Point dropPoint, MainWindowViewModel vm, int fromIndex)
    {
        if (vm.CurrentSessionClips.Count == 0)
        {
            return fromIndex;
        }

        for (var i = 0; i < vm.CurrentSessionClips.Count; i++)
        {
            if (dataGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
            {
                continue;
            }

            var bounds = VisualTreeHelper.GetDescendantBounds(row);
            var origin = row.TranslatePoint(new Point(0, 0), dataGrid);
            var rect = new Rect(origin, bounds.Size);
            if (!rect.Contains(dropPoint))
            {
                continue;
            }

            if (dropPoint.Y > rect.Top + (rect.Height / 2d))
            {
                return Math.Min(vm.CurrentSessionClips.Count - 1, i + 1);
            }

            return i;
        }

        return vm.CurrentSessionClips.Count - 1;
    }
}
