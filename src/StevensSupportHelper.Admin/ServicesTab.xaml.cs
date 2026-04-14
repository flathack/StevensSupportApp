using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class ServicesTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientRow _client;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly ObservableCollection<RemoteServiceInfo> _services = [];
    private List<RemoteServiceInfo> _allServices = [];
    private bool _isBusy;

    public ServicesTab(
        ClientRow client,
        AdminApiClient apiClient,
        string serverUrl,
        string apiKey,
        string? mfaCode)
    {
        _client = client;
        _apiClient = apiClient;
        _serverUrl = serverUrl;
        _apiKey = apiKey;
        _mfaCode = mfaCode;

        InitializeComponent();
        ServicesDataGrid.ItemsSource = _services;
        Loaded += async (_, _) => await RefreshServicesAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshServicesAsync();

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ServicesDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionActions();

    private async void StartButton_OnClick(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("start");

    private async void StopButton_OnClick(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("stop");

    private async void RestartButton_OnClick(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("restart");

    private async Task RefreshServicesAsync()
    {
        try
        {
            ToggleBusy(true, $"Loading services from {_client.DeviceName}...");
            var queued = await _apiClient.QueueServiceSnapshotJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, CancellationToken.None);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, TimeSpan.FromSeconds(25), CancellationToken.None);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentServiceSnapshotResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no service snapshot payload.");

            _allServices = result.Services
                .Select(static service => new RemoteServiceInfo
                {
                    Name = service.Name,
                    DisplayName = service.DisplayName,
                    Status = service.Status,
                    StartType = service.StartType,
                    CanStop = service.CanStop
                })
                .OrderBy(static service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static service => service.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ApplyFilter();
            ToggleBusy(false, $"Loaded {_allServices.Count} services from {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Loading services failed: {exception.Message}");
        }
    }

    private async Task ControlSelectedServiceAsync(string action)
    {
        if (ServicesDataGrid.SelectedItem is not RemoteServiceInfo service)
        {
            StatusTextBlock.Text = "Select a service first.";
            return;
        }

        var verb = action switch
        {
            "start" => "start",
            "stop" => "stop",
            _ => "restart"
        };
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"{char.ToUpperInvariant(verb[0])}{verb[1..]} service '{service.DisplayName}' on {_client.DeviceName}?",
            "Confirm Service Action",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"{char.ToUpperInvariant(verb[0])}{verb[1..]}ing service...");
            var queued = await _apiClient.QueueServiceControlJobAsync(
                _serverUrl,
                _apiKey,
                _mfaCode,
                _client.ClientId,
                service.Name,
                action,
                CancellationToken.None);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, TimeSpan.FromSeconds(35), CancellationToken.None);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var controlResult = JsonSerializer.Deserialize<AgentServiceControlResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no service control payload.");

            await RefreshServicesAsync();
            StatusTextBlock.Text = controlResult.Message;
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Service action failed: {exception.Message}");
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

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for the agent service job.");
    }

    private void ApplyFilter()
    {
        var filter = SearchTextBox.Text.Trim();
        IEnumerable<RemoteServiceInfo> filtered = _allServices;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(service =>
                service.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                service.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                service.Status.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                service.StartType.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        _services.Clear();
        foreach (var service in filtered)
        {
            _services.Add(service);
        }

        UpdateSelectionActions();
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RefreshButton.IsEnabled = !isBusy;
        SearchTextBox.IsEnabled = !isBusy;
        ServicesDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        if (_isBusy || ServicesDataGrid.SelectedItem is not RemoteServiceInfo service)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            RestartButton.IsEnabled = false;
            return;
        }

        var status = service.Status.Trim();
        var isRunning = string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
        var isStopped = string.Equals(status, "Stopped", StringComparison.OrdinalIgnoreCase);

        StartButton.IsEnabled = !isRunning;
        StopButton.IsEnabled = !isStopped && service.CanStop;
        RestartButton.IsEnabled = isRunning && service.CanStop;
    }
}
