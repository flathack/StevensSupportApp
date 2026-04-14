using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public class AuditModel : PageModel
{
    private readonly ApiClient _apiClient;

    public AuditModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public List<AuditEntryResponse> Entries { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var entries = await _apiClient.GetAuditEntriesAsync(200);
        Entries = entries ?? [];

        return Page();
    }
}