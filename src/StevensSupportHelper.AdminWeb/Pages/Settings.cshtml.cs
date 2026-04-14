using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public class SettingsModel : PageModel
{
    private readonly ApiClient _apiClient;

    public SettingsModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public List<UserInfoResponse> Users { get; set; } = [];

    [BindProperty]
    public string NewUsername { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewDisplayName { get; set; } = string.Empty;

    public string? ActionMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        // Get users via API - need to add this method
        return Page();
    }
}