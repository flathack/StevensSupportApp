using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class SoftwareCenterTab : UserControl
{
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly ObservableCollection<RemoteSoftwarePackage> _packages = [];
    private List<RemoteSoftwarePackage> _allPackages = [];
    private bool _isBusy;

    public SoftwareCenterTab(ClientRow client, PowerShellRemoteAdminService remoteService)
    {
        _client = client;
        _remoteService = remoteService;
        InitializeComponent();
        SoftwareDataGrid.ItemsSource = _packages;
        CatalogComboBox.ItemsSource = BuildCatalog();
        CatalogComboBox.SelectedIndex = 0;
        Loaded += async (_, _) => await RefreshSoftwareAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshSoftwareAsync();

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void SoftwareDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionActions();

    private async void InstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CatalogComboBox.SelectedItem is not RemoteWingetPackageOption package)
        {
            StatusTextBlock.Text = "Select a winget package first.";
            return;
        }

        try
        {
            ToggleBusy(true, $"Installing {package.DisplayName}...");
            await _remoteService.InstallWingetPackageAsync(_client, package.PackageId, CancellationToken.None);
            await RefreshSoftwareAsync();
            StatusTextBlock.Text = $"Installed {package.DisplayName} on {_client.DeviceName}.";
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Install failed: {exception.Message}");
        }
    }

    private async void UninstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SoftwareDataGrid.SelectedItem is not RemoteSoftwarePackage package)
        {
            StatusTextBlock.Text = "Select installed software first.";
            return;
        }

        var result = MessageBox.Show(Window.GetWindow(this), $"Silent uninstall '{package.DisplayName}' from {_client.DeviceName}?", "Uninstall Software", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Uninstalling {package.DisplayName}...");
            await _remoteService.UninstallSoftwareAsync(_client, package, CancellationToken.None);
            await RefreshSoftwareAsync();
            StatusTextBlock.Text = $"Uninstall command queued for {package.DisplayName}.";
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Uninstall failed: {exception.Message}");
        }
    }

    private async Task RefreshSoftwareAsync()
    {
        try
        {
            ToggleBusy(true, "Loading installed software...");
            _allPackages = (await _remoteService.ListInstalledSoftwareAsync(_client, CancellationToken.None))
                .OrderBy(static package => package.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyFilter();
            ToggleBusy(false, $"Loaded {_allPackages.Count} installed packages from {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Software refresh failed: {exception.Message}");
        }
    }

    private void ApplyFilter()
    {
        var filter = SearchTextBox.Text.Trim();
        IEnumerable<RemoteSoftwarePackage> filtered = _allPackages;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(package =>
                package.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                package.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                package.Version.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        _packages.Clear();
        foreach (var package in filtered)
        {
            _packages.Add(package);
        }

        UpdateSelectionActions();
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        CatalogComboBox.IsEnabled = !isBusy;
        SearchTextBox.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        InstallButton.IsEnabled = !isBusy;
        SoftwareDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        UninstallButton.IsEnabled = !_isBusy && SoftwareDataGrid.SelectedItem is RemoteSoftwarePackage;
    }

    private static IReadOnlyList<RemoteWingetPackageOption> BuildCatalog()
    {
        return
        [
            new RemoteWingetPackageOption { DisplayName = "Google Chrome", PackageId = "Google.Chrome", Description = "Browser" },
            new RemoteWingetPackageOption { DisplayName = "7-Zip", PackageId = "7zip.7zip", Description = "Archiver" },
            new RemoteWingetPackageOption { DisplayName = "Notepad++", PackageId = "Notepad++.Notepad++", Description = "Editor" },
            new RemoteWingetPackageOption { DisplayName = "VS Code", PackageId = "Microsoft.VisualStudioCode", Description = "Editor" },
            new RemoteWingetPackageOption { DisplayName = "RustDesk", PackageId = "RustDesk.RustDesk", Description = "Remote tool" },
            new RemoteWingetPackageOption { DisplayName = "Tailscale", PackageId = "Tailscale.Tailscale", Description = "VPN" },
            new RemoteWingetPackageOption { DisplayName = "PowerShell 7", PackageId = "Microsoft.PowerShell", Description = "Shell" }
        ];
    }
}
