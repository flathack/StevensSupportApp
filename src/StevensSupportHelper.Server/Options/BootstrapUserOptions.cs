namespace StevensSupportHelper.Server.Options;

public sealed class BootstrapUserOptions
{
    public const string SectionName = "StevensSupportHelperBootstrapUser";

    public bool Enabled { get; set; } = false;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}
