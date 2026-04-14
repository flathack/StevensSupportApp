using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages.Clients;

public class DetailsModel : PageModel
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
    public string SupportReason { get; set; } = string.Empty;

    [BindProperty]
    public string PreferredChannel { get; set; } = "RustDesk";

    [BindProperty]
    public string SelectedScript { get; set; } = string.Empty;

    [BindProperty]
    public string RegistryPath { get; set; } = @"HKLM\Software";

    [BindProperty]
    public string ServiceName { get; set; } = "Spooler";

    [BindProperty]
    public string PowerPlanGuid { get; set; } = string.Empty;

    [BindProperty]
    public string OpenTabsState { get; set; } = "overview";

    [BindProperty]
    public string ActiveTabKey { get; set; } = "overview";

    public ClientDetailResponse? Client { get; set; }
    public List<RemoteActionResponse> RemoteActions { get; set; } = [];
    public string? ActionMessage { get; set; }
    public bool IsActionError { get; set; }
    public bool IsUsingDemoClient { get; set; }

    public IReadOnlyList<ClientToolDefinition> ToolDefinitions { get; } =
    [
        new("support", "Support", "Support anfordern"),
        new("connect", "Verbinden", "Sitzung starten"),
        new("rdp", "RDP", "RDP öffnen"),
        new("rustdesk", "RustDesk", "RustDesk starten"),
        new("ps-console", "PS-Konsole", "PowerShell-Skript ausführen"),
        new("dashboard", "Dashboard", "Geräteübersicht"),
        new("files", "Dateien", "Dateiaktionen vorbereiten"),
        new("tasks", "Aufgaben", "Aufgaben einsehen"),
        new("services", "Dienste", "Dienste steuern"),
        new("software", "Software", "Installationen anzeigen"),
        new("registry", "Registry", "Registry-Snapshot"),
        new("power", "Energie", "Energieprofile"),
        new("windows-updates", "Windows-Updates", "Updates prüfen"),
        new("chat", "Chat", "Kommunikation"),
        new("remote-actions", "Remote Actions", "Automationen ausführen"),
        new("screenshot", "Screenshot", "Bildschirmaufnahme anfordern"),
        new("edit-client", "Client bearbeiten", "Metadaten pflegen"),
        new("end-session", "Sitzung beenden", "Aktive Sitzung schließen")
    ];

    public async Task<IActionResult> OnGetAsync(Guid id, string? tabs = null, string? active = null)
    {
        ClientId = id;
        OpenTabsState = string.IsNullOrWhiteSpace(tabs) ? "overview" : tabs;
        ActiveTabKey = string.IsNullOrWhiteSpace(active) ? "overview" : active;

        if (string.IsNullOrWhiteSpace(OpenTabsState) || !OpenTabsState.Contains("overview", StringComparison.Ordinal))
        {
            OpenTabsState = $"overview,{OpenTabsState}".Trim(',');
        }

        return await LoadClientPageAsync();
    }

    public async Task<IActionResult> OnPostSupportAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var displayName = HttpContext.Session.GetString("DisplayName") ?? "Web-Administrator";
        NormalizeWorkspaceState("support");

        if (IsDemoClientRequest())
        {
            ActionMessage = $"Demo-Modus: Support-Anfrage für '{ClientId}' wurde simuliert.";
            return await LoadClientPageAsync(false);
        }

        var result = await _apiClient.CreateSupportRequestAsync(ClientId, displayName, PreferredChannel, SupportReason);
        if (result is null)
        {
            ActionMessage = "Support-Anfrage konnte nicht erstellt werden.";
            return await LoadClientPageAsync(true);
        }

        ActionMessage = $"Support-Anfrage erstellt: {result.Message}";
        return await LoadClientPageAsync(false);
    }

    public async Task<IActionResult> OnPostEndSessionAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        NormalizeWorkspaceState("end-session");

        if (IsDemoClientRequest())
        {
            ActionMessage = "Demo-Modus: Sitzung wurde simuliert beendet.";
            return await LoadClientPageAsync(false);
        }

        var result = await _apiClient.EndSessionAsync(ClientId);
        if (result is null)
        {
            ActionMessage = "Sitzung konnte nicht beendet werden.";
            return await LoadClientPageAsync(true);
        }

        ActionMessage = $"Sitzung beendet: {result.Message}";
        return await LoadClientPageAsync(false);
    }

    public async Task<IActionResult> OnPostExecuteScriptAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        NormalizeWorkspaceState("remote-actions");

        if (string.IsNullOrWhiteSpace(SelectedScript))
        {
            ActionMessage = "Bitte ein Skript auswählen.";
            return await LoadClientPageAsync(true);
        }

        if (IsDemoClientRequest())
        {
            ActionMessage = $"Demo-Modus: Skript '{SelectedScript}' wurde simuliert in die Warteschlange gelegt.";
            return await LoadClientPageAsync(false);
        }

        var result = await _apiClient.ExecuteRemoteActionAsync(ClientId, SelectedScript);
        if (result is null)
        {
            ActionMessage = "Skript konnte nicht gestartet werden.";
            return await LoadClientPageAsync(true);
        }

        ActionMessage = $"Skript gestartet: {result.Message}";
        return await LoadClientPageAsync(false);
    }

    public async Task<IActionResult> OnPostQueuePlaceholderAsync(string operation, string? target = null)
    {
        NormalizeWorkspaceState(string.IsNullOrWhiteSpace(target) ? "overview" : target);
        ActionMessage = $"'{operation}' wurde als Platzhalteraktion geöffnet. Für echte Ausführung kann die API jetzt schrittweise angebunden werden.";
        return await LoadClientPageAsync(false);
    }

    private async Task<IActionResult> LoadClientPageAsync(bool isError = false)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        IsActionError = isError;

        var realClients = await _apiClient.GetClientsAsync();
        var hasRealClients = realClients is { Count: > 0 };

        if (hasRealClients)
        {
            Client = await _apiClient.GetClientAsync(ClientId);
            var actions = await _apiClient.GetRemoteActionsAsync();
            RemoteActions = actions ?? [];
        }
        else
        {
            Client = _demoClientDataService.GetClient(ClientId);
            RemoteActions = _demoClientDataService.GetRemoteActions().ToList();
            IsUsingDemoClient = Client is not null;
        }

        if (Client is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(PowerPlanGuid))
        {
            PowerPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        }

        return Page();
    }

    private bool IsDemoClientRequest()
    {
        return _demoClientDataService.GetClient(ClientId) is not null;
    }

    private void NormalizeWorkspaceState(string fallbackActiveTab)
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

        if (!string.IsNullOrWhiteSpace(fallbackActiveTab) && !tabKeys.Contains(fallbackActiveTab, StringComparer.OrdinalIgnoreCase))
        {
            tabKeys.Add(fallbackActiveTab);
        }

        OpenTabsState = string.Join(",", tabKeys);
        ActiveTabKey = string.IsNullOrWhiteSpace(ActiveTabKey) ? fallbackActiveTab : ActiveTabKey;
    }
}

public sealed record ClientToolDefinition(string Key, string Label, string Description);
