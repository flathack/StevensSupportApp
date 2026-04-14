using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public class DashboardModel : PageModel
{
    private readonly ApiClient _apiClient;

    public DashboardModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string DisplayName { get; set; } = string.Empty;
    public List<ClientSummaryResponse> Clients { get; set; } = [];
    public int OnlineCount => Clients.Count(c => c.IsOnline);
    public int OfflineCount => Clients.Count(c => !c.IsOnline);

    public async Task<IActionResult> OnGetAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        _apiClient.SetAccessToken(token);
        
        DisplayName = HttpContext.Session.GetString("DisplayName") ?? "Admin";
        
        var clients = await _apiClient.GetClientsAsync();
        Clients = clients ?? [];

        return Page();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        HttpContext.Session.Clear();
        return RedirectToPage("/Auth/Login");
    }
}