namespace StevensSupportHelper.Client.Service.Options;

public sealed class ServiceOptions
{
    public const string SectionName = "StevensSupportHelper";

    public string ServerBaseUrl { get; set; } = "http://localhost:5000";
    public string DeviceName { get; set; } = Environment.MachineName;
    public bool ConsentRequired { get; set; } = true;
    public bool AutoApproveSupportRequests { get; set; }
    public bool AllowSupportAtLogonScreen { get; set; } = true;
    public bool TailscaleConnected { get; set; }
    public string[] TailscaleIpAddresses { get; set; } = [];
    public bool AutoDetectTailscaleIps { get; set; } = true;
    public bool AutoDetectRustDeskId { get; set; } = true;
    public string RustDeskId { get; set; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; set; } = 15;
    public string RegistrationSharedKey { get; set; } = "change-me-registration-key";
    public string ManagedFilesRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "ManagedFiles");
    public int MaxTransferBytes { get; set; } = 5 * 1024 * 1024;
    public string UpdateManifestUrl { get; set; } = string.Empty;
    public string UpdateChannel { get; set; } = "stable";
    public int UpdateCheckIntervalMinutes { get; set; } = 360;
    public string UpdatesRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "Updates");
}
