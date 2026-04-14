using System.Windows;
using System.Windows.Media;
using StevensSupportHelper.Admin.Models;

namespace StevensSupportHelper.Admin;

public partial class ClientUpdateWindow : Window
{
    public ClientUpdateWindow(
        string installerPath,
        string initialConfigText,
        string configSourceMessage,
        RepairPrecheckResult precheck)
    {
        InitializeComponent();
        InstallerPathTextBox.Text = installerPath;
        ConfigTextBox.Text = initialConfigText;
        ConfigSourceTextBlock.Text = configSourceMessage;
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
        RunUpdateButton.IsEnabled = precheck.IsReachable && precheck.HasCredentials;
        StatusTextBlock.Text = RunUpdateButton.IsEnabled
            ? "Ready to push installer and config to the client."
            : "Update is blocked until WinRM and credentials are available.";
    }

    private void RunUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ConfigTextBox.Text))
        {
            StatusTextBlock.Text = "The client.installer.config content must not be empty.";
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
