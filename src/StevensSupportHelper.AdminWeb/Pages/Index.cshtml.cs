using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StevensSupportHelper.AdminWeb.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        return string.IsNullOrWhiteSpace(token)
            ? RedirectToPage("/Auth/Login")
            : RedirectToPage("/Dashboard");
    }
}
