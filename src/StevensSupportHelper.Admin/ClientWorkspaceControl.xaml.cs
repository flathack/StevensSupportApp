using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;

namespace StevensSupportHelper.Admin;

public partial class ClientWorkspaceControl : UserControl
{
    public ClientWorkspaceControl(ClientRow client)
    {
        InitializeComponent();
        ClientId = client.ClientId;
        UpdateClient(client);
    }

    public Guid ClientId { get; }

    public event EventHandler<string>? ActionRequested;

    public TabControl WorkspaceTabs => WorkspaceTabControl;

    public void UpdateClient(ClientRow client)
    {
        ClientNameTextBlock.Text = client.DeviceName;
        ClientSummaryTextBlock.Text = $"{client.MachineName} | {client.LogonStateSummary} | Agent {client.AgentVersion} | {client.PresenceSummary}";
    }

    public void SetActionEnabled(string action, bool isEnabled)
    {
        var target = action switch
        {
            "request-support" => RequestSupportButton,
            "connect" => ConnectButton,
            "rdp" => RdpConnectButton,
            "rustdesk" => RustDeskButton,
            "ps-console" => PowerShellConsoleButton,
            "dashboard" => DashboardButton,
            "files" => FilesButton,
            "tasks" => TasksButton,
            "services" => ServicesButton,
            "software" => SoftwareButton,
            "registry" => RegistryButton,
            "power" => PowerOptionsButton,
            "windows-updates" => WindowsUpdatesButton,
            "chat" => ChatButton,
            "remote-action" => RemoteActionButton,
            "screenshot" => ScreenshotButton,
            "reboot" => RebootButton,
            "shutdown" => ShutdownButton,
            "end-session" => EndSessionButton,
            "edit-client" => EditClientButton,
            _ => null
        };

        if (target is not null)
        {
            target.IsEnabled = isEnabled;
        }
    }

    private void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action })
        {
            ActionRequested?.Invoke(this, action);
        }
    }
}
