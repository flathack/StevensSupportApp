using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Server.Services;

public sealed class ClientRegistrationVerifier(IOptions<ClientRegistrationOptions> options)
{
    private readonly ClientRegistrationOptions _options = options.Value;

    public bool Validate(RegisterClientRequest request, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.RequireSignedRegistration)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(_options.SharedKey))
        {
            errorMessage = "Signed client registration is enabled, but no shared key is configured.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RegistrationNonce))
        {
            errorMessage = "Registration nonce is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RegistrationSignature))
        {
            errorMessage = "Registration signature is required.";
            return false;
        }

        var allowedClockSkew = TimeSpan.FromMinutes(Math.Max(1, _options.AllowedClockSkewMinutes));
        var now = DateTimeOffset.UtcNow;
        if (request.RequestedAtUtc < now - allowedClockSkew || request.RequestedAtUtc > now + allowedClockSkew)
        {
            errorMessage = "Registration timestamp is outside the allowed clock skew.";
            return false;
        }

        var expectedSignature = RegistrationSignatureHelper.ComputeSignature(request, _options.SharedKey);
        if (!ConstantTimeEquals(expectedSignature, request.RegistrationSignature.Trim()))
        {
            errorMessage = "Registration signature is invalid.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool ConstantTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
