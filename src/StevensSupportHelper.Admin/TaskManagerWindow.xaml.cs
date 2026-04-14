using System.Collections.ObjectModel;
using System.Windows;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class TaskManagerWindow : Window
{
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly ObservableCollection<RemoteProcessInfo> _processes = [];
    private bool _isBusy;

    public TaskManagerWindow(ClientRow client, PowerShellRemoteAdminService remoteService)
    {
        _client = client;
        _remoteService = remoteService;

        InitializeComponent();
        Title = $"Remote Task Manager - {_client.DeviceName}";
        ProcessesDataGrid.ItemsSource = _processes;
        Loaded += async (_, _) => await RefreshProcessesAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshProcessesAsync();
    }

    private void ProcessesDataGrid_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();
    }

    private async void StartProcessButton_OnClick(object sender, RoutedEventArgs e)
    {
        string? filePath = PromptDialog.Show(this, "Start Process", "Executable or script path on the remote machine:", "notepad.exe");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string? arguments = PromptDialog.Show(this, "Start Process", "Arguments (optional):", string.Empty) ?? string.Empty;

        try
        {
            ToggleBusy(true, "Starting remote process...");
            RemoteProcessInfo process = await _remoteService.StartProcessAsync(_client, filePath.Trim(), arguments.Trim(), CancellationToken.None);
            await RefreshProcessesAsync();
            StatusTextBlock.Text = $"Started {process.ProcessName} (PID {process.Id}).";
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Start process failed: {exception.Message}");
        }
    }

    private async void KillProcessButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProcessesDataGrid.SelectedItem is not RemoteProcessInfo process)
        {
            StatusTextBlock.Text = "Select a process first.";
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Kill {process.ProcessName} (PID {process.Id}) on {_client.DeviceName}?",
            "Confirm Kill",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Stopping remote process...");
            await _remoteService.KillProcessAsync(_client, process.Id, CancellationToken.None);
            await RefreshProcessesAsync();
            StatusTextBlock.Text = $"Stopped {process.ProcessName} (PID {process.Id}).";
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Kill process failed: {exception.Message}");
        }
    }

    private async Task RefreshProcessesAsync()
    {
        try
        {
            ToggleBusy(true, "Loading remote process list...");
            IReadOnlyList<RemoteProcessInfo> processes = await _remoteService.ListProcessesAsync(_client, CancellationToken.None);
            _processes.Clear();
            foreach (var process in processes)
            {
                _processes.Add(process);
            }

            ToggleBusy(false, $"Loaded {_processes.Count} processes from {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Refresh failed: {exception.Message}");
        }
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RefreshButton.IsEnabled = !isBusy;
        StartProcessButton.IsEnabled = !isBusy;
        ProcessesDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        KillProcessButton.IsEnabled = !_isBusy && ProcessesDataGrid.SelectedItem is RemoteProcessInfo;
    }
}
