namespace StevensSupportHelper.Server.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "StevensSupportHelperRateLimiting";

    public bool Enabled { get; set; } = true;
    public RateLimitPolicyOptions ClientPolicy { get; set; } = new();
    public RateLimitPolicyOptions AdminReadPolicy { get; set; } = new()
    {
        PermitLimit = 120
    };

    public RateLimitPolicyOptions AdminWritePolicy { get; set; } = new()
    {
        PermitLimit = 30
    };
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 90;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
}
