using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class DashboardTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private bool _isBusy;

    public DashboardTab(
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
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            ToggleBusy(true, "Loading dashboard data...");
            var summary = await TryLoadViaAgentAsync(CancellationToken.None)
                ?? await _remoteService.GetSystemSummaryAsync(_client, CancellationToken.None);

            CpuTextBlock.Text = $"{summary.CpuPercent:N1} %";
            RamTextBlock.Text = $"{summary.UsedMemoryGb:N2} / {summary.TotalMemoryGb:N2} GB";
            BatteryTextBlock.Text = _client.HasBatteryInfo ? _client.BatteryText.Replace("Battery ", string.Empty, StringComparison.Ordinal) : "n/a";
            UptimeTextBlock.Text = string.IsNullOrWhiteSpace(_client.UptimeSummary) ? "n/a" : _client.UptimeSummary.Replace("Uptime ", string.Empty, StringComparison.Ordinal);
            MemorySummaryTextBlock.Text = $"{summary.MemoryPercent:N1} % used";
            MemoryBar.Width = 460d * Math.Clamp(summary.MemoryPercent / 100d, 0d, 1d);
            RenderDiskBars();
            ToggleBusy(false, $"Dashboard refreshed for {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Dashboard refresh failed: {exception.Message}");
        }
    }

    private async Task<RemoteSystemSummary?> TryLoadViaAgentAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            var queued = await _apiClient.QueueProcessSnapshotJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, cancellationToken);
            var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < timeoutAt)
            {
                var job = await _apiClient.GetAgentJobAsync(_serverUrl, _apiKey, _mfaCode, queued.JobId, cancellationToken);
                if (job.Status is "Completed" or "Failed")
                {
                    if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
                    }

                    var result = JsonSerializer.Deserialize<AgentProcessSnapshotResult>(job.ResultJson ?? string.Empty, JsonOptions)
                        ?? throw new InvalidOperationException("Agent returned no dashboard payload.");
                    return new RemoteSystemSummary
                    {
                        CpuPercent = result.Summary.CpuPercent,
                        MemoryPercent = result.Summary.MemoryPercent,
                        ProcessCount = result.Summary.ProcessCount,
                        TotalMemoryGb = result.Summary.TotalMemoryGb,
                        UsedMemoryGb = result.Summary.UsedMemoryGb
                    };
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch
        {
        }

        return null;
    }

    private void RenderDiskBars()
    {
        DiskItemsControl.Items.Clear();
        foreach (var disk in _client.DiskUsages)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = $"{disk.UsageSummary} | {disk.CapacitySummary}",
                FontWeight = FontWeights.SemiBold
            };

            var barGrid = new Grid
            {
                Margin = new Thickness(0, 6, 0, 0),
                Width = 460,
                Height = 12
            };
            barGrid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(35, 48, 71)),
                CornerRadius = new CornerRadius(6)
            });
            barGrid.Children.Add(new Border
            {
                Width = disk.UsedBarWidth * (460d / 164d),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                CornerRadius = new CornerRadius(6)
            });

            Grid.SetRow(header, 0);
            Grid.SetRow(barGrid, 1);
            grid.Children.Add(header);
            grid.Children.Add(barGrid);
            DiskItemsControl.Items.Add(grid);
        }
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RefreshButton.IsEnabled = !isBusy;
        StatusTextBlock.Text = status;
    }
}
