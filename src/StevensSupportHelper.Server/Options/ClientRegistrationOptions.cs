namespace StevensSupportHelper.Server.Options;

public sealed class ClientRegistrationOptions
{
    public const string SectionName = "StevensSupportHelperClientRegistration";

    public bool RequireSignedRegistration { get; set; } = true;
    public string SharedKey { get; set; } = "change-me-registration-key";
    public int AllowedClockSkewMinutes { get; set; } = 5;
}
