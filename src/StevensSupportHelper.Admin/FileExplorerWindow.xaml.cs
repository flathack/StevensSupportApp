using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class FileExplorerWindow : Window
{
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly ObservableCollection<RemoteFileSystemEntry> _entries = [];
    private bool _isBusy;

    public FileExplorerWindow(ClientRow client, PowerShellRemoteAdminService remoteService)
    {
        _client = client;
        _remoteService = remoteService;

        InitializeComponent();
        Title = $"Remote File Explorer - {_client.DeviceName}";
        EntriesDataGrid.ItemsSource = _entries;
        Loaded += async (_, _) => await LoadPathAsync(string.Empty);
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadPathAsync(PathTextBox.Text.Trim());
    }

    private async void UpButton_OnClick(object sender, RoutedEventArgs e)
    {
        string currentPath = PathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            await LoadPathAsync(string.Empty);
            return;
        }

        string? parentPath = Directory.GetParent(currentPath)?.FullName;
        await LoadPathAsync(parentPath ?? string.Empty);
    }

    private async void EntriesDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesDataGrid.SelectedItem is not RemoteFileSystemEntry entry || !entry.IsDirectory)
        {
            return;
        }

        await LoadPathAsync(entry.FullPath);
    }

    private void EntriesDataGrid_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();
    }

    private async void NewFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        string currentPath = PathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            StatusTextBlock.Text = "Open a target directory first.";
            return;
        }

        string? folderName = PromptDialog.Show(this, "New Folder", "Folder name:", "NewFolder");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Creating remote folder...");
            await _remoteService.CreateDirectoryAsync(_client, Path.Combine(currentPath, folderName.Trim()), CancellationToken.None);
            await LoadPathAsync(currentPath);
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Create folder failed: {exception.Message}");
        }
    }

    private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
    {
        string currentPath = PathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            StatusTextBlock.Text = "Open a target directory first.";
            return;
        }

        var openFileDialog = new OpenFileDialog();
        if (openFileDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Uploading file...");
            string remotePath = Path.Combine(currentPath, Path.GetFileName(openFileDialog.FileName));
            await _remoteService.UploadFileAsync(_client, openFileDialog.FileName, remotePath, CancellationToken.None);
            await LoadPathAsync(currentPath);
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Upload failed: {exception.Message}");
        }
    }

    private async void DownloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (EntriesDataGrid.SelectedItem is not RemoteFileSystemEntry entry || entry.IsDirectory)
        {
            StatusTextBlock.Text = "Select a file first.";
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            FileName = entry.Name
        };
        if (saveFileDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Downloading file...");
            await _remoteService.DownloadFileAsync(_client, entry.FullPath, saveFileDialog.FileName, CancellationToken.None);
            ToggleBusy(false, $"Downloaded {entry.Name} to {saveFileDialog.FileName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Download failed: {exception.Message}");
        }
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (EntriesDataGrid.SelectedItem is not RemoteFileSystemEntry entry)
        {
            StatusTextBlock.Text = "Select an entry first.";
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete '{entry.FullPath}' on {_client.DeviceName}?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Deleting remote path...");
            await _remoteService.DeletePathAsync(_client, entry.FullPath, CancellationToken.None);
            await LoadPathAsync(PathTextBox.Text.Trim());
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Delete failed: {exception.Message}");
        }
    }

    private async Task LoadPathAsync(string path)
    {
        try
        {
            ToggleBusy(true, string.IsNullOrWhiteSpace(path) ? "Loading drives..." : $"Loading {path}...");
            IReadOnlyList<RemoteFileSystemEntry> entries = await _remoteService.ListDirectoryAsync(_client, path, CancellationToken.None);
            _entries.Clear();
            foreach (var entry in entries)
            {
                _entries.Add(entry);
            }

            PathTextBox.Text = path;
            ToggleBusy(false, string.IsNullOrWhiteSpace(path)
                ? $"Loaded {_entries.Count} remote drives."
                : $"Loaded {_entries.Count} entries from {path}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Refresh failed: {exception.Message}");
        }
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        PathTextBox.IsEnabled = !isBusy;
        UpButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        NewFolderButton.IsEnabled = !isBusy;
        UploadButton.IsEnabled = !isBusy;
        EntriesDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        DownloadButton.IsEnabled = !_isBusy && EntriesDataGrid.SelectedItem is RemoteFileSystemEntry selected && !selected.IsDirectory;
        DeleteButton.IsEnabled = !_isBusy && EntriesDataGrid.SelectedItem is RemoteFileSystemEntry;
    }
}
