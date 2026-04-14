using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class RemoteActionTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly RemoteActionScriptService _scriptService;
    private readonly string _scriptDirectory;
    private readonly ObservableCollection<RemoteActionScript> _scripts = [];
    private bool _isBusy;
    private bool _canRun;
    private bool _canRunViaAgent;
    private bool _canRunViaWinRm;

    public RemoteActionTab(
        ClientRow client,
        PowerShellRemoteAdminService remoteService,
        AdminApiClient apiClient,
        string serverUrl,
        string apiKey,
        string? mfaCode,
        RemoteActionScriptService scriptService,
        string scriptDirectory,
        RepairPrecheckResult precheck)
    {
        _client = client;
        _remoteService = remoteService;
        _apiClient = apiClient;
        _serverUrl = serverUrl;
        _apiKey = apiKey;
        _mfaCode = mfaCode;
        _scriptService = scriptService;
        _scriptDirectory = scriptDirectory;
        InitializeComponent();

        ScriptsListBox.ItemsSource = _scripts;
        ClientSummaryTextBlock.Text = $"Client: {_client.DeviceName} ({_client.MachineName})";
        ScriptPathTextBlock.Text = $"Script folder: {_scriptDirectory}";
        ApplyPrecheck(precheck);
        LoadScripts();
    }

    private void ApplyPrecheck(RepairPrecheckResult precheck)
    {
        AgentStatusTextBlock.Text = _client.IsOnline ? "Agent: online" : "Agent: offline";
        AgentStatusTextBlock.Foreground = _client.IsOnline
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
            : new SolidColorBrush(Color.FromRgb(185, 28, 28));
        ReachabilityTextBlock.Text = precheck.IsReachable ? "WinRM: reachable" : "WinRM: not reachable";
        ReachabilityTextBlock.Foreground = precheck.IsReachable
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
            : new SolidColorBrush(Color.FromRgb(185, 28, 28));
        PrecheckMessageTextBlock.Text = precheck.Message;
        _canRunViaAgent = _client.IsOnline;
        _canRunViaWinRm = precheck.IsReachable && precheck.HasCredentials;
        _canRun = _canRunViaAgent || _canRunViaWinRm;
        RunButton.IsEnabled = _canRun;
    }

    private void LoadScripts()
    {
        _scripts.Clear();
        foreach (var script in _scriptService.LoadScripts(_scriptDirectory))
        {
            _scripts.Add(script);
        }

        if (_scripts.Count > 0)
        {
            ScriptsListBox.SelectedIndex = 0;
        }
    }

    private void ScriptsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptsListBox.SelectedItem is not RemoteActionScript script)
        {
            SelectedScriptTextBlock.Text = "Script: -";
            ScriptEditorTextBox.Text = string.Empty;
            return;
        }

        SelectedScriptTextBlock.Text = $"Script: {System.IO.Path.GetFileName(script.Path)}";
        ScriptEditorTextBox.Text = script.Content;
    }

    private void RefreshScriptsButton_OnClick(object sender, RoutedEventArgs e)
    {
        LoadScripts();
        StatusTextBlock.Text = "Scripts refreshed.";
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ScriptsListBox.SelectedItem is not RemoteActionScript script)
        {
            StatusTextBlock.Text = "Select a script first.";
            return;
        }

        var updated = script with { Content = ScriptEditorTextBox.Text };
        _scriptService.SaveScript(updated);
        var index = _scripts.IndexOf(script);
        _scripts[index] = updated;
        ScriptsListBox.SelectedItem = updated;
        StatusTextBlock.Text = $"Saved {System.IO.Path.GetFileName(updated.Path)}.";
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (ScriptsListBox.SelectedItem is not RemoteActionScript script)
        {
            StatusTextBlock.Text = "Select a script first.";
            return;
        }

        try
        {
            ToggleBusy(true, $"Running {script.Name}...");
            var updated = script with { Content = ScriptEditorTextBox.Text };
            _scriptService.SaveScript(updated);
            var output = await TryRunViaAgentAsync(updated.Content, CancellationToken.None);
            output ??= await _remoteService.ExecuteRemoteActionScriptAsync(_client, updated.Content, CancellationToken.None);
            OutputTextBox.Text = output;
            ToggleBusy(false, $"Finished running {script.Name}.");
        }
        catch (Exception exception)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Remote action failed.");
            builder.AppendLine(exception.Message);
            OutputTextBox.Text = builder.ToString().Trim();
            ToggleBusy(false, $"Run failed: {exception.Message}");
        }
    }

    private async Task<string?> TryRunViaAgentAsync(string scriptContent, CancellationToken cancellationToken)
    {
        if (!_canRunViaAgent)
        {
            return null;
        }

        try
        {
            StatusTextBlock.Text = $"Queueing script on agent for {_client.DeviceName}...";
            var queued = await _apiClient.QueueScriptExecutionJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, scriptContent, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentScriptExecutionResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no script execution payload.");

            var builder = new StringBuilder();
            builder.AppendLine($"Execution host: {result.HostApplication}");
            builder.AppendLine($"Exit code: {result.ExitCode}");
            builder.AppendLine();
            builder.AppendLine("STDOUT:");
            builder.AppendLine(string.IsNullOrWhiteSpace(result.Output) ? "<empty>" : result.Output);
            builder.AppendLine();
            builder.AppendLine("STDERR:");
            builder.AppendLine(string.IsNullOrWhiteSpace(result.ErrorOutput) ? "<empty>" : result.ErrorOutput);
            return builder.ToString().Trim();
        }
        catch
        {
            if (_canRunViaWinRm)
            {
                StatusTextBlock.Text = $"Agent run failed, falling back to WinRM for {_client.DeviceName}...";
                return null;
            }

            throw;
        }
    }

    private async Task<AgentJobDto> WaitForAgentJobCompletionAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var job = await _apiClient.GetAgentJobAsync(_serverUrl, _apiKey, _mfaCode, jobId, cancellationToken);
            if (job.Status is "Completed" or "Failed")
            {
                return job;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for the agent script execution.");
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        ScriptsListBox.IsEnabled = !isBusy;
        ScriptEditorTextBox.IsEnabled = !isBusy;
        RunButton.IsEnabled = !isBusy && _canRun;
        StatusTextBlock.Text = status;
    }
}
