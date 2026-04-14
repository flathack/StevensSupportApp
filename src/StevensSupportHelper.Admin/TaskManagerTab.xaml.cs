using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class TaskManagerTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly ObservableCollection<RemoteProcessInfo> _processes = [];
    private List<RemoteProcessInfo> _allProcesses = [];
    private bool _isBusy;

    public TaskManagerTab(
        ClientRow client,
        PowerShellRemoteAdminService remoteService,
        AdminApiClient apiClient,
        string serverUrl,
        string apiKey,
        string? mfaCode)
    {
        _client = client;
        _remoteService = remoteService;
        _apiClient = apiClient;
        _serverUrl = serverUrl;
        _apiKey = apiKey;
        _mfaCode = mfaCode;

        InitializeComponent();
        ProcessesDataGrid.ItemsSource = _processes;
        SearchTextBox.Text = string.Empty;
        Loaded += async (_, _) => await RefreshProcessesAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ProcessesDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionActions();

    private async void StartProcessButton_OnClick(object sender, RoutedEventArgs e)
    {
        string? filePath = PromptDialog.Show(Window.GetWindow(this)!, "Start Process", "Executable or script path on the remote machine:", "notepad.exe");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string? arguments = PromptDialog.Show(Window.GetWindow(this)!, "Start Process", "Arguments (optional):", string.Empty) ?? string.Empty;

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
            Window.GetWindow(this),
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
            (IReadOnlyList<RemoteProcessInfo> processes, RemoteSystemSummary summary) = await TryLoadViaAgentAsync(CancellationToken.None)
                ?? await LoadViaPowerShellAsync(CancellationToken.None);

            _allProcesses = processes
                .OrderByDescending(static process => process.WorkingSetMb)
                .ThenBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CpuTextBlock.Text = $"{summary.CpuPercent:N1} %";
            MemoryTextBlock.Text = $"{summary.UsedMemoryGb:N2} / {summary.TotalMemoryGb:N2} GB";
            MemoryPercentTextBlock.Text = $"{summary.MemoryPercent:N1} %";
            ProcessCountTextBlock.Text = $"{summary.ProcessCount:N0}";

            ApplyFilter();
            ToggleBusy(false, $"Loaded {_allProcesses.Count} processes from {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Refresh failed: {exception.Message}");
        }
    }

    private async Task<(IReadOnlyList<RemoteProcessInfo> Processes, RemoteSystemSummary Summary)?> TryLoadViaAgentAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            ToggleBusy(true, "Requesting process snapshot from agent...");
            var queued = await _apiClient.QueueProcessSnapshotJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            if (string.IsNullOrWhiteSpace(job.ResultJson))
            {
                throw new InvalidOperationException("Agent job returned no process snapshot.");
            }

            var result = JsonSerializer.Deserialize<AgentProcessSnapshotResult>(job.ResultJson, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned invalid process snapshot payload.");
            return (
                result.Processes.Select(static process => new RemoteProcessInfo
                {
                    Id = process.Id,
                    ProcessName = process.ProcessName,
                    MainWindowTitle = process.MainWindowTitle,
                    CpuSeconds = process.CpuSeconds,
                    WorkingSetMb = process.WorkingSetMb,
                    StartTimeUtc = process.StartTimeUtc
                }).ToArray(),
                new RemoteSystemSummary
                {
                    ProcessCount = result.Summary.ProcessCount,
                    CpuPercent = result.Summary.CpuPercent,
                    UsedMemoryGb = result.Summary.UsedMemoryGb,
                    TotalMemoryGb = result.Summary.TotalMemoryGb,
                    MemoryPercent = result.Summary.MemoryPercent
                });
        }
        catch
        {
            return null;
        }
    }

    private async Task<(IReadOnlyList<RemoteProcessInfo> Processes, RemoteSystemSummary Summary)> LoadViaPowerShellAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RemoteProcessInfo> processes = await _remoteService.ListProcessesAsync(_client, cancellationToken);
        RemoteSystemSummary summary = await _remoteService.GetSystemSummaryAsync(_client, cancellationToken);
        return (processes, summary);
    }

    private async Task<AgentJobDto> WaitForAgentJobCompletionAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var job = await _apiClient.GetAgentJobAsync(_serverUrl, _apiKey, _mfaCode, jobId, cancellationToken);
            if (job.Status is "Completed" or "Failed")
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for the agent process snapshot.");
    }

    private void ApplyFilter()
    {
        var filter = SearchTextBox.Text.Trim();
        IEnumerable<RemoteProcessInfo> filtered = _allProcesses;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(process =>
                process.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(process.MainWindowTitle) && process.MainWindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                process.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        _processes.Clear();
        foreach (var process in filtered)
        {
            _processes.Add(process);
        }

        UpdateSelectionActions();
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RefreshButton.IsEnabled = !isBusy;
        StartProcessButton.IsEnabled = !isBusy;
        SearchTextBox.IsEnabled = !isBusy;
        ProcessesDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        KillProcessButton.IsEnabled = !_isBusy && ProcessesDataGrid.SelectedItem is RemoteProcessInfo;
    }
}
