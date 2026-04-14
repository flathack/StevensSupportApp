using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class FileExplorerTab : UserControl
{
    private sealed class ExplorerViewState
    {
        public string LocalPath { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
    }

    private static readonly Dictionary<Guid, ExplorerViewState> ViewStates = [];
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly ObservableCollection<LocalFileSystemEntry> _localEntries = [];
    private readonly ObservableCollection<RemoteFileSystemEntry> _remoteEntries = [];
    private bool _isBusy;
    private bool _isInitialized;

    public FileExplorerTab(ClientRow client, PowerShellRemoteAdminService remoteService)
    {
        _client = client;
        _remoteService = remoteService;

        InitializeComponent();
        LocalEntriesDataGrid.ItemsSource = _localEntries;
        RemoteEntriesDataGrid.ItemsSource = _remoteEntries;
        Loaded += async (_, _) =>
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            var state = GetOrCreateViewState();
            var localPath = string.IsNullOrWhiteSpace(state.LocalPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : state.LocalPath;
            var remotePath = state.RemotePath;
            await LoadLocalPathAsync(localPath);
            await LoadRemotePathAsync(remotePath);
        };
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadLocalPathAsync(LocalPathTextBox.Text.Trim());
        await LoadRemotePathAsync(RemotePathTextBox.Text.Trim());
    }

    private async void LocalUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentPath = LocalPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            await LoadLocalPathAsync(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            return;
        }

        var parent = Directory.GetParent(currentPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            await LoadLocalPathAsync(parent);
        }
    }

    private async void RemoteUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentPath = RemotePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            await LoadRemotePathAsync(string.Empty);
            return;
        }

        var parent = Directory.GetParent(currentPath)?.FullName;
        await LoadRemotePathAsync(parent ?? string.Empty);
    }

    private async void RemoteRootButton_OnClick(object sender, RoutedEventArgs e) => await LoadRemotePathAsync(string.Empty);

    private async void LocalEntriesDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LocalEntriesDataGrid.SelectedItem is LocalFileSystemEntry entry && entry.IsDirectory)
        {
            await LoadLocalPathAsync(entry.FullPath);
        }
    }

    private async void RemoteEntriesDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RemoteEntriesDataGrid.SelectedItem is RemoteFileSystemEntry entry && entry.IsDirectory)
        {
            await LoadRemotePathAsync(entry.FullPath);
        }
    }

    private void EntriesDataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        var dependencyObject = e.OriginalSource as DependencyObject;
        while (dependencyObject is not null && dependencyObject is not DataGridRow)
        {
            dependencyObject = System.Windows.Media.VisualTreeHelper.GetParent(dependencyObject);
        }

        if (dependencyObject is DataGridRow row)
        {
            row.IsSelected = true;
            dataGrid.Focus();
        }
    }

    private void EntriesDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionActions();

    private async void NewFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentPath = RemotePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            StatusTextBlock.Text = "Open a remote target directory first.";
            return;
        }

        string? folderName = PromptDialog.Show(Window.GetWindow(this)!, "New Remote Folder", "Folder name:", "NewFolder");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Creating remote folder...");
            await _remoteService.CreateDirectoryAsync(_client, Path.Combine(currentPath, folderName.Trim()), CancellationToken.None);
            await LoadRemotePathAsync(currentPath);
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Create folder failed: {exception.Message}");
        }
    }

    private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
    {
        var remotePath = RemotePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            StatusTextBlock.Text = "Open a remote target directory first.";
            return;
        }

        if (LocalEntriesDataGrid.SelectedItem is not LocalFileSystemEntry localEntry || localEntry.IsDirectory)
        {
            StatusTextBlock.Text = "Select a local file to upload.";
            return;
        }

        try
        {
            ToggleBusy(true, $"Uploading {localEntry.Name}...");
            await _remoteService.UploadFileAsync(_client, localEntry.FullPath, Path.Combine(remotePath, localEntry.Name), CancellationToken.None);
            await LoadRemotePathAsync(remotePath);
            ToggleBusy(false, $"Uploaded {localEntry.Name} to {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Upload failed: {exception.Message}");
        }
    }

    private async void DownloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        var localPath = LocalPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            StatusTextBlock.Text = "Open a local target directory first.";
            return;
        }

        if (RemoteEntriesDataGrid.SelectedItem is not RemoteFileSystemEntry remoteEntry || remoteEntry.IsDirectory)
        {
            StatusTextBlock.Text = "Select a remote file to download.";
            return;
        }

        try
        {
            ToggleBusy(true, $"Downloading {remoteEntry.Name}...");
            var targetPath = Path.Combine(localPath, remoteEntry.Name);
            await _remoteService.DownloadFileAsync(_client, remoteEntry.FullPath, targetPath, CancellationToken.None);
            await LoadLocalPathAsync(localPath);
            ToggleBusy(false, $"Downloaded {remoteEntry.Name} to {targetPath}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Download failed: {exception.Message}");
        }
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RemoteEntriesDataGrid.SelectedItem is not RemoteFileSystemEntry entry)
        {
            StatusTextBlock.Text = "Select a remote entry first.";
            return;
        }

        var result = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
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
            await LoadRemotePathAsync(RemotePathTextBox.Text.Trim());
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Delete failed: {exception.Message}");
        }
    }

    private async void LocalBrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var initialPath = Directory.Exists(LocalPathTextBox.Text)
            ? LocalPathTextBox.Text
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var selectedPath = PromptDialog.Show(Window.GetWindow(this)!, "Local Folder", "Enter local folder path:", initialPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await LoadLocalPathAsync(selectedPath);
        }
    }

    private async void LocalOpenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (LocalEntriesDataGrid.SelectedItem is LocalFileSystemEntry entry && entry.IsDirectory)
        {
            await LoadLocalPathAsync(entry.FullPath);
        }
    }

    private async void LocalUploadMenuItem_OnClick(object sender, RoutedEventArgs e) => UploadButton_OnClick(sender, e);

    private void LocalCopyPathMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (LocalEntriesDataGrid.SelectedItem is not LocalFileSystemEntry entry)
        {
            StatusTextBlock.Text = "Select a local entry first.";
            return;
        }

        Clipboard.SetText(entry.FullPath);
        StatusTextBlock.Text = $"Copied local path: {entry.FullPath}";
    }

    private async void LocalRefreshMenuItem_OnClick(object sender, RoutedEventArgs e) => await LoadLocalPathAsync(LocalPathTextBox.Text.Trim());

    private async void RemoteOpenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (RemoteEntriesDataGrid.SelectedItem is RemoteFileSystemEntry entry && entry.IsDirectory)
        {
            await LoadRemotePathAsync(entry.FullPath);
        }
    }

    private async void RemoteDownloadMenuItem_OnClick(object sender, RoutedEventArgs e) => DownloadButton_OnClick(sender, e);

    private void RemoteCopyPathMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (RemoteEntriesDataGrid.SelectedItem is not RemoteFileSystemEntry entry)
        {
            StatusTextBlock.Text = "Select a remote entry first.";
            return;
        }

        Clipboard.SetText(entry.FullPath);
        StatusTextBlock.Text = $"Copied remote path: {entry.FullPath}";
    }

    private async void RemoteDeleteMenuItem_OnClick(object sender, RoutedEventArgs e) => DeleteButton_OnClick(sender, e);

    private async void RemoteRefreshMenuItem_OnClick(object sender, RoutedEventArgs e) => await LoadRemotePathAsync(RemotePathTextBox.Text.Trim());

    private Task LoadLocalPathAsync(string path)
    {
        try
        {
            var effectivePath = path;
            if (string.IsNullOrWhiteSpace(effectivePath))
            {
                _localEntries.Clear();
                foreach (var drive in DriveInfo.GetDrives().OrderBy(static drive => drive.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _localEntries.Add(new LocalFileSystemEntry
                    {
                        Name = drive.Name,
                        FullPath = drive.RootDirectory.FullName,
                        EntryType = "Drive"
                    });
                }

                LocalPathTextBox.Text = string.Empty;
                RememberLocalPath(string.Empty);
                UpdateSelectionActions();
                return Task.CompletedTask;
            }

            var directory = new DirectoryInfo(effectivePath);
            if (!directory.Exists)
            {
                throw new DirectoryNotFoundException($"Local path not found: {effectivePath}");
            }

            var entries = directory.EnumerateFileSystemInfos()
                .OrderBy(static info => info is not DirectoryInfo)
                .ThenBy(static info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CreateLocalEntry)
                .ToArray();

            _localEntries.Clear();
            foreach (var entry in entries)
            {
                _localEntries.Add(entry);
            }

            LocalPathTextBox.Text = effectivePath;
            RememberLocalPath(effectivePath);
            UpdateSelectionActions();
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Local refresh failed: {exception.Message}";
            return Task.CompletedTask;
        }
    }

    private async Task LoadRemotePathAsync(string path)
    {
        try
        {
            ToggleBusy(true, string.IsNullOrWhiteSpace(path) ? "Loading remote drives..." : $"Loading {path}...");
            IReadOnlyList<RemoteFileSystemEntry> entries = await _remoteService.ListDirectoryAsync(_client, path, CancellationToken.None);
            _remoteEntries.Clear();
            foreach (var entry in entries)
            {
                _remoteEntries.Add(entry);
            }

            RemotePathTextBox.Text = path;
            RememberRemotePath(path);
            ToggleBusy(false, string.IsNullOrWhiteSpace(path)
                ? $"Loaded {_remoteEntries.Count} remote drives."
                : $"Loaded {_remoteEntries.Count} remote entries from {path}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Remote refresh failed: {exception.Message}");
        }
    }

    private static LocalFileSystemEntry CreateLocalEntry(FileSystemInfo fileSystemInfo)
    {
        return new LocalFileSystemEntry
        {
            Name = fileSystemInfo.Name,
            FullPath = fileSystemInfo.FullName,
            EntryType = fileSystemInfo is DirectoryInfo ? "Directory" : "File",
            Length = fileSystemInfo is FileInfo fileInfo ? fileInfo.Length : null,
            LastWriteTimeUtc = fileSystemInfo.LastWriteTimeUtc
        };
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        LocalPathTextBox.IsEnabled = !isBusy;
        RemotePathTextBox.IsEnabled = !isBusy;
        LocalUpButton.IsEnabled = !isBusy;
        RemoteUpButton.IsEnabled = !isBusy;
        RemoteRootButton.IsEnabled = !isBusy;
        LocalBrowseButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        NewFolderButton.IsEnabled = !isBusy;
        LocalEntriesDataGrid.IsEnabled = !isBusy;
        RemoteEntriesDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        UploadButton.IsEnabled = !_isBusy
            && LocalEntriesDataGrid.SelectedItem is LocalFileSystemEntry localSelected
            && !localSelected.IsDirectory
            && !string.IsNullOrWhiteSpace(RemotePathTextBox.Text);
        DownloadButton.IsEnabled = !_isBusy
            && RemoteEntriesDataGrid.SelectedItem is RemoteFileSystemEntry remoteSelected
            && !remoteSelected.IsDirectory
            && !string.IsNullOrWhiteSpace(LocalPathTextBox.Text);
        DeleteButton.IsEnabled = !_isBusy && RemoteEntriesDataGrid.SelectedItem is RemoteFileSystemEntry;
    }

    private ExplorerViewState GetOrCreateViewState()
    {
        if (!ViewStates.TryGetValue(_client.ClientId, out var state))
        {
            state = new ExplorerViewState();
            ViewStates[_client.ClientId] = state;
        }

        return state;
    }

    private void RememberLocalPath(string path) => GetOrCreateViewState().LocalPath = path;

    private void RememberRemotePath(string path) => GetOrCreateViewState().RemotePath = path;
}
