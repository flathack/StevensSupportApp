using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StevensSupportHelper.Server.Options;

namespace StevensSupportHelper.Server.Services;

public sealed class AdminAuthService
{
	private readonly Dictionary<string, AuthenticatedAdmin> _accounts;
	private readonly string _mfaHeaderName;
	private readonly int _totpTimeStepSeconds;
	private readonly int _totpAllowedDriftSteps;

	public AdminAuthService(IOptions<AdminAuthOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var configuredOptions = options.Value;
		HeaderName = string.IsNullOrWhiteSpace(configuredOptions.ApiKeyHeaderName)
			? "X-Admin-ApiKey"
			: configuredOptions.ApiKeyHeaderName.Trim();
		_mfaHeaderName = string.IsNullOrWhiteSpace(configuredOptions.MfaCodeHeaderName)
			? "X-Admin-Totp"
			: configuredOptions.MfaCodeHeaderName.Trim();
		_totpTimeStepSeconds = Math.Max(15, configuredOptions.TotpTimeStepSeconds);
		_totpAllowedDriftSteps = Math.Max(0, configuredOptions.TotpAllowedDriftSteps);

		_accounts = new Dictionary<string, AuthenticatedAdmin>(StringComparer.Ordinal);
		foreach (var account in configuredOptions.Accounts)
		{
			if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.DisplayName))
			{
				continue;
			}

			var roles = account.Roles
				.Select(ParseRole)
				.Distinct()
				.ToArray();
			if (roles.Length == 0)
			{
				continue;
			}

			_accounts[account.ApiKey.Trim()] = new AuthenticatedAdmin(account.DisplayName.Trim(), roles, account.TotpSecret.Trim());
		}
	}

	public string HeaderName { get; }

	public bool TryAuthenticate(HttpRequest request, out AuthenticatedAdmin admin, out string errorMessage)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (!request.Headers.TryGetValue(HeaderName, out var values))
		{
			admin = AuthenticatedAdmin.Empty;
			errorMessage = $"Missing admin API key header '{HeaderName}'.";
			return false;
		}

		var apiKey = values.ToString().Trim();
		if (string.IsNullOrWhiteSpace(apiKey) || !_accounts.TryGetValue(apiKey, out var authenticatedAdmin))
		{
			admin = AuthenticatedAdmin.Empty;
			errorMessage = "The supplied admin API key is invalid.";
			return false;
		}

		if (!string.IsNullOrWhiteSpace(authenticatedAdmin.TotpSecret))
		{
			if (!request.Headers.TryGetValue(_mfaHeaderName, out var mfaValues))
			{
				admin = AuthenticatedAdmin.Empty;
				errorMessage = $"Missing admin MFA header '{_mfaHeaderName}'.";
				return false;
			}

			var suppliedCode = mfaValues.ToString().Trim();
			if (!IsValidTotpCode(authenticatedAdmin.TotpSecret, suppliedCode, DateTimeOffset.UtcNow))
			{
				admin = AuthenticatedAdmin.Empty;
				errorMessage = "The supplied admin MFA code is invalid.";
				return false;
			}
		}

		admin = authenticatedAdmin;
		errorMessage = string.Empty;
		return true;
	}

	public bool HasAnyRole(AuthenticatedAdmin admin, params AdminRole[] requiredRoles)
	{
		ArgumentNullException.ThrowIfNull(admin);

		return requiredRoles.Length == 0 || requiredRoles.Any(admin.HasRole);
	}

	private static AdminRole ParseRole(string role)
	{
		return Enum.TryParse<AdminRole>(role, ignoreCase: true, out var parsedRole)
			? parsedRole
			: AdminRole.Auditor;
	}

	private bool IsValidTotpCode(string secret, string suppliedCode, DateTimeOffset nowUtc)
	{
		if (string.IsNullOrWhiteSpace(suppliedCode) || suppliedCode.Length != 6 || !suppliedCode.All(char.IsDigit))
		{
			return false;
		}

		for (var drift = -_totpAllowedDriftSteps; drift <= _totpAllowedDriftSteps; drift++)
		{
			var candidate = GenerateTotpCode(secret, nowUtc.AddSeconds(drift * _totpTimeStepSeconds));
			if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(suppliedCode)))
			{
				return true;
			}
		}

		return false;
	}

	internal static string GenerateTotpCode(string secret, DateTimeOffset timestampUtc, int timeStepSeconds = 30)
	{
		var normalizedSecret = NormalizeBase32(secret);
		var secretBytes = Convert.FromBase64String(PadBase64(normalizedSecret));
		var counter = timestampUtc.ToUnixTimeSeconds() / Math.Max(15, timeStepSeconds);
		var counterBytes = BitConverter.GetBytes(counter);
		if (BitConverter.IsLittleEndian)
		{
			Array.Reverse(counterBytes);
		}

		using var hmac = new HMACSHA1(secretBytes);
		var hash = hmac.ComputeHash(counterBytes);
		var offset = hash[^1] & 0x0F;
		var binaryCode =
			((hash[offset] & 0x7F) << 24) |
			(hash[offset + 1] << 16) |
			(hash[offset + 2] << 8) |
			hash[offset + 3];

		return (binaryCode % 1_000_000).ToString("D6");
	}

	private static string NormalizeBase32(string input)
	{
		const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
		var cleaned = new string(input
			.Trim()
			.ToUpperInvariant()
			.Where(ch => !char.IsWhiteSpace(ch) && ch != '-')
			.ToArray());

		var bits = new StringBuilder();
		foreach (var ch in cleaned)
		{
			var index = alphabet.IndexOf(ch);
			if (index < 0)
			{
				throw new InvalidOperationException("TOTP secret contains invalid Base32 characters.");
			}

			bits.Append(Convert.ToString(index, 2).PadLeft(5, '0'));
		}

		var bytes = new List<byte>();
		for (var offset = 0; offset + 8 <= bits.Length; offset += 8)
		{
			bytes.Add(Convert.ToByte(bits.ToString(offset, 8), 2));
		}

		return Convert.ToBase64String(bytes.ToArray());
	}

	private static string PadBase64(string value)
	{
		var padding = value.Length % 4;
		return padding == 0 ? value : value.PadRight(value.Length + (4 - padding), '=');
	}
}

public sealed record AuthenticatedAdmin(string DisplayName, IReadOnlyList<AdminRole> Roles, string TotpSecret)
{
	public static AuthenticatedAdmin Empty { get; } = new(string.Empty, [], string.Empty);

	public bool HasRole(AdminRole role)
	{
		return Roles.Contains(role);
	}
}

public enum AdminRole
{
	Auditor,
	Operator,
	Administrator
}
