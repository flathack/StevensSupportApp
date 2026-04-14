using System.Windows;
using Microsoft.Win32;

namespace StevensSupportHelper.Admin;

public partial class SettingsWindow : Window
{
    public SettingsWindow(string serverUrl, string apiKey, string serverProjectPath, string rustDeskPath, string rustDeskPassword, string clientInstallerPath, string remoteActionsPath, string packageGeneratorPath, string remoteUserName, string remotePassword)
    {
        InitializeComponent();
        ServerUrlTextBox.Text = serverUrl;
        ApiKeyPasswordBox.Password = apiKey;
        ServerProjectPathTextBox.Text = serverProjectPath;
        RustDeskPathTextBox.Text = rustDeskPath;
        RustDeskPasswordBox.Password = rustDeskPassword;
        ClientInstallerPathTextBox.Text = clientInstallerPath;
        RemoteActionsPathTextBox.Text = remoteActionsPath;
        PackageGeneratorPathTextBox.Text = packageGeneratorPath;
        RemoteUserNameTextBox.Text = remoteUserName;
        RemotePasswordBox.Password = remotePassword;
    }

    public string ServerUrl => ServerUrlTextBox.Text.Trim();
    public string ApiKey => ApiKeyPasswordBox.Password.Trim();
    public string ServerProjectPath => ServerProjectPathTextBox.Text.Trim();
    public string RustDeskPath => RustDeskPathTextBox.Text.Trim();
    public string RustDeskPassword => RustDeskPasswordBox.Password.Trim();
    public string ClientInstallerPath => ClientInstallerPathTextBox.Text.Trim();
    public string RemoteActionsPath => RemoteActionsPathTextBox.Text.Trim();
    public string PackageGeneratorPath => PackageGeneratorPathTextBox.Text.Trim();
    public string RemoteUserName => RemoteUserNameTextBox.Text.Trim();
    public string RemotePassword => RemotePasswordBox.Password.Trim();

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = ".NET project (*.csproj)|*.csproj|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = ServerProjectPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            ServerProjectPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseRustDeskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "RustDesk executable (rustdesk.exe)|rustdesk.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = RustDeskPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            RustDeskPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseClientInstallerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Client installer executable (*.exe)|*.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = ClientInstallerPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            ClientInstallerPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseRemoteActionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        BrowseFolderIntoTextBox(RemoteActionsPathTextBox, RemoteActionsPath, "Select remote actions folder");
    }

    private void BrowsePackageGeneratorButton_OnClick(object sender, RoutedEventArgs e)
    {
        BrowseFolderIntoTextBox(PackageGeneratorPathTextBox, PackageGeneratorPath, "Select package generator folder");
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowseFolderIntoTextBox(System.Windows.Controls.TextBox targetTextBox, string currentPath, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "Select folder"
        };

        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            targetTextBox.Text = System.IO.Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
        }
    }
}
