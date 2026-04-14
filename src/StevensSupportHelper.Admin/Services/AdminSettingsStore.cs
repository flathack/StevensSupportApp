using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin.Services;

public sealed class AdminSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StevensSupportHelper",
        "admin-settings.json");

    public AdminClientSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AdminClientSettings();
        }

        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedAdminSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                ?? new PersistedAdminSettings();
            return new AdminClientSettings
            {
                ServerUrl = persisted.ServerUrl ?? "http://localhost:5000",
                ApiKey = Unprotect(persisted.ProtectedApiKey),
                ServerProjectPath = persisted.ServerProjectPath ?? string.Empty,
                RustDeskPath = persisted.RustDeskPath ?? ResolveDefaultRustDeskPath() ?? string.Empty,
                RustDeskPassword = Unprotect(persisted.ProtectedRustDeskPassword),
                ClientInstallerPath = persisted.ClientInstallerPath ?? string.Empty,
                RemoteActionsPath = persisted.RemoteActionsPath ?? string.Empty,
                PackageGeneratorPath = persisted.PackageGeneratorPath ?? ResolveDefaultPackageGeneratorPath(),
                RemoteUserName = persisted.RemoteUserName ?? string.Empty,
                RemotePassword = Unprotect(persisted.ProtectedRemotePassword),
                ThemeMode = Enum.TryParse<AdminThemeMode>(persisted.ThemeMode, ignoreCase: true, out var themeMode)
                    ? themeMode
                    : AdminThemeMode.Light,
                PreferredChannel = Enum.TryParse<RemoteChannel>(persisted.PreferredChannel, ignoreCase: true, out var channel)
                    ? channel
                    : RemoteChannel.Rdp,
                Reason = string.IsNullOrWhiteSpace(persisted.Reason) ? "Remote support requested." : persisted.Reason
            };
        }
        catch
        {
            return new AdminClientSettings();
        }
    }

    public void Save(AdminClientSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var persisted = new PersistedAdminSettings
        {
            ServerUrl = string.IsNullOrWhiteSpace(settings.ServerUrl) ? "http://localhost:5000" : settings.ServerUrl.Trim(),
            ProtectedApiKey = Protect(settings.ApiKey),
            ServerProjectPath = settings.ServerProjectPath?.Trim(),
            RustDeskPath = settings.RustDeskPath?.Trim(),
            ProtectedRustDeskPassword = Protect(settings.RustDeskPassword),
            ClientInstallerPath = settings.ClientInstallerPath?.Trim(),
            RemoteActionsPath = settings.RemoteActionsPath?.Trim(),
            PackageGeneratorPath = settings.PackageGeneratorPath?.Trim(),
            RemoteUserName = settings.RemoteUserName?.Trim(),
            ProtectedRemotePassword = Protect(settings.RemotePassword),
            ThemeMode = settings.ThemeMode.ToString(),
            PreferredChannel = settings.PreferredChannel.ToString(),
            Reason = string.IsNullOrWhiteSpace(settings.Reason) ? "Remote support requested." : settings.Reason.Trim()
        };

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(persisted, JsonOptions), Encoding.UTF8);
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value.Trim()),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var decryptedBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class PersistedAdminSettings
    {
        public string? ServerUrl { get; init; }
        public string? ProtectedApiKey { get; init; }
        public string? ServerProjectPath { get; init; }
        public string? RustDeskPath { get; init; }
        public string? ProtectedRustDeskPassword { get; init; }
        public string? ClientInstallerPath { get; init; }
        public string? RemoteActionsPath { get; init; }
        public string? PackageGeneratorPath { get; init; }
        public string? RemoteUserName { get; init; }
        public string? ProtectedRemotePassword { get; init; }
        public string? ThemeMode { get; init; }
        public string? PreferredChannel { get; init; }
        public string? Reason { get; init; }
    }

    public static string? ResolveDefaultRustDeskPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(programFiles, "RustDesk", "rustdesk.exe"),
            Path.Combine(localAppData, "Programs", "RustDesk", "rustdesk.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string ResolveDefaultPackageGeneratorPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Github",
            "StevensSupportHelper",
            "publish",
            "PackageGenerator");
    }
}

public sealed class AdminClientSettings
{
    public string ServerUrl { get; init; } = "http://localhost:5000";
    public string ApiKey { get; init; } = string.Empty;
    public string ServerProjectPath { get; init; } = string.Empty;
    public string RustDeskPath { get; init; } = AdminSettingsStore.ResolveDefaultRustDeskPath() ?? string.Empty;
    public string RustDeskPassword { get; init; } = string.Empty;
    public string ClientInstallerPath { get; init; } = string.Empty;
    public string RemoteActionsPath { get; init; } = string.Empty;
    public string PackageGeneratorPath { get; init; } = AdminSettingsStore.ResolveDefaultPackageGeneratorPath();
    public string RemoteUserName { get; init; } = string.Empty;
    public string RemotePassword { get; init; } = string.Empty;
    public AdminThemeMode ThemeMode { get; init; } = AdminThemeMode.Light;
    public RemoteChannel PreferredChannel { get; init; } = RemoteChannel.Rdp;
    public string Reason { get; init; } = "Remote support requested.";
}
