using System.Security.Cryptography;
using System.Text;

namespace StevensSupportHelper.Server.Services;

public sealed class UserService
{
    private readonly ServerStateStore _stateStore;
    private List<PersistedUserRecord>? _cachedUsers;

    public UserService(ServerStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public PersistedUserRecord? GetUserByUsername(string username)
    {
        var users = GetAllUsers();
        return users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public PersistedUserRecord? GetUserById(Guid id)
    {
        var users = GetAllUsers();
        return users.FirstOrDefault(u => u.Id == id);
    }

    public IReadOnlyList<PersistedUserRecord> GetAllUsers()
    {
        if (_cachedUsers is not null)
        {
            return _cachedUsers;
        }

        var state = _stateStore.Load();
        _cachedUsers = state.Users?.ToList() ?? [];
        return _cachedUsers;
    }

    public (PersistedUserRecord? User, string Error) CreateUser(string username, string password, string displayName, List<string> roles)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (null, "Username and password are required.");
        }

        var existingUser = GetUserByUsername(username);
        if (existingUser is not null)
        {
            return (null, "Username already exists.");
        }

        var passwordHash = HashPassword(password);
        var user = new PersistedUserRecord
        {
            Username = username.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            Roles = roles.Count > 0 ? roles : ["Operator"],
            IsActive = true
        };

        var state = _stateStore.Load();
        var users = state.Users?.ToList() ?? [];
        users.Add(user);
        state.Users = users;
        _stateStore.Save(state);
        _cachedUsers = null;

        return (user, string.Empty);
    }

    public (PersistedUserRecord? User, bool Created, string Error) EnsureBootstrapUser(string username, string password, string displayName, List<string> roles)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (null, false, "Username and password are required.");
        }

        var normalizedUsername = username.Trim().ToLowerInvariant();
        var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim();
        var effectiveRoles = roles.Count > 0 ? roles : ["Administrator", "Operator", "Auditor"];

        var existingUser = GetUserByUsername(normalizedUsername);
        if (existingUser is null)
        {
            var (createdUser, error) = CreateUser(normalizedUsername, password, effectiveDisplayName, effectiveRoles);
            return (createdUser, createdUser is not null, error);
        }

        var newHash = HashPassword(password);
        var updateResult = UpdateUser(existingUser.Id, user =>
        {
            user.Username = normalizedUsername;
            user.PasswordHash = newHash;
            user.DisplayName = effectiveDisplayName;
            user.Roles = effectiveRoles;
            user.IsActive = true;
        });

        if (!updateResult.Success)
        {
            return (null, false, updateResult.Error);
        }

        return (GetUserById(existingUser.Id), false, string.Empty);
    }

    public (bool Success, string Error) UpdateUserPassword(Guid userId, string oldPassword, string newPassword)
    {
        var user = GetUserById(userId);
        if (user is null)
        {
            return (false, "User not found.");
        }

        if (!VerifyPassword(oldPassword, user.PasswordHash))
        {
            return (false, "Invalid old password.");
        }

        var newHash = HashPassword(newPassword);
        return UpdateUser(userId, u => u.PasswordHash = newHash);
    }

    public (bool Success, string Error) ResetUserPassword(Guid userId, string newPassword)
    {
        var user = GetUserById(userId);
        if (user is null)
        {
            return (false, "User not found.");
        }

        var newHash = HashPassword(newPassword);
        return UpdateUser(userId, u => u.PasswordHash = newHash);
    }

    public (bool Success, string Error) UpdateUserMfa(Guid userId, string? totpSecret, bool enabled)
    {
        return UpdateUser(userId, u =>
        {
            u.TotpSecret = totpSecret;
            u.IsMfaEnabled = enabled;
        });
    }

    public (bool Success, string Error) UpdateUserRoles(Guid userId, List<string> roles)
    {
        return UpdateUser(userId, u => u.Roles = roles);
    }

    public (bool Success, string Error) UpdateUserLastLogin(Guid userId)
    {
        return UpdateUser(userId, u => u.LastLoginAtUtc = DateTimeOffset.UtcNow);
    }

    public (bool Success, string Error) DeleteUser(Guid userId)
    {
        var state = _stateStore.Load();
        var users = state.Users?.ToList() ?? [];
        var removed = users.RemoveAll(u => u.Id == userId);
        if (removed == 0)
        {
            return (false, "User not found.");
        }

        state.Users = users;
        _stateStore.Save(state);
        _cachedUsers = null;
        return (true, string.Empty);
    }

    public bool ValidateCredentials(string username, string password)
    {
        var user = GetUserByUsername(username);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        return VerifyPassword(password, user.PasswordHash);
    }

    private (bool Success, string Error) UpdateUser(Guid userId, Action<PersistedUserRecord> update)
    {
        var state = _stateStore.Load();
        var users = state.Users?.ToList() ?? [];
        var index = users.FindIndex(u => u.Id == userId);
        if (index < 0)
        {
            return (false, "User not found.");
        }

        update(users[index]);
        state.Users = users;
        _stateStore.Save(state);
        _cachedUsers = null;
        return (true, string.Empty);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        var result = new byte[salt.Length + hash.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(hash, 0, result, salt.Length, hash.Length);
        return Convert.ToBase64String(result);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var hashBytes = Convert.FromBase64String(storedHash);
            if (hashBytes.Length < 16)
            {
                return false;
            }

            var salt = new byte[16];
            var storedKey = new byte[hashBytes.Length - 16];
            Buffer.BlockCopy(hashBytes, 0, salt, 0, 16);
            Buffer.BlockCopy(hashBytes, 16, storedKey, 0, storedKey.Length);

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                100_000,
                HashAlgorithmName.SHA256,
                32);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedKey);
        }
        catch
        {
            return false;
        }
    }
}
