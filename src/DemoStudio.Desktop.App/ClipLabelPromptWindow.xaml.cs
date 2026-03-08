using System.Windows;

namespace DemoStudio.Desktop.App;

public partial class ClipLabelPromptWindow : Window
{
    public ClipLabelPromptWindow(string initialValue)
    {
        InitializeComponent();
        LabelTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            LabelTextBox.Focus();
            LabelTextBox.SelectAll();
        };
    }

    public string LabelValue => LabelTextBox.Text?.Trim() ?? string.Empty;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
