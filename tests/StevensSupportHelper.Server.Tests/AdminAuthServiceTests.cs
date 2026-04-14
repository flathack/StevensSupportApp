using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Security.Cryptography;
using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;

namespace StevensSupportHelper.Server.Tests;

public sealed class AdminAuthServiceTests
{
    private const string TotpSecret = "JBSWY3DPEHPK3PXP";

    private static AdminAuthService CreateService(params AdminAccountOptions[] accounts)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AdminAuthOptions
        {
            ApiKeyHeaderName = "X-Admin-ApiKey",
            MfaCodeHeaderName = "X-Admin-Totp",
            Accounts = accounts.ToList()
        });
        return new AdminAuthService(options);
    }

    private static HttpRequest CreateRequestWithKey(string? apiKey, string? mfaCode = null)
    {
        var context = new DefaultHttpContext();
        if (apiKey is not null)
        {
            context.Request.Headers["X-Admin-ApiKey"] = apiKey;
        }

        if (mfaCode is not null)
        {
            context.Request.Headers["X-Admin-Totp"] = mfaCode;
        }

        return context.Request;
    }

    private static string GenerateTotpCode(string secret, DateTimeOffset timestampUtc, int timeStepSeconds = 30)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = new string(secret
            .Trim()
            .ToUpperInvariant()
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '-')
            .ToArray());

        var bitBuffer = new List<int>(cleaned.Length * 5);
        foreach (var ch in cleaned)
        {
            var index = alphabet.IndexOf(ch);
            if (index < 0)
            {
                throw new InvalidOperationException("TOTP secret contains invalid Base32 characters.");
            }

            for (var shift = 4; shift >= 0; shift--)
            {
                bitBuffer.Add((index >> shift) & 1);
            }
        }

        var secretBytes = new List<byte>();
        for (var offset = 0; offset + 8 <= bitBuffer.Count; offset += 8)
        {
            byte value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (byte)((value << 1) | bitBuffer[offset + bit]);
            }

            secretBytes.Add(value);
        }

        var counter = timestampUtc.ToUnixTimeSeconds() / Math.Max(15, timeStepSeconds);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(secretBytes.ToArray());
        var hash = hmac.ComputeHash(counterBytes);
        var offsetNibble = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offsetNibble] & 0x7F) << 24) |
            (hash[offsetNibble + 1] << 16) |
            (hash[offsetNibble + 2] << 8) |
            hash[offsetNibble + 3];

        return (binaryCode % 1_000_000).ToString("D6");
    }

    [Fact]
    public void TryAuthenticate_ValidKey_ReturnsTrue()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "test-key-123",
            Roles = ["Administrator"]
        });

        var result = service.TryAuthenticate(CreateRequestWithKey("test-key-123"), out var admin, out _);

        Assert.True(result);
        Assert.Equal("TestAdmin", admin.DisplayName);
    }

    [Fact]
    public void TryAuthenticate_InvalidKey_ReturnsFalse()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "test-key-123",
            Roles = ["Administrator"]
        });

        var result = service.TryAuthenticate(CreateRequestWithKey("wrong-key"), out _, out var error);

        Assert.False(result);
        Assert.Contains("invalid", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryAuthenticate_MissingHeader_ReturnsFalse()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "test-key-123",
            Roles = ["Administrator"]
        });

        var result = service.TryAuthenticate(CreateRequestWithKey(null), out _, out var error);

        Assert.False(result);
        Assert.Contains("Missing", error);
    }

    [Fact]
    public void TryAuthenticate_ValidKeyAndMfa_ReturnsTrue()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "test-key-123",
            TotpSecret = TotpSecret,
            Roles = ["Administrator"]
        });

        var code = GenerateTotpCode(TotpSecret, DateTimeOffset.UtcNow);

        var result = service.TryAuthenticate(CreateRequestWithKey("test-key-123", code), out var admin, out _);

        Assert.True(result);
        Assert.Equal("TestAdmin", admin.DisplayName);
    }

    [Fact]
    public void TryAuthenticate_MissingMfaHeader_ReturnsFalse()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "test-key-123",
            TotpSecret = TotpSecret,
            Roles = ["Administrator"]
        });

        var result = service.TryAuthenticate(CreateRequestWithKey("test-key-123"), out _, out var error);

        Assert.False(result);
        Assert.Contains("MFA", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryAuthenticate_InvalidMfaCode_ReturnsFalse()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "test-key-123",
            TotpSecret = TotpSecret,
            Roles = ["Administrator"]
        });

        var result = service.TryAuthenticate(CreateRequestWithKey("test-key-123", "000000"), out _, out var error);

        Assert.False(result);
        Assert.Contains("invalid", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasAnyRole_AdminHasRole_ReturnsTrue()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "TestAdmin",
            ApiKey = "key",
            Roles = ["Operator", "Auditor"]
        });
        service.TryAuthenticate(CreateRequestWithKey("key"), out var admin, out _);

        Assert.True(service.HasAnyRole(admin, AdminRole.Operator));
        Assert.True(service.HasAnyRole(admin, AdminRole.Auditor));
    }

    [Fact]
    public void HasAnyRole_AdminMissingRole_ReturnsFalse()
    {
        var service = CreateService(new AdminAccountOptions
        {
            DisplayName = "Auditor",
            ApiKey = "key",
            Roles = ["Auditor"]
        });
        service.TryAuthenticate(CreateRequestWithKey("key"), out var admin, out _);

        Assert.False(service.HasAnyRole(admin, AdminRole.Administrator));
    }
}
