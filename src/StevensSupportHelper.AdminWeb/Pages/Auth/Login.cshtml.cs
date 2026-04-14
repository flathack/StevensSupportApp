using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages.Auth;

public class LoginModel : PageModel
{
    private readonly ApiClient _apiClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LoginModel(ApiClient apiClient, IHttpContextAccessor httpContextAccessor)
    {
        _apiClient = apiClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // Check if already logged in
        var token = HttpContext.Session.GetString("AccessToken");
        if (!string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Dashboard");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password.";
            return Page();
        }

        var response = await _apiClient.LoginAsync(Username, Password);
        if (response is null)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        _apiClient.SetAccessToken(response.AccessToken);
        HttpContext.Session.SetString("AccessToken", response.AccessToken);
        HttpContext.Session.SetString("UserId", response.User.Id.ToString());
        HttpContext.Session.SetString("DisplayName", response.User.DisplayName);

        return RedirectToPage("/Dashboard");
    }
}