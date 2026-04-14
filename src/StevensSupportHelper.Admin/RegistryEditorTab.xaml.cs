using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class RegistryEditorTab : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RootPaths =
    [
        "Registry::HKEY_LOCAL_MACHINE",
        "Registry::HKEY_CURRENT_USER",
        "Registry::HKEY_USERS",
        "Registry::HKEY_CLASSES_ROOT",
        "Registry::HKEY_CURRENT_CONFIG"
    ];

    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly ObservableCollection<RegistryTreeNode> _rootNodes = [];
    private readonly ObservableCollection<RemoteRegistryEntry> _values = [];
    private bool _isBusy;
    private bool _suppressTreeSelection;

    public RegistryEditorTab(
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
        RegistryTreeView.ItemsSource = _rootNodes;
        ValuesDataGrid.ItemsSource = _values;
        BuildRootNodes();
        Loaded += async (_, _) => await LoadRegistryPathAsync(RegistryPathTextBox.Text);
    }

    private void BuildRootNodes()
    {
        _rootNodes.Clear();
        foreach (var rootPath in RootPaths)
        {
            var node = new RegistryTreeNode(rootPath.Replace("Registry::", string.Empty, StringComparison.Ordinal), rootPath);
            node.EnsurePlaceholder();
            _rootNodes.Add(node);
        }
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await LoadRegistryPathAsync(RegistryPathTextBox.Text.Trim());

    private async void UpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentPath = RegistryPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        var separatorIndex = currentPath.LastIndexOf('\\');
        if (separatorIndex > "Registry::HKEY_LOCAL_MACHINE".Length)
        {
            await LoadRegistryPathAsync(currentPath[..separatorIndex], selectInTree: true);
        }
    }

    private async void RegistryTreeItem_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem { DataContext: RegistryTreeNode node })
        {
            return;
        }

        if (!node.IsLoaded)
        {
            await LoadChildrenAsync(node, CancellationToken.None);
        }
    }

    private async void RegistryTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeSelection || e.NewValue is not RegistryTreeNode node)
        {
            return;
        }

        await LoadRegistryPathAsync(node.FullPath, selectInTree: false);
    }

    private void ValuesDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionActions();

    private async void NewStringButton_OnClick(object sender, RoutedEventArgs e)
    {
        string? name = PromptDialog.Show(Window.GetWindow(this)!, "Registry Value Name", "Name of the string value:", "NewValue");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string? value = PromptDialog.Show(Window.GetWindow(this)!, "Registry Value Data", "String value data:", string.Empty) ?? string.Empty;

        try
        {
            ToggleBusy(true, "Writing registry value...");
            await _remoteService.SetRegistryStringValueAsync(_client, RegistryPathTextBox.Text.Trim(), name.Trim(), value, CancellationToken.None);
            await LoadRegistryPathAsync(RegistryPathTextBox.Text.Trim(), selectInTree: true);
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Registry write failed: {exception.Message}");
        }
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ValuesDataGrid.SelectedItem is not RemoteRegistryEntry entry)
        {
            StatusTextBlock.Text = "Select a registry value first.";
            return;
        }

        var result = MessageBox.Show(Window.GetWindow(this), $"Delete registry value '{entry.FullName}'?", "Delete Value", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ToggleBusy(true, "Deleting registry value...");
            await _remoteService.DeleteRegistryValueAsync(_client, RegistryPathTextBox.Text.Trim(), entry.Name, CancellationToken.None);
            await LoadRegistryPathAsync(RegistryPathTextBox.Text.Trim(), selectInTree: true);
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Delete failed: {exception.Message}");
        }
    }

    private async Task LoadRegistryPathAsync(string registryPath, bool selectInTree = true)
    {
        try
        {
            ToggleBusy(true, $"Loading registry path {registryPath}...");
            (IReadOnlyList<string> subKeys, IReadOnlyList<RemoteRegistryEntry> values) = await TryLoadViaAgentAsync(registryPath, CancellationToken.None)
                ?? await LoadViaPowerShellAsync(registryPath, CancellationToken.None);

            _values.Clear();
            foreach (var value in values)
            {
                _values.Add(value);
            }

            RegistryPathTextBox.Text = registryPath;
            if (selectInTree)
            {
                await EnsureTreeSelectionAsync(registryPath, subKeys, CancellationToken.None);
            }

            ToggleBusy(false, $"Loaded {subKeys.Count} subkeys and {_values.Count} values.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Registry refresh failed: {exception.Message}");
        }
    }

    private async Task EnsureTreeSelectionAsync(string registryPath, IReadOnlyList<string>? knownSubKeys, CancellationToken cancellationToken)
    {
        _suppressTreeSelection = true;
        try
        {
            var segments = registryPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return;
            }

            var rootPath = segments[0];
            var currentNode = _rootNodes.FirstOrDefault(node => string.Equals(node.FullPath, rootPath, StringComparison.OrdinalIgnoreCase));
            if (currentNode is null)
            {
                return;
            }

            currentNode.IsExpanded = true;
            if (!currentNode.IsLoaded)
            {
                await LoadChildrenAsync(currentNode, cancellationToken);
            }

            for (var index = 1; index < segments.Length; index++)
            {
                var childName = segments[index];
                var nextNode = currentNode.Children.FirstOrDefault(node => string.Equals(node.Name, childName, StringComparison.OrdinalIgnoreCase));
                if (nextNode is null)
                {
                    break;
                }

                currentNode = nextNode;
                currentNode.IsExpanded = true;
                if (!currentNode.IsLoaded)
                {
                    await LoadChildrenAsync(currentNode, cancellationToken);
                }
            }

            currentNode.IsSelected = true;

            if (knownSubKeys is not null && currentNode.IsLoaded)
            {
                SyncChildren(currentNode, knownSubKeys);
            }
        }
        finally
        {
            _suppressTreeSelection = false;
        }
    }

    private async Task LoadChildrenAsync(RegistryTreeNode node, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> subKeys = (await TryLoadViaAgentAsync(node.FullPath, cancellationToken)
            ?? await LoadViaPowerShellAsync(node.FullPath, cancellationToken)).SubKeys;
        SyncChildren(node, subKeys);
        node.IsLoaded = true;
    }

    private static void SyncChildren(RegistryTreeNode node, IReadOnlyList<string> subKeys)
    {
        node.Children.Clear();
        foreach (var subKey in subKeys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            var child = new RegistryTreeNode(subKey, $"{node.FullPath}\\{subKey}");
            child.EnsurePlaceholder();
            node.Children.Add(child);
        }

        node.IsLoaded = true;
    }

    private async Task<(IReadOnlyList<string> SubKeys, IReadOnlyList<RemoteRegistryEntry> Values)?> TryLoadViaAgentAsync(string registryPath, CancellationToken cancellationToken)
    {
        if (!_client.IsOnline)
        {
            return null;
        }

        try
        {
            var queued = await _apiClient.QueueRegistrySnapshotJobAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, registryPath, cancellationToken);
            var job = await WaitForAgentJobCompletionAsync(queued.JobId, cancellationToken);
            if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(job.ErrorMessage ?? $"Agent job finished with status {job.Status}.");
            }

            var result = JsonSerializer.Deserialize<AgentRegistrySnapshotResult>(job.ResultJson ?? string.Empty, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned no registry snapshot payload.");
            return (
                result.SubKeys,
                result.Values.Select(static value => new RemoteRegistryEntry
                {
                    Name = value.Name,
                    Kind = value.Kind,
                    Value = value.Value
                }).ToArray());
        }
        catch
        {
            return null;
        }
    }

    private async Task<(IReadOnlyList<string> SubKeys, IReadOnlyList<RemoteRegistryEntry> Values)> LoadViaPowerShellAsync(string registryPath, CancellationToken cancellationToken)
    {
        var subKeys = await _remoteService.ListRegistrySubKeysAsync(_client, registryPath, cancellationToken);
        var values = await _remoteService.ListRegistryValuesAsync(_client, registryPath, cancellationToken);
        return (subKeys, values);
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

        throw new TimeoutException("Timed out waiting for the agent registry snapshot.");
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RegistryPathTextBox.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        UpButton.IsEnabled = !isBusy;
        NewStringButton.IsEnabled = !isBusy;
        RegistryTreeView.IsEnabled = !isBusy;
        ValuesDataGrid.IsEnabled = !isBusy;
        UpdateSelectionActions();
        StatusTextBlock.Text = status;
    }

    private void UpdateSelectionActions()
    {
        DeleteButton.IsEnabled = !_isBusy && ValuesDataGrid.SelectedItem is RemoteRegistryEntry;
    }
}
