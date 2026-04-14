using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class WindowsUpdatesTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly ObservableCollection<RemoteWindowsUpdateItem> _updates = [];

    public WindowsUpdatesTab(
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
        UpdatesDataGrid.ItemsSource = _updates;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void InstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            InstallButton.IsEnabled = false;
            StatusTextBlock.Text = $"Installing available Windows updates on {_client.DeviceName}...";
            var result = await TryInstallViaAgentAsync(CancellationToken.None)
                ?? await _remoteService.InstallAvailableWindowsUpdatesAsync(_client, CancellationToken.None);
            StatusTextBlock.Text = result;
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Installing updates failed: {exception.Message}";
        }
        finally
        {
            InstallButton.IsEnabled = true;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusTextBlock.Text = $"Loading available updates for {_client.DeviceName}...";
            var updates = await TryLoadViaAgentAsync(CancellationToken.None)
                ?? await _remoteService.ListAvailableWindowsUpdatesAsync(_client, CancellationToken.None);
            _updates.Clear();
            foreach (var update in updates)
            {
                _updates.Add(update with { MaxDownloadSizeBytes = update.MaxDownloadSizeBytes / (1024 * 1024) });
            }

            SummaryTextBlock.Text = _updates.Count == 0
                ? "No pending Windows updates."
                : $"{_updates.Count} Windows updates available";
            StatusTextBlock.Text = $"Loaded {_updates.Count} available Windows updates.";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Loading Windows updates failed: {exception.Message}";
        }
    }

    private async Task<IReadOnlyList<RemoteWindowsUpdateItem>?> TryLoadViaAgentAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            var queued = await _apiClient.QueueWindowsUpdateScanJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, TimeSpan.FromSeconds(30), cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentWindowsUpdateScanResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no Windows update scan payload.");
            return result.Updates.Select(static update => new RemoteWindowsUpdateItem(
                update.Title,
                update.KbArticleIds,
                update.Categories,
                update.IsDownloaded,
                update.MaxDownloadSizeBytes)).ToArray();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryInstallViaAgentAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            var queued = await _apiClient.QueueWindowsUpdateInstallJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, TimeSpan.FromMinutes(8), cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentWindowsUpdateInstallResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no Windows update install payload.");
            return result.Message;
        }
        catch
        {
            return null;
        }
    }

    private async Task<AgentJobDto> WaitForAgentJobCompletionAsync(Guid jobId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var job = await _apiClient.GetAgentJobAsync(_serverUrl, _apiKey, _mfaCode, jobId, cancellationToken);
            if (job.Status is "Completed" or "Failed")
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for the Windows update agent job.");
    }
}
