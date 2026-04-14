using System.Windows;
using System.Windows.Media;
using StevensSupportHelper.Admin.Models;

namespace StevensSupportHelper.Admin;

public partial class RepairClientWindow : Window
{
    public RepairClientWindow(string installerPath, string initialConfigText, RepairPrecheckResult precheck)
    {
        InitializeComponent();
        InstallerPathTextBox.Text = installerPath;
        ConfigTextBox.Text = initialConfigText;
        ApplyPrecheck(precheck);
    }

    public string ConfigText => ConfigTextBox.Text;

    private void ApplyPrecheck(RepairPrecheckResult precheck)
    {
        TargetHostTextBlock.Text = $"Target: {precheck.TargetHost}";
        CredentialTextBlock.Text = precheck.HasCredentials
            ? $"Credentials: {precheck.CredentialUserName}"
            : "Credentials: missing";
        ReachabilityTextBlock.Text = precheck.IsReachable ? "WinRM: reachable" : "WinRM: not reachable";
        ReachabilityTextBlock.Foreground = precheck.IsReachable
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
            : new SolidColorBrush(Color.FromRgb(185, 28, 28));
        PrecheckMessageTextBlock.Text = precheck.Message;
        StartRepairButton.IsEnabled = precheck.IsReachable && precheck.HasCredentials;
        StatusTextBlock.Text = StartRepairButton.IsEnabled
            ? "Ready to copy installer and config to the client."
            : "Repair is blocked until WinRM and credentials are available.";
    }

    private void StartRepairButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ConfigTextBox.Text))
        {
            StatusTextBlock.Text = "Paste the client.installer.config content first.";
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
