using System.Diagnostics;
using System.Windows;

namespace StevensSupportHelper.Admin;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).FileVersion ?? "unknown";
        VersionTextBlock.Text = $"Version: {version}";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
