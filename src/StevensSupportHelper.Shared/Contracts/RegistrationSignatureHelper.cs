using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StevensSupportHelper.Shared.Contracts;

public static class RegistrationSignatureHelper
{
    public static string ComputeSignature(RegisterClientRequest request, string sharedKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedKey);

        var payload = string.Join('\n',
            request.DeviceName.Trim(),
            request.MachineName.Trim(),
            request.CurrentUser.Trim(),
            request.HasInteractiveUser ? "1" : "0",
            request.IsAtLogonScreen ? "1" : "0",
            request.ConsentRequired ? "1" : "0",
            request.AutoApproveSupportRequests ? "1" : "0",
            request.TailscaleConnected ? "1" : "0",
            string.Join(',', (request.TailscaleIpAddresses ?? []).Select(static address => address.Trim()).Where(static address => !string.IsNullOrWhiteSpace(address)).OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)),
            string.Join(',', request.SupportedChannels.Select(static channel => channel.ToString()).OrderBy(static value => value, StringComparer.Ordinal)),
            request.RustDeskId?.Trim() ?? string.Empty,
            string.Join(',', (request.DiskUsages ?? [])
                .OrderBy(static disk => disk.DriveName, StringComparer.OrdinalIgnoreCase)
                .Select(static disk => $"{disk.DriveName.Trim()}:{disk.TotalBytes}:{disk.FreeBytes}")),
            request.TotalMemoryBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            request.AvailableMemoryBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            request.OsDescription?.Trim() ?? string.Empty,
            request.LastBootAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            request.RequestedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            request.RegistrationNonce.Trim());

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedKey.Trim()));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(signature);
    }
}
