using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public class UsersModel : PageModel
{
    private static readonly IReadOnlyList<string> AvailableRoles = ["Administrator", "Operator", "Auditor"];
    private readonly ApiClient _apiClient;

    public UsersModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string DisplayName { get; set; } = "Administrator";
    public string UserId { get; set; } = "-";
    public List<string> Roles { get; set; } = [];
    public List<UserInfoResponse> Users { get; set; } = [];
    public IReadOnlyList<string> RoleOptions => AvailableRoles;
    public string? StatusMessage { get; set; }
    public bool IsError { get; set; }

    [BindProperty]
    public string NewUsername { get; set; } = string.Empty;

    [BindProperty]
    public string NewDisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public List<string> NewUserRoles { get; set; } = ["Operator"];

    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ChangedPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmedPassword { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostCreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword))
        {
            return await LoadPageAsync("Benutzername und Passwort sind erforderlich.", true);
        }

        if (NewUserRoles.Count == 0)
        {
            return await LoadPageAsync("Bitte mindestens eine Rolle auswählen.", true);
        }

        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var response = await _apiClient.CreateUserAsync(new CreateUserRequest(
            NewUsername.Trim(),
            NewPassword,
            string.IsNullOrWhiteSpace(NewDisplayName) ? NewUsername.Trim() : NewDisplayName.Trim(),
            NewUserRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));

        return await LoadPageAsync(response?.Message ?? "Benutzer konnte nicht angelegt werden.", response?.Success != true);
    }

    public async Task<IActionResult> OnPostUpdateRolesAsync(Guid userId, List<string> roles)
    {
        if (roles.Count == 0)
        {
            return await LoadPageAsync("Bitte mindestens eine Rolle auswählen.", true);
        }

        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var response = await _apiClient.UpdateUserRolesAsync(userId, roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        return await LoadPageAsync(response?.Message ?? "Rollen konnten nicht aktualisiert werden.", response?.Success != true);
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid userId, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return await LoadPageAsync("Bitte ein neues Passwort eingeben.", true);
        }

        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var response = await _apiClient.ResetUserPasswordAsync(userId, newPassword);
        return await LoadPageAsync(response?.Message ?? "Passwort konnte nicht zurückgesetzt werden.", response?.Success != true);
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(Guid userId)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        var currentUserId = HttpContext.Session.GetString("UserId");
        if (Guid.TryParse(currentUserId, out var currentUserGuid) && currentUserGuid == userId)
        {
            return await LoadPageAsync("Der aktuell angemeldete Benutzer kann nicht gelöscht werden.", true);
        }

        _apiClient.SetAccessToken(token);
        var response = await _apiClient.DeleteUserAsync(userId);
        return await LoadPageAsync(response?.Message ?? "Benutzer konnte nicht gelöscht werden.", response?.Success != true);
    }

    public async Task<IActionResult> OnPostChangeOwnPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(ChangedPassword))
        {
            return await LoadPageAsync("Bitte aktuelles und neues Passwort eingeben.", true);
        }

        if (!string.Equals(ChangedPassword, ConfirmedPassword, StringComparison.Ordinal))
        {
            return await LoadPageAsync("Die neuen Passwörter stimmen nicht überein.", true);
        }

        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var response = await _apiClient.ChangeOwnPasswordAsync(CurrentPassword, ChangedPassword);
        return await LoadPageAsync(response?.Message ?? "Passwort konnte nicht geändert werden.", response?.Success != true);
    }

    private async Task<IActionResult> LoadPageAsync(string? statusMessage = null, bool isError = false)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        DisplayName = HttpContext.Session.GetString("DisplayName") ?? "Administrator";
        UserId = HttpContext.Session.GetString("UserId") ?? "-";
        StatusMessage = statusMessage;
        IsError = isError;

        var currentUser = await _apiClient.GetCurrentUserAsync();
        if (currentUser is not null)
        {
            DisplayName = currentUser.DisplayName;
            UserId = currentUser.Id.ToString();
            Roles = currentUser.Roles.ToList();
        }

        var users = await _apiClient.GetUsersAsync();
        Users = users?.OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase).ToList() ?? [];

        if (users is null && string.IsNullOrEmpty(StatusMessage))
        {
            StatusMessage = "Die Benutzerliste konnte nicht geladen werden.";
            IsError = true;
        }

        return Page();
    }
}
