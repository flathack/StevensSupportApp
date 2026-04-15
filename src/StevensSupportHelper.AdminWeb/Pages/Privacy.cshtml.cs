using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.AdminWeb.Pages;

public class PrivacyModel : PageModel
{
    private readonly ApiClient _apiClient;

    public PrivacyModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public UserInfoResponse? CurrentUser { get; private set; }
    public AdminSessionInfoResponse? SessionInfo { get; private set; }
    public HardcodedSuperAdminStateResponse? HardcodedSuperAdmin { get; private set; }
    public List<UserInfoResponse> Users { get; private set; } = [];
    public string? StatusMessage { get; private set; }
    public bool IsError { get; private set; }

    public int AdministratorCount => Users.Count(user => user.Roles.Contains("Administrator", StringComparer.OrdinalIgnoreCase));
    public int OperatorCount => Users.Count(user => user.Roles.Contains("Operator", StringComparer.OrdinalIgnoreCase));
    public int AuditorCount => Users.Count(user => user.Roles.Contains("Auditor", StringComparer.OrdinalIgnoreCase));
    public int MfaEnabledCount => Users.Count(user => user.IsMfaEnabled);

    public async Task<IActionResult> OnGetAsync()
        => await LoadPageAsync();

    public async Task<IActionResult> OnPostSetHardcodedSuperAdminStateAsync(bool enabled)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var response = await _apiClient.UpdateHardcodedSuperAdminStateAsync(enabled);
        return await LoadPageAsync(response?.Message ?? "Der Status des Notfallzugangs konnte nicht aktualisiert werden.", response?.Success != true);
    }

    private async Task<IActionResult> LoadPageAsync(string? statusMessage = null, bool isError = false)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        StatusMessage = statusMessage;
        IsError = isError;

        CurrentUser = await _apiClient.GetCurrentUserAsync();
        SessionInfo = await _apiClient.GetAdminSessionAsync();
        HardcodedSuperAdmin = await _apiClient.GetHardcodedSuperAdminStateAsync();
        Users = await _apiClient.GetUsersAsync() ?? [];

        if (CurrentUser is null && string.IsNullOrWhiteSpace(StatusMessage))
        {
            StatusMessage = "Der aktuelle Sitzungsstatus konnte nicht geladen werden.";
            IsError = true;
        }

        return Page();
    }
}
