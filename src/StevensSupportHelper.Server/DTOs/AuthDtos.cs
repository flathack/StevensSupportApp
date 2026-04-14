namespace StevensSupportHelper.Server.DTOs;

public sealed record RegisterRequest(
    string Username,
    string Password,
    string? DisplayName = null,
    List<string>? Roles = null);

public sealed record RegisterResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string Message);

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserInfoResponse User);

public sealed record RefreshTokenRequest(
    string RefreshToken);

public sealed record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);

public sealed record ChangePasswordRequest(
    string OldPassword,
    string NewPassword);

public sealed record ResetPasswordRequest(
    string NewPassword);

public sealed record UserInfoResponse(
    Guid Id,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool IsMfaEnabled,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UpdateUserRolesRequest(
    List<string> Roles);

public sealed record UpdateMfaRequest(
    string? Secret,
    bool Enabled);

public sealed record ApiResponse(
    bool Success,
    string Message);