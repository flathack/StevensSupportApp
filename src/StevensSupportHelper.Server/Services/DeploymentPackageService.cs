using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StevensSupportHelper.Server.Services;

public sealed class DeploymentPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ServerStateStore _stateStore;
    private readonly string _assetsRootPath;

    public DeploymentPackageService(ServerStateStore stateStore)
    {
        _stateStore = stateStore;
        _assetsRootPath = Path.Combine(stateStore.StorageRootPath, "deployment-assets");
        Directory.CreateDirectory(_assetsRootPath);
    }

    public DeploymentSettingsSnapshot GetSnapshot()
    {
        var state = _stateStore.Load();
        return new DeploymentSettingsSnapshot(
            CloneSettings(state.DeploymentSettings),
            state.DeploymentAssets
                .OrderBy(asset => asset.Kind, StringComparer.OrdinalIgnoreCase)
                .Select(CloneAsset)
                .ToList(),
            state.DeploymentProfiles
                .OrderBy(profile => profile.CustomerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
                .Select(CloneProfile)
                .ToList());
    }

    public PersistedDeploymentSettings SaveSettings(PersistedDeploymentSettings settings)
    {
        var state = _stateStore.Load();
        state.DeploymentSettings = NormalizeSettings(settings);
        _stateStore.Save(state);
        return CloneSettings(state.DeploymentSettings);
    }

    public PersistedDeploymentAsset SaveAsset(string kind, string fileName, string contentType, Stream contentStream)
    {
        var normalizedKind = NormalizeAssetKind(kind);
        var safeFileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException("Für den Upload wird ein Dateiname benötigt.");
        }

        var extension = Path.GetExtension(safeFileName);
        var storedFileName = $"{normalizedKind}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var assetPath = Path.Combine(_assetsRootPath, storedFileName);

        string sha256;
        long fileSize;
        using (var output = File.Create(assetPath))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            int bytesRead;
            long totalBytes = 0;
            while ((bytesRead = contentStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, bytesRead);
                hasher.AppendData(buffer, 0, bytesRead);
                totalBytes += bytesRead;
            }

            fileSize = totalBytes;
            sha256 = Convert.ToHexString(hasher.GetHashAndReset());
        }

        var state = _stateStore.Load();
        var existingAsset = state.DeploymentAssets.FirstOrDefault(asset => string.Equals(asset.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase));
        if (existingAsset is not null)
        {
            DeleteAssetFileIfExists(existingAsset.StoredFileName);
            state.DeploymentAssets.Remove(existingAsset);
        }

        var assetRecord = new PersistedDeploymentAsset
        {
            Id = Guid.NewGuid(),
            Kind = normalizedKind,
            OriginalFileName = safeFileName,
            StoredFileName = storedFileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = fileSize,
            Sha256 = sha256,
            UploadedAtUtc = DateTimeOffset.UtcNow
        };

        state.DeploymentAssets.Add(assetRecord);
        _stateStore.Save(state);
        return CloneAsset(assetRecord);
    }

    public IReadOnlyList<PersistedDeploymentProfile> GetProfiles()
    {
        var state = _stateStore.Load();
        return state.DeploymentProfiles
            .OrderBy(profile => profile.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(CloneProfile)
            .ToList();
    }

    public PersistedDeploymentProfile? GetProfile(Guid profileId)
    {
        var state = _stateStore.Load();
        var profile = state.DeploymentProfiles.FirstOrDefault(entry => entry.Id == profileId);
        return profile is null ? null : CloneProfile(profile);
    }

    public PersistedDeploymentProfile SaveProfile(PersistedDeploymentProfile profile)
    {
        var state = _stateStore.Load();
        var normalized = NormalizeProfile(profile, state.DeploymentSettings, state.DeploymentAssets);
        var existingIndex = state.DeploymentProfiles.FindIndex(entry => entry.Id == normalized.Id);
        if (existingIndex >= 0)
        {
            normalized.CreatedAtUtc = state.DeploymentProfiles[existingIndex].CreatedAtUtc;
            state.DeploymentProfiles[existingIndex] = normalized;
        }
        else
        {
            normalized.CreatedAtUtc = DateTimeOffset.UtcNow;
            state.DeploymentProfiles.Add(normalized);
        }

        _stateStore.Save(state);
        return CloneProfile(normalized);
    }

    public bool DeleteProfile(Guid profileId)
    {
        var state = _stateStore.Load();
        var removedCount = state.DeploymentProfiles.RemoveAll(profile => profile.Id == profileId);
        if (removedCount == 0)
        {
            return false;
        }

        _stateStore.Save(state);
        return true;
    }

    public string BuildProfileConfig(Guid profileId)
    {
        var state = _stateStore.Load();
        var profile = state.DeploymentProfiles.FirstOrDefault(entry => entry.Id == profileId)
            ?? throw new KeyNotFoundException("Das Kundenprofil wurde nicht gefunden.");
        return BuildConfigText(profile, state.DeploymentAssets);
    }

    public GeneratedPackageResult ExportPackage(Guid profileId)
    {
        var state = _stateStore.Load();
        var profile = state.DeploymentProfiles.FirstOrDefault(entry => entry.Id == profileId)
            ?? throw new KeyNotFoundException("Das Kundenprofil wurde nicht gefunden.");

        var clientInstaller = ResolveRequiredAsset(state.DeploymentAssets, DeploymentAssetKinds.ClientInstaller, "Client-Installer");
        PersistedDeploymentAsset? rustDeskInstaller = null;
        PersistedDeploymentAsset? tailscaleInstaller = null;

        if (profile.InstallRustDesk)
        {
            rustDeskInstaller = ResolveRequiredAsset(state.DeploymentAssets, DeploymentAssetKinds.RustDeskInstaller, "RustDesk-Installer");
        }

        if (profile.InstallTailscale)
        {
            tailscaleInstaller = ResolveRequiredAsset(state.DeploymentAssets, DeploymentAssetKinds.TailscaleInstaller, "Tailscale-Installer");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "StevensSupportHelper", "package-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            CopyAssetInto(tempRoot, clientInstaller);
            if (rustDeskInstaller is not null)
            {
                CopyAssetInto(tempRoot, rustDeskInstaller);
            }

            if (tailscaleInstaller is not null)
            {
                CopyAssetInto(tempRoot, tailscaleInstaller);
            }

            var configPath = Path.Combine(tempRoot, "client.installer.config");
            File.WriteAllText(configPath, BuildConfigText(profile, state.DeploymentAssets), new UTF8Encoding(false));

            var archiveFileName = $"{BuildArchiveSlug(profile)}.zip";
            var archivePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            ZipFile.CreateFromDirectory(tempRoot, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            var bytes = File.ReadAllBytes(archivePath);
            File.Delete(archivePath);

            return new GeneratedPackageResult(archiveFileName, "application/zip", bytes);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private string BuildConfigText(PersistedDeploymentProfile profile, IReadOnlyList<PersistedDeploymentAsset> assets)
    {
        var rustDeskInstaller = FindAsset(assets, DeploymentAssetKinds.RustDeskInstaller);
        var tailscaleInstaller = FindAsset(assets, DeploymentAssetKinds.TailscaleInstaller);

        var payload = new DeploymentClientInstallerConfig
        {
            ServerUrl = profile.ServerUrl,
            InstallRoot = profile.InstallRoot,
            ServiceName = profile.ServiceName,
            DeviceName = profile.DeviceName,
            RegistrationSharedKey = profile.RegistrationSharedKey,
            InstallRustDesk = profile.InstallRustDesk,
            RustDeskInstallerFileName = profile.InstallRustDesk ? rustDeskInstaller?.OriginalFileName ?? string.Empty : string.Empty,
            InstallTailscale = profile.InstallTailscale,
            TailscaleInstallerFileName = profile.InstallTailscale ? tailscaleInstaller?.OriginalFileName ?? string.Empty : string.Empty,
            TailscaleAuthKey = profile.TailscaleAuthKey,
            EnableAutoApprove = profile.EnableAutoApprove,
            EnableRdp = profile.EnableRdp,
            CreateServiceUser = profile.CreateServiceUser,
            ServiceUserIsAdministrator = profile.ServiceUserIsAdministrator,
            ServiceUserName = profile.ServiceUserName,
            ServiceUserPassword = profile.ServiceUserPassword,
            RustDeskId = profile.RustDeskId,
            RustDeskPassword = profile.RustDeskPassword,
            TailscaleIpAddresses = profile.TailscaleIpAddresses,
            Silent = profile.Silent
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static PersistedDeploymentSettings NormalizeSettings(PersistedDeploymentSettings settings)
    {
        return new PersistedDeploymentSettings
        {
            ServerUrl = string.IsNullOrWhiteSpace(settings.ServerUrl) ? "http://localhost:5000" : settings.ServerUrl.Trim(),
            ApiKey = settings.ApiKey?.Trim() ?? string.Empty,
            ServerProjectPath = settings.ServerProjectPath?.Trim() ?? string.Empty,
            RustDeskPath = settings.RustDeskPath?.Trim() ?? string.Empty,
            RustDeskPassword = settings.RustDeskPassword?.Trim() ?? string.Empty,
            ClientInstallerPath = settings.ClientInstallerPath?.Trim() ?? string.Empty,
            RemoteActionsPath = settings.RemoteActionsPath?.Trim() ?? string.Empty,
            PackageGeneratorPath = settings.PackageGeneratorPath?.Trim() ?? string.Empty,
            RemoteUserName = settings.RemoteUserName?.Trim() ?? string.Empty,
            RemotePassword = settings.RemotePassword?.Trim() ?? string.Empty,
            PreferredChannel = string.IsNullOrWhiteSpace(settings.PreferredChannel) ? "Rdp" : settings.PreferredChannel.Trim(),
            Reason = string.IsNullOrWhiteSpace(settings.Reason) ? "Remote support requested." : settings.Reason.Trim(),
            DefaultRegistrationSharedKey = settings.DefaultRegistrationSharedKey?.Trim() ?? string.Empty,
            DefaultInstallRoot = string.IsNullOrWhiteSpace(settings.DefaultInstallRoot) ? @"C:\Program Files\StevensSupportHelper" : settings.DefaultInstallRoot.Trim(),
            DefaultServiceName = string.IsNullOrWhiteSpace(settings.DefaultServiceName) ? "StevensSupportHelperClientService" : settings.DefaultServiceName.Trim()
        };
    }

    private static PersistedDeploymentProfile NormalizeProfile(
        PersistedDeploymentProfile profile,
        PersistedDeploymentSettings settings,
        IReadOnlyList<PersistedDeploymentAsset> assets)
    {
        var normalizedId = profile.Id == Guid.Empty ? Guid.NewGuid() : profile.Id;
        var normalizedCustomerName = string.IsNullOrWhiteSpace(profile.CustomerName) ? "Neuer Kunde" : profile.CustomerName.Trim();
        var normalizedDeviceName = string.IsNullOrWhiteSpace(profile.DeviceName) ? $"{normalizedCustomerName}-PC" : profile.DeviceName.Trim();
        var normalizedRegistrationKey = string.IsNullOrWhiteSpace(profile.RegistrationSharedKey)
            ? settings.DefaultRegistrationSharedKey
            : profile.RegistrationSharedKey.Trim();

        return new PersistedDeploymentProfile
        {
            Id = normalizedId,
            CustomerName = normalizedCustomerName,
            DeviceName = normalizedDeviceName,
            Notes = profile.Notes?.Trim() ?? string.Empty,
            ServerUrl = string.IsNullOrWhiteSpace(profile.ServerUrl) ? settings.ServerUrl : profile.ServerUrl.Trim(),
            RegistrationSharedKey = normalizedRegistrationKey,
            InstallRoot = string.IsNullOrWhiteSpace(profile.InstallRoot) ? settings.DefaultInstallRoot : profile.InstallRoot.Trim(),
            ServiceName = string.IsNullOrWhiteSpace(profile.ServiceName) ? settings.DefaultServiceName : profile.ServiceName.Trim(),
            InstallRustDesk = profile.InstallRustDesk,
            InstallTailscale = profile.InstallTailscale,
            TailscaleAuthKey = profile.TailscaleAuthKey?.Trim() ?? string.Empty,
            EnableAutoApprove = profile.EnableAutoApprove,
            EnableRdp = profile.EnableRdp,
            CreateServiceUser = profile.CreateServiceUser,
            ServiceUserIsAdministrator = profile.ServiceUserIsAdministrator,
            ServiceUserName = profile.ServiceUserName?.Trim() ?? string.Empty,
            ServiceUserPassword = profile.ServiceUserPassword?.Trim() ?? string.Empty,
            RustDeskId = profile.RustDeskId?.Trim() ?? string.Empty,
            RustDeskPassword = string.IsNullOrWhiteSpace(profile.RustDeskPassword) ? settings.RustDeskPassword : profile.RustDeskPassword.Trim(),
            TailscaleIpAddresses = profile.TailscaleIpAddresses
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Select(address => address.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Silent = profile.Silent,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = profile.CreatedAtUtc
        };
    }

    private static string NormalizeAssetKind(string kind)
    {
        if (string.Equals(kind, DeploymentAssetKinds.ClientInstaller, StringComparison.OrdinalIgnoreCase))
        {
            return DeploymentAssetKinds.ClientInstaller;
        }

        if (string.Equals(kind, DeploymentAssetKinds.RustDeskInstaller, StringComparison.OrdinalIgnoreCase))
        {
            return DeploymentAssetKinds.RustDeskInstaller;
        }

        if (string.Equals(kind, DeploymentAssetKinds.TailscaleInstaller, StringComparison.OrdinalIgnoreCase))
        {
            return DeploymentAssetKinds.TailscaleInstaller;
        }

        throw new InvalidOperationException($"Unbekannter Asset-Typ: {kind}");
    }

    private static PersistedDeploymentAsset ResolveRequiredAsset(IEnumerable<PersistedDeploymentAsset> assets, string kind, string label)
    {
        return FindAsset(assets, kind) ?? throw new InvalidOperationException($"{label} wurde noch nicht hochgeladen.");
    }

    private static PersistedDeploymentAsset? FindAsset(IEnumerable<PersistedDeploymentAsset> assets, string kind)
    {
        return assets.FirstOrDefault(asset => string.Equals(asset.Kind, kind, StringComparison.OrdinalIgnoreCase));
    }

    private void CopyAssetInto(string destinationRoot, PersistedDeploymentAsset asset)
    {
        var sourcePath = Path.Combine(_assetsRootPath, asset.StoredFileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Die gespeicherte Datei für {asset.Kind} wurde nicht gefunden.", sourcePath);
        }

        var destinationPath = Path.Combine(destinationRoot, asset.OriginalFileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private void DeleteAssetFileIfExists(string storedFileName)
    {
        var assetPath = Path.Combine(_assetsRootPath, storedFileName);
        if (File.Exists(assetPath))
        {
            File.Delete(assetPath);
        }
    }

    private static string BuildArchiveSlug(PersistedDeploymentProfile profile)
    {
        var source = $"{profile.CustomerName}-{profile.DeviceName}";
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return $"StevensSupportHelper-{builder.ToString().Trim('-')}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName ?? string.Empty).Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidCharacter, '-');
        }

        return safeName;
    }

    private static PersistedDeploymentSettings CloneSettings(PersistedDeploymentSettings settings)
    {
        return new PersistedDeploymentSettings
        {
            ServerUrl = settings.ServerUrl,
            ApiKey = settings.ApiKey,
            ServerProjectPath = settings.ServerProjectPath,
            RustDeskPath = settings.RustDeskPath,
            RustDeskPassword = settings.RustDeskPassword,
            ClientInstallerPath = settings.ClientInstallerPath,
            RemoteActionsPath = settings.RemoteActionsPath,
            PackageGeneratorPath = settings.PackageGeneratorPath,
            RemoteUserName = settings.RemoteUserName,
            RemotePassword = settings.RemotePassword,
            PreferredChannel = settings.PreferredChannel,
            Reason = settings.Reason,
            DefaultRegistrationSharedKey = settings.DefaultRegistrationSharedKey,
            DefaultInstallRoot = settings.DefaultInstallRoot,
            DefaultServiceName = settings.DefaultServiceName
        };
    }

    private static PersistedDeploymentProfile CloneProfile(PersistedDeploymentProfile profile)
    {
        return new PersistedDeploymentProfile
        {
            Id = profile.Id,
            CustomerName = profile.CustomerName,
            DeviceName = profile.DeviceName,
            Notes = profile.Notes,
            ServerUrl = profile.ServerUrl,
            RegistrationSharedKey = profile.RegistrationSharedKey,
            InstallRoot = profile.InstallRoot,
            ServiceName = profile.ServiceName,
            InstallRustDesk = profile.InstallRustDesk,
            InstallTailscale = profile.InstallTailscale,
            TailscaleAuthKey = profile.TailscaleAuthKey,
            EnableAutoApprove = profile.EnableAutoApprove,
            EnableRdp = profile.EnableRdp,
            CreateServiceUser = profile.CreateServiceUser,
            ServiceUserIsAdministrator = profile.ServiceUserIsAdministrator,
            ServiceUserName = profile.ServiceUserName,
            ServiceUserPassword = profile.ServiceUserPassword,
            RustDeskId = profile.RustDeskId,
            RustDeskPassword = profile.RustDeskPassword,
            TailscaleIpAddresses = profile.TailscaleIpAddresses.ToList(),
            Silent = profile.Silent,
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = profile.UpdatedAtUtc
        };
    }

    private static PersistedDeploymentAsset CloneAsset(PersistedDeploymentAsset asset)
    {
        return new PersistedDeploymentAsset
        {
            Id = asset.Id,
            Kind = asset.Kind,
            OriginalFileName = asset.OriginalFileName,
            StoredFileName = asset.StoredFileName,
            ContentType = asset.ContentType,
            FileSizeBytes = asset.FileSizeBytes,
            Sha256 = asset.Sha256,
            UploadedAtUtc = asset.UploadedAtUtc
        };
    }
}

public sealed record DeploymentSettingsSnapshot(
    PersistedDeploymentSettings Settings,
    IReadOnlyList<PersistedDeploymentAsset> Assets,
    IReadOnlyList<PersistedDeploymentProfile> Profiles);

public sealed record GeneratedPackageResult(
    string FileName,
    string ContentType,
    byte[] Content);

public static class DeploymentAssetKinds
{
    public const string ClientInstaller = "client-installer";
    public const string RustDeskInstaller = "rustdesk-installer";
    public const string TailscaleInstaller = "tailscale-installer";
}

public sealed class DeploymentClientInstallerConfig
{
    public string ServerUrl { get; init; } = string.Empty;
    public string InstallRoot { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string RegistrationSharedKey { get; init; } = string.Empty;
    public bool InstallRustDesk { get; init; }
    public string RustDeskInstallerFileName { get; init; } = string.Empty;
    public bool InstallTailscale { get; init; }
    public string TailscaleInstallerFileName { get; init; } = string.Empty;
    public string TailscaleAuthKey { get; init; } = string.Empty;
    public bool EnableAutoApprove { get; init; }
    public bool EnableRdp { get; init; }
    public bool CreateServiceUser { get; init; }
    public bool ServiceUserIsAdministrator { get; init; }
    public string ServiceUserName { get; init; } = string.Empty;
    public string ServiceUserPassword { get; init; } = string.Empty;
    public string RustDeskId { get; init; } = string.Empty;
    public string RustDeskPassword { get; init; } = string.Empty;
    public IReadOnlyList<string> TailscaleIpAddresses { get; init; } = [];
    public bool Silent { get; init; }
}
