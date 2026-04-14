namespace StevensSupportHelper.Admin.Models;

public sealed record RepairPrecheckResult(
    string TargetHost,
    string CredentialUserName,
    bool HasCredentials,
    bool IsReachable,
    string Message);
