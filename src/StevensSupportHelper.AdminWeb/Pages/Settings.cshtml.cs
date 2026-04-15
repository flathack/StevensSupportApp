using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public sealed class SettingsModel : PageModel
{
    private readonly ApiClient _apiClient;

    public SettingsModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public DeploymentSettingsForm Input { get; set; } = new();

    public IReadOnlyList<DeploymentAssetResponse> Assets { get; private set; } = [];
    public string? ActionMessage { get; private set; }
    public bool ActionSucceeded { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var result = await _apiClient.SaveDeploymentSettingsAsync(Input.ToRequest());
        ActionSucceeded = result?.Success == true;
        ActionMessage = result?.Message ?? "Die Einstellungen konnten nicht gespeichert werden.";
        return await LoadPageAsync(result?.Data);
    }

    private async Task<IActionResult> LoadPageAsync(DeploymentSettingsResponse? preferredSettings = null)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var snapshot = await _apiClient.GetDeploymentSnapshotAsync();
        if (snapshot is null)
        {
            ActionSucceeded = false;
            ActionMessage = ActionMessage ?? "Die Deployment-Einstellungen konnten nicht vom Server geladen werden.";
            Assets = [];
            return Page();
        }

        Input = DeploymentSettingsForm.FromResponse(preferredSettings ?? snapshot.Settings);
        Assets = snapshot.Assets;
        return Page();
    }

    public sealed class DeploymentSettingsForm
    {
        public string ServerUrl { get; set; } = "http://localhost:5000";
        public string ApiKey { get; set; } = string.Empty;
        public string ServerProjectPath { get; set; } = string.Empty;
        public string RustDeskPath { get; set; } = string.Empty;
        public string RustDeskPassword { get; set; } = string.Empty;
        public string ClientInstallerPath { get; set; } = string.Empty;
        public string RemoteActionsPath { get; set; } = string.Empty;
        public string PackageGeneratorPath { get; set; } = string.Empty;
        public string RemoteUserName { get; set; } = string.Empty;
        public string RemotePassword { get; set; } = string.Empty;
        public string PreferredChannel { get; set; } = "Rdp";
        public string Reason { get; set; } = "Remote support requested.";
        public string DefaultRegistrationSharedKey { get; set; } = string.Empty;
        public string DefaultInstallRoot { get; set; } = @"C:\Program Files\StevensSupportHelper";
        public string DefaultServiceName { get; set; } = "StevensSupportHelperClientService";

        public DeploymentSettingsRequest ToRequest()
        {
            return new DeploymentSettingsRequest(
                ServerUrl,
                ApiKey,
                ServerProjectPath,
                RustDeskPath,
                RustDeskPassword,
                ClientInstallerPath,
                RemoteActionsPath,
                PackageGeneratorPath,
                RemoteUserName,
                RemotePassword,
                PreferredChannel,
                Reason,
                DefaultRegistrationSharedKey,
                DefaultInstallRoot,
                DefaultServiceName);
        }

        public static DeploymentSettingsForm FromResponse(DeploymentSettingsResponse response)
        {
            return new DeploymentSettingsForm
            {
                ServerUrl = response.ServerUrl,
                ApiKey = response.ApiKey,
                ServerProjectPath = response.ServerProjectPath,
                RustDeskPath = response.RustDeskPath,
                RustDeskPassword = response.RustDeskPassword,
                ClientInstallerPath = response.ClientInstallerPath,
                RemoteActionsPath = response.RemoteActionsPath,
                PackageGeneratorPath = response.PackageGeneratorPath,
                RemoteUserName = response.RemoteUserName,
                RemotePassword = response.RemotePassword,
                PreferredChannel = response.PreferredChannel,
                Reason = response.Reason,
                DefaultRegistrationSharedKey = response.DefaultRegistrationSharedKey,
                DefaultInstallRoot = response.DefaultInstallRoot,
                DefaultServiceName = response.DefaultServiceName
            };
        }
    }
}
