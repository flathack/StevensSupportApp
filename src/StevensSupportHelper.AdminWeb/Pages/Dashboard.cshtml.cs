using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public class DashboardModel : PageModel
{
    private const string DemoModeSessionKey = "AdminWeb.DemoModeEnabled";
    private readonly ApiClient _apiClient;
    private readonly DemoClientDataService _demoClientDataService;

    public DashboardModel(ApiClient apiClient, DemoClientDataService demoClientDataService)
    {
        _apiClient = apiClient;
        _demoClientDataService = demoClientDataService;
    }

    public string DisplayName { get; set; } = string.Empty;
    public List<ClientSummaryResponse> Clients { get; set; } = [];
    public bool IsUsingDemoClients { get; private set; }
    public bool CanEnableDemoMode { get; private set; }
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
        DisplayName = HttpContext.Session.GetString("DisplayName") ?? "Admin";

        var clients = await _apiClient.GetClientsAsync();
        if (clients is { Count: > 0 })
        {
            Clients = clients;
            IsUsingDemoClients = false;
            CanEnableDemoMode = false;
            HttpContext.Session.Remove(DemoModeSessionKey);
        }
        else if (IsDemoModeEnabled())
        {
            Clients = _demoClientDataService.GetClients().ToList();
            IsUsingDemoClients = true;
            CanEnableDemoMode = true;
        }
        else
        {
            Clients = [];
            IsUsingDemoClients = false;
            CanEnableDemoMode = true;
        }

        return Page();
    }

    public IActionResult OnPostEnableDemoAsync()
    {
        HttpContext.Session.SetString(DemoModeSessionKey, bool.TrueString);
        return RedirectToPage();
    }

    public IActionResult OnPostDisableDemoAsync()
    {
        HttpContext.Session.Remove(DemoModeSessionKey);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        HttpContext.Session.Clear();
        return RedirectToPage("/Auth/Login");
    }

    private bool IsDemoModeEnabled()
        => string.Equals(HttpContext.Session.GetString(DemoModeSessionKey), bool.TrueString, StringComparison.OrdinalIgnoreCase);
}
