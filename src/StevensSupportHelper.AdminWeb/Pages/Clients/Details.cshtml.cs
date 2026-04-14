using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages.Clients;

public class DetailsModel : PageModel
{
    private readonly ApiClient _apiClient;

    public DetailsModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public Guid ClientId { get; set; }

    public ClientDetailResponse? Client { get; set; }
    public List<RemoteActionResponse> RemoteActions { get; set; } = [];

    [BindProperty]
    public string SupportReason { get; set; } = string.Empty;

    [BindProperty]
    public string PreferredChannel { get; set; } = "RustDesk";

    [BindProperty]
    public string SelectedScript { get; set; } = string.Empty;

    public string? ActionMessage { get; set; }
    public bool IsActionError { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        ClientId = id;
        Client = await _apiClient.GetClientAsync(id);
        var actions = await _apiClient.GetRemoteActionsAsync();
        RemoteActions = actions ?? [];

        if (Client is null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSupportAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var displayName = HttpContext.Session.GetString("DisplayName") ?? "Web Admin";

        var result = await _apiClient.CreateSupportRequestAsync(ClientId, displayName, PreferredChannel, SupportReason);
        if (result is null)
        {
            ActionMessage = "Failed to create support request.";
            IsActionError = true;
        }
        else
        {
            ActionMessage = $"Support request created: {result.Message}";
            IsActionError = false;
        }

        Client = await _apiClient.GetClientAsync(ClientId);
        return Page();
    }

    public async Task<IActionResult> OnPostEndSessionAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var result = await _apiClient.EndSessionAsync(ClientId);
        if (result is null)
        {
            ActionMessage = "Failed to end session.";
            IsActionError = true;
        }
        else
        {
            ActionMessage = $"Session ended: {result.Message}";
            IsActionError = false;
        }

        Client = await _apiClient.GetClientAsync(ClientId);
        return Page();
    }

    public async Task<IActionResult> OnPostExecuteScriptAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        if (string.IsNullOrEmpty(SelectedScript))
        {
            ActionMessage = "Please select a script.";
            IsActionError = true;
            Client = await _apiClient.GetClientAsync(ClientId);
            var actions = await _apiClient.GetRemoteActionsAsync();
            RemoteActions = actions ?? [];
            return Page();
        }

        _apiClient.SetAccessToken(token);
        var result = await _apiClient.ExecuteRemoteActionAsync(ClientId, SelectedScript);
        if (result is null)
        {
            ActionMessage = "Failed to execute script.";
            IsActionError = true;
        }
        else
        {
            ActionMessage = $"Script queued: {result.Message}";
            IsActionError = false;
        }

        Client = await _apiClient.GetClientAsync(ClientId);
        var remoteActions = await _apiClient.GetRemoteActionsAsync();
        RemoteActions = remoteActions ?? [];
        return Page();
    }
}