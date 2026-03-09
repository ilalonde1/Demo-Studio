using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace DemoStudio.Desktop.App;

public partial class StageWorkspaceWindow : Window
{
    private const string DefaultAddress = "https://www.bing.com";
    private bool _initialized;

    public StageWorkspaceWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public string CurrentAddress
    {
        get => AddressTextBox.Text;
        set => AddressTextBox.Text = string.IsNullOrWhiteSpace(value) ? DefaultAddress : value.Trim();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        NavigateToAddress(CurrentAddress);
    }

    private void GoButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToAddress(AddressTextBox.Text);
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (StageBrowser.CanGoBack)
        {
            StageBrowser.GoBack();
        }
    }

    private void ForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (StageBrowser.CanGoForward)
        {
            StageBrowser.GoForward();
        }
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        StageBrowser.Refresh();
    }

    private void OpenExternalButton_OnClick(object sender, RoutedEventArgs e)
    {
        var address = NormalizeAddress(AddressTextBox.Text);
        if (address is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(address)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Keep stage workspace stable even if shell launch fails.
        }
    }

    private void AddressTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        NavigateToAddress(AddressTextBox.Text);
    }

    private void NavigateToAddress(string? rawAddress)
    {
        var address = NormalizeAddress(rawAddress);
        if (address is null)
        {
            return;
        }

        AddressTextBox.Text = address;
        try
        {
            StageBrowser.Navigate(address);
        }
        catch
        {
            // No-op to avoid breaking stage session due to navigation error.
        }
    }

    private static string? NormalizeAddress(string? rawAddress)
    {
        var candidate = string.IsNullOrWhiteSpace(rawAddress) ? DefaultAddress : rawAddress.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : null;
    }
}
