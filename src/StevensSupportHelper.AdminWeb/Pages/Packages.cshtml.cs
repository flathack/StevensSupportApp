using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StevensSupportHelper.AdminWeb.Services;

namespace StevensSupportHelper.AdminWeb.Pages;

public sealed class PackagesModel : PageModel
{
    private readonly ApiClient _apiClient;

    public PackagesModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public PackageProfileForm Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ClientInstallerUpload { get; set; }

    [BindProperty]
    public IFormFile? RustDeskInstallerUpload { get; set; }

    [BindProperty]
    public IFormFile? TailscaleInstallerUpload { get; set; }

    public IReadOnlyList<DeploymentProfileResponse> Profiles { get; private set; } = [];
    public IReadOnlyList<DeploymentAssetResponse> Assets { get; private set; } = [];
    public Guid? SelectedProfileId { get; private set; }
    public string ConfigPreview { get; private set; } = string.Empty;
    public string? ActionMessage { get; private set; }
    public bool ActionSucceeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? profileId = null)
    {
        SelectedProfileId = profileId;
        return await LoadPageAsync(profileId);
    }

    public async Task<IActionResult> OnPostSaveProfileAsync()
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var result = await _apiClient.SaveDeploymentProfileAsync(Input.ToRequest());
        ActionSucceeded = result?.Success == true;
        ActionMessage = result?.Message ?? "Das Kundenprofil konnte nicht gespeichert werden.";
        SelectedProfileId = result?.Data?.Id ?? (Input.Id == Guid.Empty ? null : Input.Id);
        return await LoadPageAsync(SelectedProfileId, preferredProfile: result?.Data);
    }

    public async Task<IActionResult> OnPostDeleteProfileAsync(Guid profileId)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var result = await _apiClient.DeleteDeploymentProfileAsync(profileId);
        ActionSucceeded = result?.Success == true;
        ActionMessage = result?.Message ?? "Das Kundenprofil konnte nicht gelöscht werden.";
        SelectedProfileId = null;
        return await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostUploadAsync(string assetKind)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        var file = ResolveUpload(assetKind);
        if (file is null || file.Length == 0)
        {
            ActionSucceeded = false;
            ActionMessage = "Bitte wähle zuerst eine Datei aus.";
            return await LoadPageAsync(Input.Id == Guid.Empty ? null : Input.Id);
        }

        _apiClient.SetAccessToken(token);
        await using var stream = file.OpenReadStream();
        var result = await _apiClient.UploadDeploymentAssetAsync(assetKind, stream, file.FileName, file.ContentType);
        ActionSucceeded = result?.Success == true;
        ActionMessage = result?.Message ?? "Der Upload konnte nicht gespeichert werden.";
        return await LoadPageAsync(Input.Id == Guid.Empty ? null : Input.Id);
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid profileId)
    {
        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Auth/Login");
        }

        _apiClient.SetAccessToken(token);
        var package = await _apiClient.DownloadDeploymentPackageAsync(profileId);
        if (package is null)
        {
            ActionSucceeded = false;
            ActionMessage = "Das ZIP-Paket konnte nicht erzeugt werden.";
            return await LoadPageAsync(profileId);
        }

        return File(package.Content, package.ContentType, package.FileName);
    }

    private async Task<IActionResult> LoadPageAsync(Guid? profileId = null, DeploymentProfileResponse? preferredProfile = null)
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
            ActionMessage = ActionMessage ?? "Der Paketgenerator konnte nicht geladen werden.";
            return Page();
        }

        Profiles = snapshot.Profiles;
        Assets = snapshot.Assets;

        var activeProfile = preferredProfile
            ?? (profileId.HasValue ? snapshot.Profiles.FirstOrDefault(profile => profile.Id == profileId.Value) : snapshot.Profiles.FirstOrDefault());

        SelectedProfileId = activeProfile?.Id;
        Input = activeProfile is null
            ? PackageProfileForm.CreateNew(snapshot.Settings)
            : PackageProfileForm.FromResponse(activeProfile);

        if (activeProfile is not null)
        {
            var configResponse = await _apiClient.GetDeploymentProfileConfigAsync(activeProfile.Id);
            ConfigPreview = configResponse?.ConfigText ?? string.Empty;
        }
        else
        {
            ConfigPreview = string.Empty;
        }

        return Page();
    }

    private IFormFile? ResolveUpload(string assetKind)
    {
        return assetKind switch
        {
            "client-installer" => ClientInstallerUpload,
            "rustdesk-installer" => RustDeskInstallerUpload,
            "tailscale-installer" => TailscaleInstallerUpload,
            _ => null
        };
    }

    public sealed class PackageProfileForm
    {
        public Guid Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string ServerUrl { get; set; } = "http://localhost:5000";
        public string RegistrationSharedKey { get; set; } = string.Empty;
        public string InstallRoot { get; set; } = @"C:\Program Files\StevensSupportHelper";
        public string ServiceName { get; set; } = "StevensSupportHelperClientService";
        public bool InstallRustDesk { get; set; } = true;
        public bool InstallTailscale { get; set; } = true;
        public string TailscaleAuthKey { get; set; } = string.Empty;
        public bool EnableAutoApprove { get; set; } = true;
        public bool EnableRdp { get; set; } = true;
        public bool CreateServiceUser { get; set; }
        public bool ServiceUserIsAdministrator { get; set; } = true;
        public string ServiceUserName { get; set; } = string.Empty;
        public string ServiceUserPassword { get; set; } = string.Empty;
        public string RustDeskId { get; set; } = string.Empty;
        public string RustDeskPassword { get; set; } = string.Empty;
        public string TailscaleIpAddresses { get; set; } = string.Empty;
        public bool Silent { get; set; } = true;

        public DeploymentProfileRequest ToRequest()
        {
            return new DeploymentProfileRequest(
                Id,
                CustomerName,
                DeviceName,
                Notes,
                ServerUrl,
                RegistrationSharedKey,
                InstallRoot,
                ServiceName,
                InstallRustDesk,
                InstallTailscale,
                TailscaleAuthKey,
                EnableAutoApprove,
                EnableRdp,
                CreateServiceUser,
                ServiceUserIsAdministrator,
                ServiceUserName,
                ServiceUserPassword,
                RustDeskId,
                RustDeskPassword,
                TailscaleIpAddresses
                    .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                Silent);
        }

        public static PackageProfileForm CreateNew(DeploymentSettingsResponse settings)
        {
            return new PackageProfileForm
            {
                ServerUrl = settings.ServerUrl,
                RegistrationSharedKey = settings.DefaultRegistrationSharedKey,
                InstallRoot = settings.DefaultInstallRoot,
                ServiceName = settings.DefaultServiceName,
                RustDeskPassword = settings.RustDeskPassword
            };
        }

        public static PackageProfileForm FromResponse(DeploymentProfileResponse profile)
        {
            return new PackageProfileForm
            {
                Id = profile.Id,
                CustomerName = profile.CustomerName,
                DeviceName = profile.DeviceName,
                Notes = profile.Notes,
                ServerUrl = profile.ServerUrl,
                RegistrationSharedKey = profile.RegistrationSharedKey,
                InstallRoot = profile.InstallRoot,
                ServiceName = profile.ServiceName,
                InstallRustDesk = profile.InstallRustDesk,
                InstallTailscale = profile.InstallTailscale,
                TailscaleAuthKey = profile.TailscaleAuthKey,
                EnableAutoApprove = profile.EnableAutoApprove,
                EnableRdp = profile.EnableRdp,
                CreateServiceUser = profile.CreateServiceUser,
                ServiceUserIsAdministrator = profile.ServiceUserIsAdministrator,
                ServiceUserName = profile.ServiceUserName,
                ServiceUserPassword = profile.ServiceUserPassword,
                RustDeskId = profile.RustDeskId,
                RustDeskPassword = profile.RustDeskPassword,
                TailscaleIpAddresses = string.Join(Environment.NewLine, profile.TailscaleIpAddresses),
                Silent = profile.Silent
            };
        }
    }
}
