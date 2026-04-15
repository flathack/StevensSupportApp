using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.AdminWeb.Pages.Clients;

public sealed class DetailsModel : PageModel
{
    private readonly ApiClient _apiClient;
    private readonly DemoClientDataService _demoClientDataService;

    public DetailsModel(ApiClient apiClient, DemoClientDataService demoClientDataService)
    {
        _apiClient = apiClient;
        _demoClientDataService = demoClientDataService;
    }

    [BindProperty]
    public Guid ClientId { get; set; }

    [BindProperty]
    public string SupportReason { get; set; } = "Fernwartung angefordert.";

    [BindProperty]
    public string PreferredChannel { get; set; } = "RustDesk";

    [BindProperty]
    public string RegistryPath { get; set; } = @"HKLM\Software";

    [BindProperty]
    public string ServiceName { get; set; } = "Spooler";

    [BindProperty]
    public string ServiceAction { get; set; } = "restart";

    [BindProperty]
    public string PowerPlanGuid { get; set; } = "381b4222-f694-41f0-9685-ff5bb260df2e";

    [BindProperty]
    public string ScriptContent { get; set; } = "Get-ComputerInfo | Select-Object CsName, WindowsVersion, OsArchitecture";

    [BindProperty]
    public string ChatMessage { get; set; } = string.Empty;

    [BindProperty]
    public string Notes { get; set; } = string.Empty;

    [BindProperty]
    public string RustDeskId { get; set; } = string.Empty;

    [BindProperty]
    public string RustDeskPassword { get; set; } = string.Empty;

    [BindProperty]
    public string RemoteUserName { get; set; } = string.Empty;

    [BindProperty]
    public string RemotePassword { get; set; } = string.Empty;

    [BindProperty]
    public string UploadTargetPath { get; set; } = @"AdminDrop\tools";

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty]
    public string DownloadSourcePath { get; set; } = @"C:\ProgramData\StevensSupportHelper\Logs\client.log";

    [BindProperty]
    public Guid? LatestTransferId { get; set; }

    [BindProperty]
    public Guid? LatestJobId { get; set; }

    [BindProperty]
    public string OpenTabsState { get; set; } = "overview";

    [BindProperty]
    public string ActiveTabKey { get; set; } = "overview";

    public ClientDetailResponse? Client { get; set; }
    public List<RemoteActionResponse> RemoteActions { get; set; } = [];
    public List<ChatMessageDto> ChatMessages { get; set; } = [];
    public FileTransferDto? LatestTransfer { get; set; }
    public FileTransferContentResponse? LatestTransferContent { get; set; }
    public AgentJobDto? LatestAgentJob { get; set; }
    public string? ActionMessage { get; set; }
    public bool IsActionError { get; set; }
    public bool IsUsingDemoClient { get; set; }
    public string RdpTargetHost => Client?.TailscaleIpAddresses.FirstOrDefault()
        ?? Client?.MachineName
        ?? string.Empty;

    public IReadOnlyList<ClientToolDefinition> ToolDefinitions { get; } =
    [
        new("support", "Support", "Support anfordern"),
        new("connect", "Verbinden", "Verbindungsdaten anzeigen"),
        new("rdp", "RDP", "RDP-Datei erzeugen"),
        new("rustdesk", "RustDesk", "RustDesk-Zugang"),
        new("ps-console", "PS-Konsole", "PowerShell-Job anstoßen"),
        new("dashboard", "Dashboard", "Geräteübersicht"),
        new("files", "Dateien", "Datei-Transfer steuern"),
        new("tasks", "Aufgaben", "Agent-Jobs verfolgen"),
        new("services", "Dienste", "Services prüfen und steuern"),
        new("registry", "Registry", "Registry-Snapshot anfordern"),
        new("power", "Energie", "Power-Pläne prüfen"),
        new("windows-updates", "Windows-Updates", "Update-Jobs steuern"),
        new("chat", "Chat", "Mit dem Client schreiben"),
        new("remote-actions", "Remote Actions", "Skriptkatalog ansehen"),
        new("edit-client", "Client bearbeiten", "Metadaten pflegen"),
        new("end-session", "Sitzung beenden", "Aktive Sitzung schließen")
    ];

    public async Task<IActionResult> OnGetAsync(Guid id, string? tabs = null, string? active = null, Guid? transferId = null, Guid? jobId = null)
    {
        ClientId = id;
        LatestTransferId = transferId;
        LatestJobId = jobId;
        OpenTabsState = string.IsNullOrWhiteSpace(tabs) ? "overview" : tabs;
        ActiveTabKey = string.IsNullOrWhiteSpace(active) ? "overview" : active;

        NormalizeWorkspaceState("overview");
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostSupportAsync()
    {
        if (!TryPrepareAuthenticatedRequest("support", out var displayName, out var redirect))
        {
            return redirect!;
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = $"Demo-Modus: Support-Anfrage für '{ClientId}' wurde simuliert.";
            return await LoadClientPageAsync();
        }

        var result = await _apiClient.CreateSupportRequestAsync(ClientId, displayName!, PreferredChannel, SupportReason);
        if (result is null)
        {
            return await FailAndReloadAsync("Support-Anfrage konnte nicht erstellt werden.");
        }

        ActionMessage = $"Support-Anfrage erstellt: {result.Message}";
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostEndSessionAsync()
    {
        if (!TryPrepareAuthenticatedRequest("end-session", out _, out var redirect))
        {
            return redirect!;
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = "Demo-Modus: Sitzung wurde simuliert beendet.";
            return await LoadClientPageAsync();
        }

        var result = await _apiClient.EndSessionAsync(ClientId);
        if (result is null)
        {
            return await FailAndReloadAsync("Sitzung konnte nicht beendet werden.");
        }

        ActionMessage = $"Sitzung beendet: {result.Message}";
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostSendChatAsync()
    {
        if (!TryPrepareAuthenticatedRequest("chat", out _, out var redirect))
        {
            return redirect!;
        }

        if (string.IsNullOrWhiteSpace(ChatMessage))
        {
            return await FailAndReloadAsync("Bitte gib zuerst eine Nachricht ein.");
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = "Demo-Modus: Chat-Nachricht wurde simuliert gesendet.";
            ChatMessage = string.Empty;
            return await LoadClientPageAsync();
        }

        var result = await _apiClient.SendChatMessageAsync(ClientId, ChatMessage.Trim());
        if (result is null)
        {
            return await FailAndReloadAsync("Die Nachricht konnte nicht gesendet werden.");
        }

        ChatMessage = string.Empty;
        ActionMessage = "Nachricht wurde an den Client gesendet.";
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostSaveMetadataAsync()
    {
        if (!TryPrepareAuthenticatedRequest("edit-client", out _, out var redirect))
        {
            return redirect!;
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = "Demo-Modus: Metadaten wurden simuliert gespeichert.";
            return await LoadClientPageAsync();
        }

        var result = await _apiClient.UpdateClientMetadataAsync(ClientId, Notes, RustDeskId, RustDeskPassword, RemoteUserName, RemotePassword);
        if (result?.Success != true)
        {
            return await FailAndReloadAsync(result?.Message ?? "Client-Metadaten konnten nicht gespeichert werden.");
        }

        ActionMessage = result.Message;
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostUploadFileAsync()
    {
        if (!TryPrepareAuthenticatedRequest("files", out _, out var redirect))
        {
            return redirect!;
        }

        if (UploadFile is null || UploadFile.Length == 0)
        {
            return await FailAndReloadAsync("Bitte wähle zuerst eine Datei für den Upload aus.");
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = $"Demo-Modus: Upload '{UploadFile.FileName}' wurde simuliert.";
            return await LoadClientPageAsync();
        }

        await using var stream = UploadFile.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);

        var targetPath = string.IsNullOrWhiteSpace(UploadTargetPath) ? UploadFile.FileName : UploadTargetPath.Trim();
        var result = await _apiClient.QueueFileUploadAsync(ClientId, UploadFile.FileName, targetPath, Convert.ToBase64String(memoryStream.ToArray()));
        if (result is null)
        {
            return await FailAndReloadAsync("Der Datei-Upload konnte nicht in die Warteschlange gelegt werden.");
        }

        LatestTransferId = result.TransferId;
        ActionMessage = result.Message;
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostDownloadFileAsync()
    {
        if (!TryPrepareAuthenticatedRequest("files", out _, out var redirect))
        {
            return redirect!;
        }

        if (string.IsNullOrWhiteSpace(DownloadSourcePath))
        {
            return await FailAndReloadAsync("Bitte gib einen Quellpfad für den Download an.");
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = "Demo-Modus: Datei-Download wurde simuliert.";
            return await LoadClientPageAsync();
        }

        var result = await _apiClient.QueueFileDownloadAsync(ClientId, DownloadSourcePath.Trim());
        if (result is null)
        {
            return await FailAndReloadAsync("Der Datei-Download konnte nicht in die Warteschlange gelegt werden.");
        }

        LatestTransferId = result.TransferId;
        ActionMessage = result.Message;
        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostRefreshTransferAsync()
    {
        if (!TryPrepareAuthenticatedRequest("files", out _, out var redirect))
        {
            return redirect!;
        }

        if (LatestTransferId is null || LatestTransferId == Guid.Empty)
        {
            return await FailAndReloadAsync("Es ist noch kein Datei-Transfer ausgewählt.");
        }

        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnGetDownloadTransferContentAsync(Guid clientId, Guid transferId)
    {
        ClientId = clientId;
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var content = await _apiClient.GetFileTransferContentAsync(transferId);
        if (content is null || string.IsNullOrWhiteSpace(content.ContentBase64))
        {
            return RedirectToPage(new { id = clientId, tabs = "overview,files", active = "files", transferId });
        }

        return File(Convert.FromBase64String(content.ContentBase64), "application/octet-stream", content.FileName);
    }

    public async Task<IActionResult> OnPostQueueProcessSnapshotAsync()
        => await QueueJobAsync("tasks", () => _apiClient.QueueProcessSnapshotAsync(ClientId), "Demo-Modus: Prozess-Snapshot wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueueServiceSnapshotAsync()
        => await QueueJobAsync("services", () => _apiClient.QueueServiceSnapshotAsync(ClientId), "Demo-Modus: Service-Snapshot wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueueWindowsUpdateScanAsync()
        => await QueueJobAsync("windows-updates", () => _apiClient.QueueWindowsUpdateScanAsync(ClientId), "Demo-Modus: Update-Scan wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueueWindowsUpdateInstallAsync()
        => await QueueJobAsync("windows-updates", () => _apiClient.QueueWindowsUpdateInstallAsync(ClientId), "Demo-Modus: Update-Installation wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueueRegistrySnapshotAsync()
        => await QueueJobAsync("registry", () => _apiClient.QueueRegistrySnapshotAsync(ClientId, RegistryPath.Trim()), "Demo-Modus: Registry-Snapshot wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueuePowerSnapshotAsync()
        => await QueueJobAsync("power", () => _apiClient.QueuePowerPlanSnapshotAsync(ClientId), "Demo-Modus: Power-Plan-Snapshot wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueuePowerActivateAsync()
        => await QueueJobAsync("power", () => _apiClient.QueuePowerPlanActivateAsync(ClientId, PowerPlanGuid.Trim()), "Demo-Modus: Power-Plan-Aktivierung wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostQueueServiceControlAsync()
        => await QueueJobAsync("services", () => _apiClient.QueueServiceControlAsync(ClientId, ServiceName.Trim(), ServiceAction.Trim()), "Demo-Modus: Service-Steuerung wurde simuliert angefordert.");

    public async Task<IActionResult> OnPostExecuteScriptAsync()
        => await QueueJobAsync("ps-console", () => _apiClient.QueueScriptExecutionAsync(ClientId, ScriptContent), "Demo-Modus: PowerShell-Skript wurde simuliert an den Agenten gesendet.");

    public async Task<IActionResult> OnPostRefreshJobAsync()
    {
        if (!TryPrepareAuthenticatedRequest("tasks", out _, out var redirect))
        {
            return redirect!;
        }

        if (LatestJobId is null || LatestJobId == Guid.Empty)
        {
            return await FailAndReloadAsync("Es ist noch kein Agent-Job ausgewählt.");
        }

        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnGetDownloadRdpAsync(Guid id)
    {
        ClientId = id;
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var client = await _apiClient.GetClientAsync(id);
        if (client is null)
        {
            return RedirectToPage("/Dashboard");
        }

        var host = client.TailscaleIpAddresses.FirstOrDefault() ?? client.MachineName;
        var userName = client.RemoteUserName ?? string.Empty;
        var content = BuildRdpFileContent(host, userName);
        var fileName = $"{client.DeviceName}.rdp";
        return File(Encoding.UTF8.GetBytes(content), "application/x-rdp", fileName);
    }

    private async Task<IActionResult> QueueJobAsync(string targetTab, Func<Task<QueueAgentJobResponse?>> queueAction, string demoMessage)
    {
        if (!TryPrepareAuthenticatedRequest(targetTab, out _, out var redirect))
        {
            return redirect!;
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = demoMessage;
            return await LoadClientPageAsync();
        }

        var result = await queueAction();
        if (result is null)
        {
            return await FailAndReloadAsync("Der Agent-Job konnte nicht eingereiht werden.");
        }

        LatestJobId = result.JobId;
        ActionMessage = result.Message;
        return await LoadClientPageAsync();
    }

    private async Task<IActionResult> LoadClientPageAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var realClients = await _apiClient.GetClientsAsync();
        var hasRealClients = realClients is { Count: > 0 };

        if (hasRealClients)
        {
            Client = await _apiClient.GetClientAsync(ClientId);
            RemoteActions = await _apiClient.GetRemoteActionsAsync() ?? [];
            ChatMessages = await _apiClient.GetChatMessagesAsync(ClientId) ?? [];
            IsUsingDemoClient = false;
        }
        else
        {
            Client = _demoClientDataService.GetClient(ClientId);
            RemoteActions = _demoClientDataService.GetRemoteActions().ToList();
            ChatMessages = [];
            IsUsingDemoClient = Client is not null;
        }

        if (Client is null)
        {
            return NotFound();
        }

        if (LatestTransferId is Guid transferId && transferId != Guid.Empty && !IsUsingDemoClient)
        {
            LatestTransfer = await _apiClient.GetFileTransferAsync(transferId);
            if (LatestTransfer?.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true
                && LatestTransfer.Direction == FileTransferDirection.ClientToAdmin)
            {
                LatestTransferContent = await _apiClient.GetFileTransferContentAsync(transferId);
            }
        }

        if (LatestJobId is Guid jobId && jobId != Guid.Empty && !IsUsingDemoClient)
        {
            LatestAgentJob = await _apiClient.GetAgentJobAsync(jobId);
        }

        Notes = Client.Notes ?? string.Empty;
        RustDeskId = Client.RustDeskId ?? string.Empty;
        RustDeskPassword = Client.RustDeskPassword ?? string.Empty;
        RemoteUserName = Client.RemoteUserName ?? string.Empty;
        RemotePassword = Client.RemotePassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(PreferredChannel))
        {
            PreferredChannel = Client.SupportedChannels?.FirstOrDefault().ToString() ?? "RustDesk";
        }

        NormalizeWorkspaceState(ActiveTabKey);
        return Page();
    }

    private bool IsDemoClientRequest()
        => _demoClientDataService.GetClient(ClientId) is not null && IsUsingDemoClient;

    private bool TryPrepareAuthenticatedRequest(string targetTab, out string? displayName, out IActionResult? redirect)
    {
        displayName = HttpContext.Session.GetString("DisplayName") ?? "Web-Administrator";
        redirect = null;

        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("AccessToken")))
        {
            redirect = RedirectToPage("/Auth/Login");
            return false;
        }

        NormalizeWorkspaceState(targetTab);
        return true;
    }

    private async Task<IActionResult> FailAndReloadAsync(string message)
    {
        ActionMessage = message;
        IsActionError = true;
        return await LoadClientPageAsync();
    }

    private void NormalizeWorkspaceState(string? fallbackActiveTab)
    {
        var tabKeys = (OpenTabsState ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!tabKeys.Contains("overview", StringComparer.OrdinalIgnoreCase))
        {
            tabKeys.Insert(0, "overview");
        }

        if (!string.IsNullOrWhiteSpace(fallbackActiveTab)
            && !tabKeys.Contains(fallbackActiveTab, StringComparer.OrdinalIgnoreCase))
        {
            tabKeys.Add(fallbackActiveTab);
        }

        OpenTabsState = string.Join(",", tabKeys);
        ActiveTabKey = string.IsNullOrWhiteSpace(fallbackActiveTab) ? "overview" : fallbackActiveTab;
    }

    private static string BuildRdpFileContent(string host, string userName)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"full address:s:{host}");
        builder.AppendLine("prompt for credentials:i:1");
        builder.AppendLine("administrative session:i:1");
        builder.AppendLine("screen mode id:i:2");
        builder.AppendLine("use multimon:i:0");
        builder.AppendLine("redirectprinters:i:1");
        builder.AppendLine("redirectclipboard:i:1");
        builder.AppendLine("audiomode:i:0");

        if (!string.IsNullOrWhiteSpace(userName))
        {
            builder.AppendLine($"username:s:{userName}");
        }

        return builder.ToString();
    }
}

public sealed record ClientToolDefinition(string Key, string Label, string Description);
