namespace StevensSupportHelper.Server.Options;

public sealed class ServerStorageOptions
{
    public const string SectionName = "StevensSupportHelperServer";

    public string Provider { get; set; } = "Json";
    public string StateFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "server-state.json");
    public string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "server-state.db");

    public int MaxAuditEntries { get; set; } = 200;
    public int MaxTransferBytes { get; set; } = 5 * 1024 * 1024;
    public int SessionTimeoutMinutes { get; set; } = 60;
    public int ConsentTimeoutMinutes { get; set; } = 5;
}
