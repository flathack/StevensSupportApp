using System.IO;
using System.Windows;
using Microsoft.Win32;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class GenerateInstallPackageWindow : Window
{
    private readonly string _serverUrl;
    private readonly InstallPackageGeneratorService _generatorService = new();

    public GenerateInstallPackageWindow(string serverUrl, string clientInstallerPath, string packageGeneratorPath)
    {
        _serverUrl = serverUrl;
        InitializeComponent();

        ClientInstallerPathTextBox.Text = clientInstallerPath;
        PackageGeneratorPathTextBox.Text = packageGeneratorPath;
        OutputZipPathTextBox.Text = BuildDefaultOutputPath(clientInstallerPath);
        InstallerConfigTextBox.Text = _generatorService.BuildDefaultInstallerConfigText(serverUrl);
    }

    public string OutputZipPath { get; private set; } = string.Empty;

    private void BrowseClientInstallerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Client installer executable (*.exe)|*.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = ClientInstallerPathTextBox.Text.Trim()
        };

        if (dialog.ShowDialog(this) == true)
        {
            ClientInstallerPathTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(OutputZipPathTextBox.Text))
            {
                OutputZipPathTextBox.Text = BuildDefaultOutputPath(dialog.FileName);
            }
        }
    }

    private void BrowsePackageGeneratorButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select package generator folder",
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "Select folder"
        };

        var currentPath = PackageGeneratorPathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            PackageGeneratorPathTextBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
        }
    }

    private void BrowseOutputZipButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP archive (*.zip)|*.zip",
            AddExtension = true,
            DefaultExt = ".zip",
            FileName = string.IsNullOrWhiteSpace(OutputZipPathTextBox.Text)
                ? "StevensSupportHelper-InstallPackage.zip"
                : Path.GetFileName(OutputZipPathTextBox.Text),
            InitialDirectory = ResolveInitialDirectory(OutputZipPathTextBox.Text)
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputZipPathTextBox.Text = dialog.FileName;
        }
    }

    private void ReloadTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        InstallerConfigTextBox.Text = _generatorService.BuildDefaultInstallerConfigText(_serverUrl);
        StatusTextBlock.Text = "Template reloaded.";
    }

    private void GenerateButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ToggleBusy(true, "Generating install package...");
            OutputZipPath = _generatorService.BuildPackage(
                ClientInstallerPathTextBox.Text.Trim(),
                PackageGeneratorPathTextBox.Text.Trim(),
                InstallerConfigTextBox.Text,
                OutputZipPathTextBox.Text.Trim());
            ToggleBusy(false, $"ZIP created at {OutputZipPath}");
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Generation failed: {exception.Message}");
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        ClientInstallerPathTextBox.IsEnabled = !isBusy;
        PackageGeneratorPathTextBox.IsEnabled = !isBusy;
        OutputZipPathTextBox.IsEnabled = !isBusy;
        InstallerConfigTextBox.IsEnabled = !isBusy;
        GenerateButton.IsEnabled = !isBusy;
        StatusTextBlock.Text = status;
    }

    private static string BuildDefaultOutputPath(string clientInstallerPath)
    {
        var baseDirectory = !string.IsNullOrWhiteSpace(clientInstallerPath) && File.Exists(clientInstallerPath)
            ? Path.GetDirectoryName(clientInstallerPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(baseDirectory, "StevensSupportHelper-InstallPackage.zip");
    }

    private static string ResolveInitialDirectory(string outputZipPath)
    {
        if (string.IsNullOrWhiteSpace(outputZipPath))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        var directory = Path.GetDirectoryName(outputZipPath);
        return string.IsNullOrWhiteSpace(directory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : directory;
    }
}
