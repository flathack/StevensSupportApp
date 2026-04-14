using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class MainWindow : Window
{
    private static readonly TimeSpan FastRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan NormalRefreshInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FailureRefreshInterval = TimeSpan.FromSeconds(15);
    private readonly AdminApiClient _apiClient;
    private RemoteLauncher _remoteLauncher;
    private readonly PowerShellRemoteAdminService _powerShellRemoteService;
    private readonly AdminSettingsStore _settingsStore;
    private readonly RemoteActionScriptService _remoteActionScriptService;
    private readonly LocalServerLauncher _localServerLauncher;
    private readonly ObservableCollection<ClientRow> _clients = [];
    private readonly ObservableCollection<AuditEntryDto> _auditEntries = [];
    private readonly ObservableCollection<AdminLogEntry> _logEntries = [];
    private readonly ObservableCollection<AdminToastNotification> _toastNotifications = [];
    private readonly Dictionary<Guid, bool> _knownClientOnlineStates = [];
    private readonly Dictionary<Guid, DateTimeOffset?> _lastViewedClientChatAt = [];
    private readonly Dictionary<Guid, DateTimeOffset?> _knownLastClientChatAt = [];
    private readonly DispatcherTimer _refreshTimer;
    private AdminSessionInfoResponse? _adminSession;
    private string _serverUrl = "http://localhost:5000";
    private string _apiKey = string.Empty;
    private string _serverProjectPath = string.Empty;
    private string _rustDeskPath = string.Empty;
    private string _rustDeskPassword = string.Empty;
    private string _clientInstallerPath = string.Empty;
    private string _remoteActionsPath = string.Empty;
    private string _packageGeneratorPath = AdminSettingsStore.ResolveDefaultPackageGeneratorPath();
    private string _localClientPackageVersion = string.Empty;
    private string _remoteUserName = string.Empty;
    private string _remotePassword = string.Empty;
    private AdminThemeMode _themeMode = AdminThemeMode.Light;
    private bool _isRefreshing;
    private bool _isLoadingSettings;
    private int _clientRefreshCount;

    public ObservableCollection<AdminToastNotification> ToastNotifications => _toastNotifications;

    public MainWindow(AdminApiClient apiClient)
    {
        _apiClient = apiClient;
        _remoteLauncher = new RemoteLauncher();
        _powerShellRemoteService = new PowerShellRemoteAdminService();
        _settingsStore = new AdminSettingsStore();
        _remoteActionScriptService = new RemoteActionScriptService();
        _localServerLauncher = new LocalServerLauncher();
        InitializeComponent();
        Title = $"StevensSupportHelper Admin {GetAdminVersionText()}";

        ClientsDataGrid.ItemsSource = _clients;
        AuditListView.ItemsSource = _auditEntries;
        LogListView.ItemsSource = _logEntries;
        LoadPersistedSettings();
        WirePersistenceEvents();

        _refreshTimer = new DispatcherTimer
        {
            Interval = NormalRefreshInterval
        };
        _refreshTimer.Tick += async (_, _) => await RefreshClientsAsync(refreshAudit: false);
        LogInfo("Admin client started.");

        Loaded += async (_, _) =>
        {
            await RefreshClientsAsync(refreshAudit: true);
            _refreshTimer.Start();
        };

        Closing += (_, _) => PersistSettings();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshClientsAsync(refreshAudit: true);

    private void SettingsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_serverUrl, _apiKey, _serverProjectPath, _rustDeskPath, _rustDeskPassword, _clientInstallerPath, _remoteActionsPath, _packageGeneratorPath, _remoteUserName, _remotePassword)
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        _serverUrl = window.ServerUrl;
        _apiKey = window.ApiKey;
        _serverProjectPath = window.ServerProjectPath;
        _rustDeskPath = window.RustDeskPath;
        _rustDeskPassword = window.RustDeskPassword;
        _clientInstallerPath = window.ClientInstallerPath;
        _remoteActionsPath = _remoteActionScriptService.EnsureScriptDirectory(window.RemoteActionsPath);
        _packageGeneratorPath = window.PackageGeneratorPath;
        _localClientPackageVersion = ResolveClientPackageVersion(_clientInstallerPath);
        _remoteUserName = window.RemoteUserName;
        _remotePassword = window.RemotePassword;
        _remoteLauncher = new RemoteLauncher(_rustDeskPath, _rustDeskPassword);
        _powerShellRemoteService.UpdateDefaultCredentials(_remoteUserName, _remotePassword);
        UpdateStartServerButtonState();
        PersistSettings();
        SetStatus("Settings saved.");
    }

    private void GenerateInstallPackageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new GenerateInstallPackageWindow(_serverUrl, _clientInstallerPath, _packageGeneratorPath)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
            {
                return;
            }

            SetStatus($"Install package created at {window.OutputZipPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Install package generation failed: {exception.Message}", isError: true);
        }
    }

    private void StartServerButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _localServerLauncher.Start(_serverProjectPath, _serverUrl);
            SetStatus($"Local server start requested for {_serverUrl}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Start server failed: {exception.Message}", isError: true);
        }
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) => Close();

    private void LightThemeMenuItem_OnClick(object sender, RoutedEventArgs e) => ApplyTheme(AdminThemeMode.Light);

    private void DarkThemeMenuItem_OnClick(object sender, RoutedEventArgs e) => ApplyTheme(AdminThemeMode.Dark);

    private void HelpMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        new HelpWindow
        {
            Owner = this
        }.ShowDialog();
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow
        {
            Owner = this
        }.ShowDialog();
    }

    private async void RequestSupportButton_OnClick(object sender, RoutedEventArgs e) => await RequestSupportForSelectedClientAsync();

    private async void ClientsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot launch or request support sessions.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is ClientRow selected && selected.HasActiveSession)
        {
            ConnectToSelectedClient(DeterminePreferredConnectChannel(selected));
            return;
        }

        await RequestSupportForSelectedClientAsync();
    }

    private void ClientsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();

        if (ClientsDataGrid.SelectedItem is ClientRow client && CurrentAdminCanManage())
        {
            var launchCheck = _remoteLauncher.Check(client, DeterminePreferredConnectChannel(client));
            if (!launchCheck.CanLaunch)
            {
                SetStatus(launchCheck.Message, isError: true);
            }
            else if (launchCheck.Warnings.Count > 0)
            {
                SetStatus(launchCheck.Message + " " + string.Join(" ", launchCheck.Warnings));
            }
        }
    }

    private async Task RefreshClientsAsync(bool refreshAudit = false)
    {
        if (_isRefreshing)
        {
            return;
        }

        try
        {
            _isRefreshing = true;
            RefreshButton.IsEnabled = false;
            Guid? selectedClientId = (ClientsDataGrid.SelectedItem as ClientRow)?.ClientId;
            string serverBaseUrl = _serverUrl;
            string apiKey = _apiKey;
            string mfaCode = MfaCodeTextBox.Text.Trim();

            _adminSession = await _apiClient.GetSessionInfoAsync(serverBaseUrl, apiKey, mfaCode, CancellationToken.None);
            UpdateMfaVisibility(_adminSession.RequiresMfa);
            IReadOnlyList<AdminClientSummary> clients = await _apiClient.GetClientsAsync(serverBaseUrl, apiKey, mfaCode, CancellationToken.None);
            ApplyClientSnapshot(clients);
            RestoreClientSelection(selectedClientId);

            _clientRefreshCount++;
            bool shouldRefreshAudit = refreshAudit || _clientRefreshCount % 4 == 0;
            if (shouldRefreshAudit)
            {
                IReadOnlyList<AuditEntryDto> auditEntries = await _apiClient.GetAuditEntriesAsync(serverBaseUrl, apiKey, mfaCode, 25, CancellationToken.None);
                _auditEntries.Clear();
                foreach (var auditEntry in auditEntries)
                {
                    _auditEntries.Add(auditEntry);
                }
            }

            UpdateSelectionActions();
            UpdateRefreshInterval(clients);
            UpdateServerStatusIndicator(isOnline: true);
            SetStatus($"Loaded {_clients.Count} clients as {_adminSession.DisplayName} ({string.Join(", ", _adminSession.Roles)}) at {DateTime.Now:T}.", log: false);
        }
        catch (Exception exception)
        {
            _adminSession = null;
            UpdateSelectionActions();
            _refreshTimer.Interval = FailureRefreshInterval;
            UpdateServerStatusIndicator(isOnline: false);
            UpdateMfaVisibility(ShouldPromptForMfa(exception.Message));
            SetStatus($"Refresh failed: {exception.Message}", isError: true);
        }
        finally
        {
            _isRefreshing = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplyClientSnapshot(IReadOnlyList<AdminClientSummary> clients)
    {
        var ordered = clients
            .OrderByDescending(client => client.IsOnline)
            .ThenBy(client => client.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existing = _clients.ToDictionary(client => client.ClientId);
        var orderedRows = new List<ClientRow>(ordered.Length);

        foreach (var summary in ordered)
        {
            if (!existing.TryGetValue(summary.ClientId, out var row))
            {
                row = ClientRow.FromSummary(summary);
                HandleClientPresenceTransition(row, previousStateKnown: false, previousIsOnline: false);
            }
            else
            {
                var previousIsOnline = row.IsOnline;
                row.Apply(summary);
                HandleClientPresenceTransition(row, previousStateKnown: true, previousIsOnline);
            }

            ApplyAvailableUpdateInfo(row);
            ApplyChatViewState(row);
            HandleClientChatTransition(row);

            orderedRows.Add(row);
        }

        for (var index = _clients.Count - 1; index >= 0; index--)
        {
            if (orderedRows.All(row => row.ClientId != _clients[index].ClientId))
            {
                _clients.RemoveAt(index);
            }
        }

        for (var index = 0; index < orderedRows.Count; index++)
        {
            if (index >= _clients.Count)
            {
                _clients.Add(orderedRows[index]);
                continue;
            }

            if (_clients[index].ClientId == orderedRows[index].ClientId)
            {
                continue;
            }

            var currentIndex = _clients.IndexOf(orderedRows[index]);
            if (currentIndex >= 0)
            {
                _clients.Move(currentIndex, index);
            }
            else
            {
                _clients.Insert(index, orderedRows[index]);
            }
        }

        UpdateOpenClientWorkspaces();
    }

    private async Task RequestSupportForSelectedClientAsync()
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot create support requests.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        try
        {
            ToggleBusy(true, $"Queueing support request for {client.DeviceName}...");
            var response = await _apiClient.CreateSupportRequestAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                client.ClientId,
                new CreateSupportRequestRequest(
                    _adminSession?.DisplayName ?? string.Empty,
                    DetermineSupportRequestChannel(client),
                    string.IsNullOrWhiteSpace(ReasonTextBox.Text) ? "Remote support requested." : ReasonTextBox.Text.Trim()),
                CancellationToken.None);

            SetStatus(response.Message);
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            SetStatus($"Support request failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        ConnectToSelectedClient(DeterminePreferredConnectChannel(client));
    }

    private void RdpConnectButton_OnClick(object sender, RoutedEventArgs e) => ConnectToSelectedClient(RemoteChannel.Rdp);

    private void RustDeskConnectButton_OnClick(object sender, RoutedEventArgs e) => ConnectToSelectedClient(RemoteChannel.RustDesk);

    private async void EndSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot end sessions.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        try
        {
            ToggleBusy(true, $"Ending session for {client.DeviceName}...");
            var response = await _apiClient.EndActiveSessionAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                client.ClientId,
                CancellationToken.None);
            SetStatus(response.Message);
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            SetStatus($"End session failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private async void UploadFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            StatusTextBlock.Text = "Current role is read-only and cannot upload files.";
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            StatusTextBlock.Text = "Select a client first.";
            return;
        }

        var openFileDialog = new OpenFileDialog();
        if (openFileDialog.ShowDialog(this) != true)
        {
            return;
        }

        string fileName = Path.GetFileName(openFileDialog.FileName);
        string? targetRelativePath = PromptDialog.Show(this, "Upload Target", "Relative target path inside the client's managed files root:", fileName);
        if (string.IsNullOrWhiteSpace(targetRelativePath))
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Queueing upload for {client.DeviceName}...");
            byte[] bytes = await File.ReadAllBytesAsync(openFileDialog.FileName);
            var queued = await _apiClient.QueueUploadTransferAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                client.ClientId,
                new QueueFileUploadRequest(fileName, targetRelativePath, Convert.ToBase64String(bytes)),
                CancellationToken.None);

            FileTransferDto result = await WaitForTransferCompletionAsync(queued.TransferId, CancellationToken.None);
            StatusTextBlock.Text = result.Status == "Completed"
                ? $"Upload completed for {client.DeviceName}."
                : $"Upload finished with status {result.Status}.";
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Upload failed: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private async void DownloadFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            StatusTextBlock.Text = "Current role is read-only and cannot download files.";
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            StatusTextBlock.Text = "Select a client first.";
            return;
        }

        string? sourceRelativePath = PromptDialog.Show(this, "Download Source", "Relative source path inside the client's managed files root:", "example.txt");
        if (string.IsNullOrWhiteSpace(sourceRelativePath))
        {
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(sourceRelativePath)
        };
        if (saveFileDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Queueing download from {client.DeviceName}...");
            var queued = await _apiClient.QueueDownloadTransferAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                client.ClientId,
                new QueueFileDownloadRequest(sourceRelativePath),
                CancellationToken.None);

            FileTransferDto result = await WaitForTransferCompletionAsync(queued.TransferId, CancellationToken.None);
            if (result.Status != "Completed")
            {
                throw new InvalidOperationException($"Download finished with status {result.Status}: {result.ErrorMessage}");
            }

            FileTransferContentResponse content = await _apiClient.GetFileTransferContentAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                result.TransferId,
                CancellationToken.None);
            await File.WriteAllBytesAsync(saveFileDialog.FileName, Convert.FromBase64String(content.ContentBase64));
            StatusTextBlock.Text = $"Download saved to {saveFileDialog.FileName}.";
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = $"Download failed: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private void PowerShellButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "PowerShell", out var client))
        {
            return;
        }

        try
        {
            _powerShellRemoteService.LaunchInteractivePowerShellSession(client);
            SetStatus($"Opened interactive PowerShell console for {client.DeviceName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Opening interactive PowerShell failed: {exception.Message}", isError: true);
        }
    }

    private void DashboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryResolveClientFromSenderOrSelection(sender, out var client))
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        OpenWorkspaceTab(
            client,
            $"dashboard:{client.ClientId}",
            $"Dashboard {client.DeviceName}",
            new DashboardTab(client, _powerShellRemoteService, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim()));
        SetStatus($"Opened dashboard for {client.DeviceName}.");
    }

    private void FileExplorerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "file explorer", out var client))
        {
            return;
        }

        OpenWorkspaceTab(client, $"files:{client.ClientId}", $"Files {client.DeviceName}", new FileExplorerTab(client, _powerShellRemoteService));
        SetStatus($"Opened remote file explorer tab for {client.DeviceName}.");
    }

    private void SoftwareButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "software center", out var client))
        {
            return;
        }

        OpenWorkspaceTab(client, $"software:{client.ClientId}", $"Software {client.DeviceName}", new SoftwareCenterTab(client, _powerShellRemoteService));
        SetStatus($"Opened software tab for {client.DeviceName}.");
    }

    private async void RemoteActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot run remote actions.", isError: true);
            return;
        }

        if (!TryResolveClientFromSenderOrSelection(sender, out var client))
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        try
        {
            ToggleBusy(true, $"Checking remote action readiness for {client.DeviceName}...");
            var precheck = await _powerShellRemoteService.CheckRepairReadinessAsync(client, CancellationToken.None);
            var scriptDirectory = _remoteActionScriptService.EnsureScriptDirectory(_remoteActionsPath);
            OpenWorkspaceTab(
                client,
                $"remote-actions:{client.ClientId}",
                $"Remote Actions {client.DeviceName}",
                new RemoteActionTab(
                    client,
                    _powerShellRemoteService,
                    _apiClient,
                    _serverUrl,
                    _apiKey,
                    MfaCodeTextBox.Text.Trim(),
                    _remoteActionScriptService,
                    scriptDirectory,
                    precheck));
            ToggleBusy(false, StatusTextBlock.Text);
            SetStatus($"Opened remote actions for {client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, StatusTextBlock.Text);
            SetStatus($"Remote action preparation failed: {exception.Message}", isError: true);
        }
    }

    private void ScreenshotButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "screenshot preview", out var client))
        {
            return;
        }

        OpenWorkspaceTab(
            client,
            $"screenshot:{client.ClientId}",
            $"Screenshot {client.DeviceName}",
            new ScreenshotPreviewTab(client, _powerShellRemoteService));
        SetStatus($"Opened screenshot preview for {client.DeviceName}.");
    }

    private async void UpdateClientButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ClientRow row)
        {
            ClientsDataGrid.SelectedItem = row;
        }

        if (!TryGetSelectedClientForWinRmTool(sender, "client update", out var client))
        {
            return;
        }

        var installerPath = ResolveInstallerPathForUpdate();
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            return;
        }

        if (!client.IsUpdateAvailable)
        {
            SetStatus($"No newer local client installer is available for {client.DeviceName}.", isError: true);
            return;
        }

        RepairPrecheckResult precheck;
        ClientInstallerConfigLoadResult configLoadResult;
        try
        {
            ToggleBusy(true, $"Preparing update for {client.DeviceName}...");
            precheck = await _powerShellRemoteService.CheckRepairReadinessAsync(client, CancellationToken.None);
            if (precheck.IsReachable && precheck.HasCredentials)
            {
                AddLogEntry("Info", $"[{client.DeviceName}] Loading current client.installer.config from client.");
                configLoadResult = await _powerShellRemoteService.LoadClientInstallerConfigAsync(client, CancellationToken.None);
            }
            else
            {
                configLoadResult = new ClientInstallerConfigLoadResult(
                    BuildDefaultInstallerConfigText(),
                    true,
                    "Current client.installer.config could not be loaded because WinRM or credentials are not ready. Review the template and fix connectivity before running the update.");
            }
        }
        catch (Exception exception)
        {
            ToggleBusy(false, StatusTextBlock.Text);
            SetStatus($"Update preparation failed: {exception.Message}", isError: true);
            return;
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }

        var dialog = new ClientUpdateWindow(
            installerPath,
            configLoadResult.ConfigText,
            configLoadResult.Message,
            precheck)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Updating {client.DeviceName}...");
            AddLogEntry("Info", $"Client update requested for {client.DeviceName}.");
            var updateResult = await _powerShellRemoteService.UpdateClientWithConfigAsync(
                client,
                installerPath,
                dialog.ConfigText,
                message => AddLogEntry("Info", $"[{client.DeviceName}] {message}"),
                CancellationToken.None);
            LogRemoteInstallerOutput(client, updateResult);
            if (updateResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Remote installer exited with code {updateResult.ExitCode}. See admin logs for stdout/stderr.");
            }
            SetStatus(
                $"Client update completed for {client.DeviceName}. PID {updateResult.ProcessId}, remote path {updateResult.RemoteInstallerPath}.");
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            SetStatus($"Client update failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private async void RepairClientButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ClientRow row)
        {
            ClientsDataGrid.SelectedItem = row;
        }

        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot repair clients.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        if (!CanRepairClient(client))
        {
            SetStatus("Repair requires a WinRM target plus remote username/password for this client.", isError: true);
            return;
        }

        var installerPath = ResolveInstallerPathForUpdate();
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            return;
        }

        ToggleBusy(true, $"Checking repair readiness for {client.DeviceName}...");
        RepairPrecheckResult precheck;
        try
        {
            precheck = await _powerShellRemoteService.CheckRepairReadinessAsync(client, CancellationToken.None);
        }
        catch (Exception exception)
        {
            ToggleBusy(false, StatusTextBlock.Text);
            SetStatus($"Repair precheck failed: {exception.Message}", isError: true);
            return;
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }

        var dialog = new RepairClientWindow(
            installerPath,
            BuildDefaultInstallerConfigText(),
            precheck)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Repairing {client.DeviceName}...");
            AddLogEntry("Info", $"Client repair requested for {client.DeviceName}.");
            var repairResult = await _powerShellRemoteService.RepairClientAsync(
                client,
                installerPath,
                dialog.ConfigText,
                message => AddLogEntry("Info", $"[{client.DeviceName}] {message}"),
                CancellationToken.None);
            LogRemoteInstallerOutput(client, repairResult);
            if (repairResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Remote repair installer exited with code {repairResult.ExitCode}. See admin logs for stdout/stderr.");
            }

            SetStatus($"Client repair completed for {client.DeviceName}. PID {repairResult.ProcessId}.");
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            SetStatus($"Client repair failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private void RegistryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "registry editor", out var client))
        {
            return;
        }

        OpenWorkspaceTab(
            client,
            $"registry:{client.ClientId}",
            $"Registry {client.DeviceName}",
            new RegistryEditorTab(client, _powerShellRemoteService, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim()));
        SetStatus($"Opened registry tab for {client.DeviceName}.");
    }

    private async void WingetUpdateAllMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "winget update", out var client))
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Run 'winget update --all' on {client.DeviceName} now?",
            "Winget Update All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Running winget update on {client.DeviceName}...");
            AddLogEntry("Info", $"winget update --all started on {client.DeviceName}.");
            var output = await _powerShellRemoteService.RunWingetUpdateAllAsync(client, CancellationToken.None);
            AddLogEntry("Info", $"winget update --all output for {client.DeviceName}: {output}");
            SetStatus($"winget update completed for {client.DeviceName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"winget update failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private async void RebootButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "reboot", out var client))
        {
            return;
        }

        if (MessageBox.Show(this, $"Restart {client.DeviceName} now?", "Restart Computer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Rebooting {client.DeviceName}...");
            await _powerShellRemoteService.RestartComputerAsync(client, CancellationToken.None);
            SetStatus($"Restart command sent to {client.DeviceName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Restart failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private async void ShutdownButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "shutdown", out var client))
        {
            return;
        }

        if (MessageBox.Show(this, $"Shut down {client.DeviceName} now?", "Shutdown Computer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Shutting down {client.DeviceName}...");
            await _powerShellRemoteService.ShutdownComputerAsync(client, CancellationToken.None);
            SetStatus($"Shutdown command sent to {client.DeviceName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Shutdown failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private void TaskManagerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "task manager", out var client))
        {
            return;
        }

        OpenWorkspaceTab(
            client,
            $"tasks:{client.ClientId}",
            $"Tasks {client.DeviceName}",
            new TaskManagerTab(client, _powerShellRemoteService, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim()));
        SetStatus($"Opened remote task manager tab for {client.DeviceName}.");
    }

    private void ServicesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot open the services tab.", isError: true);
            return;
        }

        if (!TryResolveClientFromSenderOrSelection(sender, out var client))
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        if (!client.IsOnline)
        {
            SetStatus("The client must be online for agent-based service management.", isError: true);
            return;
        }

        OpenWorkspaceTab(
            client,
            $"services:{client.ClientId}",
            $"Services {client.DeviceName}",
            new ServicesTab(client, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim()));
        SetStatus($"Opened services tab for {client.DeviceName}.");
    }

    private void PowerOptionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot open the power options tab.", isError: true);
            return;
        }

        if (!TryResolveClientFromSenderOrSelection(sender, out var client))
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        if (!client.IsOnline && !client.HasLaunchableActiveSession)
        {
            SetStatus("The client must be online or reachable via WinRM for power options.", isError: true);
            return;
        }

        OpenWorkspaceTab(
            client,
            $"power:{client.ClientId}",
            $"Power {client.DeviceName}",
            new PowerOptionsTab(client, _powerShellRemoteService, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim()));
        SetStatus($"Opened power options tab for {client.DeviceName}.");
    }

    private void WindowsUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClientForWinRmTool(sender, "Windows Updates", out var client))
        {
            return;
        }

        OpenWorkspaceTab(
            client,
            $"windows-updates:{client.ClientId}",
            $"Windows Updates {client.DeviceName}",
            new WindowsUpdatesTab(client, _powerShellRemoteService, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim()));
        SetStatus($"Opened Windows Updates tab for {client.DeviceName}.");
    }

    private void ChatButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot open chat.", isError: true);
            return;
        }

        if (!TryResolveClientFromSenderOrSelection(sender, out var client))
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        if (!client.IsOnline)
        {
            SetStatus("The client must be online for chat.", isError: true);
            return;
        }

        var chatTab = new ChatTab(client, _apiClient, _serverUrl, _apiKey, MfaCodeTextBox.Text.Trim());
        chatTab.MessagesViewed += (_, latestClientMessageAtUtc) => MarkClientChatAsViewed(client.ClientId, latestClientMessageAtUtc);
        OpenWorkspaceTab(client, $"chat:{client.ClientId}", $"Chat {client.DeviceName}", chatTab);
        SetStatus($"Opened chat tab for {client.DeviceName}.");
    }

    private void OpenClientWorkspaceMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryResolveClientFromSenderOrSelection(sender, out var client))
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        EnsureClientWorkspace(client);
        SetStatus($"Opened workspace for {client.DeviceName}.");
    }

    private bool TryGetSelectedClientForWinRmTool(object? sender, string toolName, out ClientRow client)
    {
        client = null!;
        if (!CurrentAdminCanManage())
        {
            SetStatus($"Current role is read-only and cannot open the {toolName}.", isError: true);
            return false;
        }

        if (!TryResolveClientFromSenderOrSelection(sender, out var selectedClient))
        {
            SetStatus("Select a client first.", isError: true);
            return false;
        }

        client = selectedClient;
        return true;
    }

    private bool TryResolveClientFromSenderOrSelection(object? sender, out ClientRow client)
    {
        client = null!;

        if (sender is FrameworkElement element)
        {
            if (element.DataContext is ClientRow row)
            {
                ClientsDataGrid.SelectedItem = row;
                client = row;
                return true;
            }

            var workspace = FindAncestor<ClientWorkspaceControl>(element);
            if (workspace is not null && TrySelectClientById(workspace.ClientId, out var workspaceClient))
            {
                client = workspaceClient;
                return true;
            }
        }

        if (ClientsDataGrid.SelectedItem is ClientRow selected)
        {
            client = selected;
            return true;
        }

        return false;
    }

    private bool TrySelectClientById(Guid clientId, out ClientRow client)
    {
        client = _clients.FirstOrDefault(item => item.ClientId == clientId)!;
        if (client is null)
        {
            return false;
        }

        ClientsDataGrid.SelectedItem = client;
        return true;
    }

    private void OpenWorkspaceTab(ClientRow client, string key, string header, FrameworkElement content)
    {
        var workspace = EnsureClientWorkspace(client);
        foreach (var item in workspace.WorkspaceTabs.Items.OfType<TabItem>())
        {
            if (string.Equals(item.Tag as string, key, StringComparison.Ordinal))
            {
                workspace.WorkspaceTabs.SelectedItem = item;
                MainNavigationTabControl.SelectedItem = FindClientWorkspaceTab(client.ClientId);
                return;
            }
        }

        var tab = new TabItem
        {
            Header = BuildClosableTabHeader(header, workspace.WorkspaceTabs),
            Content = content,
            Tag = key
        };
        workspace.WorkspaceTabs.Items.Add(tab);
        workspace.WorkspaceTabs.SelectedItem = tab;
        MainNavigationTabControl.SelectedItem = FindClientWorkspaceTab(client.ClientId);
    }

    private TabItem? FindClientWorkspaceTab(Guid clientId)
    {
        return MainNavigationTabControl.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, $"client:{clientId:N}", StringComparison.Ordinal));
    }

    private ClientWorkspaceControl EnsureClientWorkspace(ClientRow client)
    {
        var existingTab = FindClientWorkspaceTab(client.ClientId);
        if (existingTab?.Content is ClientWorkspaceControl existingWorkspace)
        {
            existingWorkspace.UpdateClient(client);
            UpdateWorkspaceActions(existingWorkspace, client);
            MainNavigationTabControl.SelectedItem = existingTab;
            return existingWorkspace;
        }

        var workspace = new ClientWorkspaceControl(client);
        workspace.ActionRequested += ClientWorkspace_ActionRequested;
        UpdateWorkspaceActions(workspace, client);

        var clientTab = new TabItem
        {
            Header = BuildClosableTabHeader(client.DeviceName, MainNavigationTabControl),
            Content = workspace,
            Tag = $"client:{client.ClientId:N}"
        };

        MainNavigationTabControl.Items.Add(clientTab);
        MainNavigationTabControl.SelectedItem = clientTab;
        return workspace;
    }

    private object BuildClosableTabHeader(string header, ItemsControl owner)
    {
        var panel = new DockPanel
        {
            LastChildFill = false,
            Tag = owner
        };

        var title = new TextBlock
        {
            Text = header,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Content = "x",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Focusable = false,
            Tag = header
        };
        closeButton.Click += WorkspaceTabCloseButton_OnClick;

        panel.Children.Add(title);
        panel.Children.Add(closeButton);
        return panel;
    }

    private void WorkspaceTabCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Parent: DockPanel panel })
        {
            return;
        }

        if (panel.Tag is not ItemsControl owner)
        {
            return;
        }

        var tab = owner.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Header, panel));
        if (tab is null)
        {
            return;
        }

        owner.Items.Remove(tab);
        SetStatus($"Closed tab '{(sender as Button)?.Tag ?? "Workspace"}'.");
    }

    private void ConnectToSelectedClient(RemoteChannel channel)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot launch remote sessions.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        try
        {
            EnsureClientWorkspace(client);
            if (channel == RemoteChannel.WinRm)
            {
                _powerShellRemoteService.LaunchInteractivePowerShellSession(client);
                SetStatus($"Opened interactive WinRM PowerShell for {client.DeviceName}.");
                return;
            }

            var launchCheck = _remoteLauncher.Launch(client, channel);
            SetStatus(launchCheck.Warnings.Count == 0
                ? $"Launched {channel} for {client.DeviceName}."
                : $"Launched {channel} for {client.DeviceName}. {string.Join(" ", launchCheck.Warnings)}");
        }
        catch (Exception exception)
        {
            SetStatus($"Connect failed: {exception.Message}", isError: true);
        }
    }

    private void ClientWorkspace_ActionRequested(object? sender, string action)
    {
        if (sender is not ClientWorkspaceControl workspace || !TrySelectClientById(workspace.ClientId, out _))
        {
            return;
        }

        switch (action)
        {
            case "request-support":
                _ = RequestSupportForSelectedClientAsync();
                break;
            case "connect":
                ConnectButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "rdp":
                RdpConnectButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "rustdesk":
                RustDeskConnectButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "ps-console":
                PowerShellButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "dashboard":
                DashboardButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "files":
                FileExplorerButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "tasks":
                TaskManagerButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "services":
                ServicesButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "software":
                SoftwareButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "registry":
                RegistryButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "power":
                PowerOptionsButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "windows-updates":
                WindowsUpdatesButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "chat":
                ChatButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "remote-action":
                RemoteActionButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "screenshot":
                ScreenshotButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "reboot":
                RebootButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "shutdown":
                ShutdownButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "end-session":
                EndSessionButton_OnClick(workspace, new RoutedEventArgs());
                break;
            case "edit-client":
                EditClientButton_OnClick(workspace, new RoutedEventArgs());
                break;
        }
    }

    private void UpdateSelectionActions()
    {
        foreach (var tab in MainNavigationTabControl.Items.OfType<TabItem>())
        {
            if (tab.Content is not ClientWorkspaceControl workspace)
            {
                continue;
            }

            var client = _clients.FirstOrDefault(item => item.ClientId == workspace.ClientId);
            if (client is null)
            {
                continue;
            }

            workspace.UpdateClient(client);
            UpdateWorkspaceActions(workspace, client);
        }
    }

    private void UpdateWorkspaceActions(ClientWorkspaceControl workspace, ClientRow client)
    {
        bool canManage = CurrentAdminCanManage();
        bool canUseRemotePowerShellTools = canManage
            && client.IsOnline
            && ((client.ActiveChannel is RemoteChannel.WinRm && client.HasLaunchableActiveSession)
                || client.IsDirectAdminAccessAvailable);
        bool canUseAgentTools = canManage && client.IsOnline;
        bool canLaunchRdp = (client.HasLaunchableActiveSession || client.IsDirectAdminAccessAvailable) && (client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(RemoteChannel.Rdp));
        bool canLaunchWinRm = (client.HasLaunchableActiveSession || client.IsDirectAdminAccessAvailable) && (client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(RemoteChannel.WinRm));
        bool canLaunchRustDesk = !string.IsNullOrWhiteSpace(client.RustDeskId) || client.TailscaleIpAddresses.Count > 0;
        bool selectedChannelLaunchable = canManage && (canLaunchRustDesk || (client.IsOnline && (canLaunchRdp || canLaunchWinRm)));
        bool canRemoteAction = canManage && CanRepairClient(client);

        workspace.SetActionEnabled("request-support", canManage);
        workspace.SetActionEnabled("connect", selectedChannelLaunchable);
        workspace.SetActionEnabled("rdp", canManage && client.IsOnline && canLaunchRdp);
        workspace.SetActionEnabled("rustdesk", canManage && canLaunchRustDesk);
        workspace.SetActionEnabled("end-session", canManage && client.HasActiveSession);
        workspace.SetActionEnabled("ps-console", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("dashboard", canManage && client.IsOnline);
        workspace.SetActionEnabled("files", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("tasks", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("services", canUseAgentTools);
        workspace.SetActionEnabled("software", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("registry", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("power", canManage && (client.IsOnline || canUseRemotePowerShellTools));
        workspace.SetActionEnabled("windows-updates", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("chat", canManage && client.IsOnline);
        workspace.SetActionEnabled("remote-action", canRemoteAction);
        workspace.SetActionEnabled("screenshot", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("reboot", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("shutdown", canUseRemotePowerShellTools);
        workspace.SetActionEnabled("edit-client", canManage);
    }

    private void UpdateOpenClientWorkspaces()
    {
        foreach (var tab in MainNavigationTabControl.Items.OfType<TabItem>().ToArray())
        {
            if (tab.Content is not ClientWorkspaceControl workspace)
            {
                continue;
            }

            var client = _clients.FirstOrDefault(item => item.ClientId == workspace.ClientId);
            if (client is null)
            {
                MainNavigationTabControl.Items.Remove(tab);
                continue;
            }

            workspace.UpdateClient(client);
            UpdateWorkspaceActions(workspace, client);
        }
    }

    private void ApplyChatViewState(ClientRow row)
    {
        if (_lastViewedClientChatAt.TryGetValue(row.ClientId, out var lastViewedAtUtc) &&
            row.LastClientChatMessageAtUtc is not null &&
            lastViewedAtUtc is not null &&
            row.LastClientChatMessageAtUtc <= lastViewedAtUtc)
        {
            row.ChatViewedInSession = true;
            return;
        }

        row.ChatViewedInSession = false;
    }

    private void MarkClientChatAsViewed(Guid clientId, DateTimeOffset? latestClientMessageAtUtc)
    {
        _lastViewedClientChatAt[clientId] = latestClientMessageAtUtc ?? DateTimeOffset.UtcNow;
        var row = _clients.FirstOrDefault(item => item.ClientId == clientId);
        if (row is null)
        {
            return;
        }

        ApplyChatViewState(row);
    }

    private void HandleClientChatTransition(ClientRow row)
    {
        if (!_knownLastClientChatAt.TryGetValue(row.ClientId, out var previousLastClientChatAtUtc))
        {
            _knownLastClientChatAt[row.ClientId] = row.LastClientChatMessageAtUtc;
            return;
        }

        _knownLastClientChatAt[row.ClientId] = row.LastClientChatMessageAtUtc;
        if (row.LastClientChatMessageAtUtc is null)
        {
            return;
        }

        if (previousLastClientChatAtUtc is not null && row.LastClientChatMessageAtUtc <= previousLastClientChatAtUtc)
        {
            return;
        }

        if (!row.HasUnreadClientChat)
        {
            return;
        }

        ShowToast(row.ClientId, "New Client Message", $"{row.DeviceName} sent a new chat message.");
        AddLogEntry("Info", $"New chat reply from {row.DeviceName}.");
    }

    private void HandleClientPresenceTransition(ClientRow client, bool previousStateKnown, bool previousIsOnline)
    {
        if (!previousStateKnown)
        {
            _knownClientOnlineStates[client.ClientId] = client.IsOnline;
            return;
        }

        if (previousIsOnline == client.IsOnline)
        {
            _knownClientOnlineStates[client.ClientId] = client.IsOnline;
            return;
        }

        _knownClientOnlineStates[client.ClientId] = client.IsOnline;
        AddLogEntry("Info", $"Client {client.DeviceName} is now {(client.IsOnline ? "Online" : "Offline")}.");
        StatusTextBlock.Text = $"Client {client.DeviceName} is now {(client.IsOnline ? "Online" : "Offline")}.";
    }

    private static T? FindAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void RestoreClientSelection(Guid? selectedClientId)
    {
        if (selectedClientId is null)
        {
            return;
        }

        var selectedClient = _clients.FirstOrDefault(client => client.ClientId == selectedClientId.Value);
        if (selectedClient is null)
        {
            return;
        }

        ClientsDataGrid.SelectedItem = selectedClient;
        ClientsDataGrid.ScrollIntoView(selectedClient);
    }

    private void ToggleBusy(bool isBusy, string statusText)
    {
        RefreshButton.IsEnabled = !isBusy;

        if (!isBusy)
        {
            UpdateSelectionActions();
        }

        SetStatus(statusText, log: false);
    }

    private void ShowToast(Guid clientId, string title, string message)
    {
        var notification = new AdminToastNotification
        {
            ClientId = clientId,
            Title = title,
            Message = message
        };
        _toastNotifications.Insert(0, notification);
        _ = DismissToastAsync(notification);
    }

    private async Task DismissToastAsync(AdminToastNotification notification)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        await Dispatcher.InvokeAsync(() => _toastNotifications.Remove(notification));
    }

    private void ToastNotificationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AdminToastNotification notification })
        {
            return;
        }

        _toastNotifications.Remove(notification);
        if (!TrySelectClientById(notification.ClientId, out var client))
        {
            SetStatus("The client for this notification is no longer available.", isError: true);
            return;
        }

        ChatButton_OnClick(sender, new RoutedEventArgs());
        SetStatus($"Opened chat for {client.DeviceName} from notification.");
    }

    private async Task<FileTransferDto> WaitForTransferCompletionAsync(Guid transferId, CancellationToken cancellationToken)
    {
        DateTimeOffset timeoutAt = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            FileTransferDto transfer = await _apiClient.GetFileTransferAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                transferId,
                cancellationToken);
            if (transfer.Status is "Completed" or "Failed")
            {
                return transfer;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for the file transfer to finish.");
    }

    private bool CurrentAdminCanManage() => _adminSession?.Roles.Any(role => role is "Operator" or "Administrator") == true;

    private void UpdateRefreshInterval(IReadOnlyList<AdminClientSummary> clients)
    {
        bool needsFastPolling = clients.Any(client => client.PendingSupportRequest is not null || client.ActiveSession is not null);
        _refreshTimer.Interval = needsFastPolling ? FastRefreshInterval : NormalRefreshInterval;
    }

    private void UpdateServerStatusIndicator(bool isOnline)
    {
        if (isOnline)
        {
            ServerStatusMenuItem.Header = "Server: Online";
            ServerStatusMenuItem.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
            return;
        }

        ServerStatusMenuItem.Header = "Server: Offline";
        ServerStatusMenuItem.Foreground = new SolidColorBrush(Color.FromRgb(153, 27, 27));
    }

    private void LoadPersistedSettings()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = _settingsStore.Load();
            _serverUrl = settings.ServerUrl;
            _apiKey = settings.ApiKey;
            _serverProjectPath = settings.ServerProjectPath;
            _rustDeskPath = settings.RustDeskPath;
            _rustDeskPassword = settings.RustDeskPassword;
            _clientInstallerPath = settings.ClientInstallerPath;
            _remoteActionsPath = _remoteActionScriptService.EnsureScriptDirectory(settings.RemoteActionsPath);
            _packageGeneratorPath = string.IsNullOrWhiteSpace(settings.PackageGeneratorPath)
                ? AdminSettingsStore.ResolveDefaultPackageGeneratorPath()
                : settings.PackageGeneratorPath;
            _localClientPackageVersion = ResolveClientPackageVersion(_clientInstallerPath);
            _remoteUserName = settings.RemoteUserName;
            _remotePassword = settings.RemotePassword;
            _themeMode = settings.ThemeMode;
            _remoteLauncher = new RemoteLauncher(_rustDeskPath, _rustDeskPassword);
            _powerShellRemoteService.UpdateDefaultCredentials(_remoteUserName, _remotePassword);
            App.ApplyTheme(_themeMode);
            UpdateThemeMenuState();
            ReasonTextBox.Text = settings.Reason;
            UpdateStartServerButtonState();
            UpdateMfaVisibility(false);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void WirePersistenceEvents()
    {
        ReasonTextBox.TextChanged += (_, _) => PersistSettings();
    }

    private void UpdateStartServerButtonState()
    {
        StartServerMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(_serverProjectPath);
        StartServerMenuItem.ToolTip = string.IsNullOrWhiteSpace(_serverProjectPath)
            ? "Open Application > Settings and configure the server project path first."
            : _serverProjectPath;
    }

    private void ApplyTheme(AdminThemeMode themeMode)
    {
        _themeMode = themeMode;
        App.ApplyTheme(_themeMode);
        UpdateThemeMenuState();
        PersistSettings();
        SetStatus($"Theme switched to {_themeMode}.", log: false);
    }

    private void UpdateThemeMenuState()
    {
        if (LightThemeMenuItem is null || DarkThemeMenuItem is null)
        {
            return;
        }

        LightThemeMenuItem.IsChecked = _themeMode == AdminThemeMode.Light;
        DarkThemeMenuItem.IsChecked = _themeMode == AdminThemeMode.Dark;
    }

    private void PersistSettings()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _settingsStore.Save(new AdminClientSettings
        {
            ServerUrl = _serverUrl,
            ApiKey = _apiKey,
            ServerProjectPath = _serverProjectPath,
            RustDeskPath = _rustDeskPath,
            RustDeskPassword = _rustDeskPassword,
            ClientInstallerPath = _clientInstallerPath,
            RemoteActionsPath = _remoteActionsPath,
            PackageGeneratorPath = _packageGeneratorPath,
            RemoteUserName = _remoteUserName,
            RemotePassword = _remotePassword,
            ThemeMode = _themeMode,
            PreferredChannel = RemoteChannel.Rdp,
            Reason = ReasonTextBox.Text.Trim()
        });
    }

    private async void DeleteClientButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot delete clients.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Delete client '{client.DeviceName}' from the server list? The client can register again automatically later.",
            "Delete Client",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Deleting client {client.DeviceName}...");
            await _apiClient.DeleteClientAsync(_serverUrl, _apiKey, MfaCodeTextBox.Text.Trim(), client.ClientId, CancellationToken.None);
            SetStatus($"Client {client.DeviceName} deleted.");
            await RefreshClientsAsync(refreshAudit: true);
        }
        catch (Exception exception)
        {
            SetStatus($"Delete client failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private async void EditClientButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CurrentAdminCanManage())
        {
            SetStatus("Current role is read-only and cannot edit clients.", isError: true);
            return;
        }

        if (ClientsDataGrid.SelectedItem is not ClientRow client)
        {
            SetStatus("Select a client first.", isError: true);
            return;
        }

        var window = new ClientSettingsWindow(client.RustDeskId, client.RustDeskPassword, client.RemoteUserName, client.RemotePassword, client.Notes)
        {
            Owner = this
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ToggleBusy(true, $"Updating settings for {client.DeviceName}...");
            await _apiClient.UpdateClientMetadataAsync(
                _serverUrl,
                _apiKey,
                MfaCodeTextBox.Text.Trim(),
                client.ClientId,
                new UpdateAdminClientMetadataRequest(window.Notes, window.RustDeskId, window.RustDeskPassword, window.RemoteUserName, window.RemotePassword),
                CancellationToken.None);
            await RefreshClientsAsync(refreshAudit: true);
            SetStatus($"Updated settings for {client.DeviceName}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Update client settings failed: {exception.Message}", isError: true);
        }
        finally
        {
            ToggleBusy(false, StatusTextBlock.Text);
        }
    }

    private RemoteChannel DeterminePreferredConnectChannel(ClientRow client)
    {
        if (client.IsDirectAdminAccessAvailable &&
            (client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(RemoteChannel.Rdp)))
        {
            return RemoteChannel.Rdp;
        }

        if ((!string.IsNullOrWhiteSpace(client.RustDeskId) || client.TailscaleIpAddresses.Count > 0) &&
            (client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(RemoteChannel.RustDesk)))
        {
            return RemoteChannel.RustDesk;
        }

        if (client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(RemoteChannel.Rdp))
        {
            return RemoteChannel.Rdp;
        }

        return RemoteChannel.WinRm;
    }

    private RemoteChannel DetermineSupportRequestChannel(ClientRow client)
    {
        if (client.SupportedChannels.Contains(RemoteChannel.WinRm))
        {
            return RemoteChannel.WinRm;
        }

        if (client.SupportedChannels.Contains(RemoteChannel.RustDesk))
        {
            return RemoteChannel.RustDesk;
        }

        return client.SupportedChannels.Count == 0
            ? RemoteChannel.WinRm
            : client.SupportedChannels[0];
    }

    private void UpdateMfaVisibility(bool isVisible)
    {
        MfaPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!isVisible)
        {
            MfaCodeTextBox.Text = string.Empty;
        }
    }

    private static bool ShouldPromptForMfa(string message)
    {
        return message.Contains("MFA", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TOTP", StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatus(string message, bool isError = false, bool log = true)
    {
        StatusTextBlock.Text = message;
        if (log)
        {
            AddLogEntry(isError ? "Error" : "Info", message);
        }
    }

    private void LogInfo(string message)
    {
        AddLogEntry("Info", message);
    }

    private void AddLogEntry(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _logEntries.Insert(0, new AdminLogEntry
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Level = level,
            Message = message
        });

        while (_logEntries.Count > 250)
        {
            _logEntries.RemoveAt(_logEntries.Count - 1);
        }
    }

    private void LogRemoteInstallerOutput(ClientRow client, RemoteInstallerLaunchResult updateResult)
    {
        AddLogEntry("Info", $"[{client.DeviceName}] Remote installer stdout: {updateResult.StdOutPath}");
        AddLogEntry("Info", $"[{client.DeviceName}] Remote installer stderr: {updateResult.StdErrPath}");

        foreach (var line in SplitLogLines(updateResult.StandardOutput))
        {
            AddLogEntry("Info", $"[{client.DeviceName}] installer> {line}");
        }

        foreach (var line in SplitLogLines(updateResult.StandardError))
        {
            AddLogEntry("Error", $"[{client.DeviceName}] installer! {line}");
        }
    }

    private static IEnumerable<string> SplitLogLines(string? text)
    {
        return (text ?? string.Empty)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line));
    }

    private bool CanRepairClient(ClientRow client)
    {
        var effectiveUserName = !string.IsNullOrWhiteSpace(client.RemoteUserName)
            ? client.RemoteUserName
            : _remoteUserName;
        var effectivePassword = !string.IsNullOrWhiteSpace(client.RemotePassword)
            ? client.RemotePassword
            : _remotePassword;

        return !string.IsNullOrWhiteSpace(effectiveUserName)
            && !string.IsNullOrWhiteSpace(effectivePassword)
            && (!string.IsNullOrWhiteSpace(client.MachineName) || client.TailscaleIpAddresses.Count > 0);
    }

    private string BuildDefaultInstallerConfigText()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "client.installer.config.sample");
        if (!File.Exists(samplePath))
        {
            samplePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "client.installer.config.sample"));
        }

        if (File.Exists(samplePath))
        {
            return File.ReadAllText(samplePath);
        }

        return $$"""
{
  "serverUrl": "{{_serverUrl}}",
  "installRoot": "C:\\Program Files\\StevensSupportHelper",
  "serviceName": "StevensSupportHelperClientService",
  "deviceName": "",
  "registrationSharedKey": "",
  "installRustDesk": false,
  "installTailscale": false,
  "enableAutoApprove": false,
  "enableRdp": false,
  "createServiceUser": false,
  "serviceUserIsAdministrator": true,
  "silent": true
}
""";
    }

    private string? ResolveInstallerPathForUpdate()
    {
        if (!string.IsNullOrWhiteSpace(_clientInstallerPath) && File.Exists(_clientInstallerPath))
        {
            return _clientInstallerPath;
        }

        var openFileDialog = new OpenFileDialog
        {
            Filter = "Installer EXE (*.exe)|*.exe",
            Title = "Select StevensSupportHelper client installer"
        };

        if (openFileDialog.ShowDialog(this) != true)
        {
            return null;
        }

        _clientInstallerPath = openFileDialog.FileName;
        _localClientPackageVersion = ResolveClientPackageVersion(_clientInstallerPath);
        PersistSettings();
        return _clientInstallerPath;
    }

    private void ApplyAvailableUpdateInfo(ClientRow row)
    {
        row.AvailableUpdateVersion = _localClientPackageVersion;
        row.IsUpdateAvailable = !string.IsNullOrWhiteSpace(_localClientPackageVersion)
            && IsVersionNewer(_localClientPackageVersion, row.AgentVersion);
    }

    private static string ResolveClientPackageVersion(string? installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return string.Empty;
        }

        var versionInfo = FileVersionInfo.GetVersionInfo(installerPath);
        var candidates = new[]
        {
            versionInfo.ProductVersion,
            versionInfo.FileVersion,
            Path.GetFileNameWithoutExtension(installerPath)
        };

        foreach (var candidate in candidates)
        {
            var normalized = TryExtractVersion(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static bool IsVersionNewer(string availableVersion, string installedVersion)
    {
        return ParseVersionCore(availableVersion) > ParseVersionCore(installedVersion);
    }

    private static string TryExtractVersion(string? value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value ?? string.Empty, @"\d+(\.\d+){0,3}");
        return match.Success ? match.Value : string.Empty;
    }

    private static Version ParseVersionCore(string value)
    {
        var extracted = TryExtractVersion(value);
        return Version.TryParse(extracted, out var version)
            ? version
            : new Version(0, 0, 0, 0);
    }

    private static string GetAdminVersionText()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(processPath).FileVersion;
            var normalized = TryExtractVersion(fileVersion);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        return TryExtractVersion(version);
    }

}
