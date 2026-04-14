namespace StevensSupportHelper.Server.Options;

public sealed class AdminAuthOptions
{
	public const string SectionName = "StevensSupportHelperAdminAuth";

	public string ApiKeyHeaderName { get; set; } = "X-Admin-ApiKey";
	public string MfaCodeHeaderName { get; set; } = "X-Admin-Totp";
	public int TotpTimeStepSeconds { get; set; } = 30;
	public int TotpAllowedDriftSteps { get; set; } = 1;
	public List<AdminAccountOptions> Accounts { get; set; } = [];
}

public sealed class AdminAccountOptions
{
	public string DisplayName { get; set; } = string.Empty;
	public string ApiKey { get; set; } = string.Empty;
	public string TotpSecret { get; set; } = string.Empty;
	public List<string> Roles { get; set; } = [];
}
