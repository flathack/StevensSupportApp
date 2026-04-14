using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class PowerOptionsTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly ObservableCollection<RemotePowerPlan> _plans = [];

    public PowerOptionsTab(
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
        PlansDataGrid.ItemsSource = _plans;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void ActivateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlansDataGrid.SelectedItem is not RemotePowerPlan plan)
        {
            StatusTextBlock.Text = "Select a power plan first.";
            return;
        }

        try
        {
            ActivateButton.IsEnabled = false;
            StatusTextBlock.Text = $"Activating {plan.Name}...";
            var activated = await TryActivateViaAgentAsync(plan.Guid, CancellationToken.None);
            if (activated is null)
            {
                await _remoteService.SetActivePowerPlanAsync(_client, plan.Guid, CancellationToken.None);
            }
            else
            {
                StatusTextBlock.Text = activated;
            }

            await RefreshAsync();
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Activating power plan failed: {exception.Message}";
        }
        finally
        {
            ActivateButton.IsEnabled = true;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusTextBlock.Text = $"Loading power plans for {_client.DeviceName}...";
            var plans = await TryLoadViaAgentAsync(CancellationToken.None)
                ?? await _remoteService.ListPowerPlansAsync(_client, CancellationToken.None);
            _plans.Clear();
            foreach (var plan in plans)
            {
                _plans.Add(plan);
            }

            SummaryTextBlock.Text = _plans.FirstOrDefault(item => item.IsActive) is { } active
                ? $"Active power plan: {active.Name}"
                : "Power plans";
            StatusTextBlock.Text = $"Loaded {_plans.Count} power plans.";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Loading power plans failed: {exception.Message}";
        }
    }

    private async Task<IReadOnlyList<RemotePowerPlan>?> TryLoadViaAgentAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            var queued = await _apiClient.QueuePowerPlanSnapshotJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentPowerPlanSnapshotResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no power plan payload.");
            return result.Plans.Select(static plan => new RemotePowerPlan(plan.Guid, plan.Name, plan.IsActive)).ToArray();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryActivateViaAgentAsync(string planGuid, CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            var queued = await _apiClient.QueuePowerPlanActivateJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, planGuid, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentPowerPlanActivateResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no power plan activation payload.");
            return result.Message;
        }
        catch
        {
            return null;
        }
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

        throw new TimeoutException("Timed out waiting for the agent power plan job.");
    }
}
