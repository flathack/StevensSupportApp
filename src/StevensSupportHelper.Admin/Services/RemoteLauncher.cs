using System.Diagnostics;
using System.IO;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin.Services;

public sealed class RemoteLauncher
{
    private readonly string? _configuredRustDeskPath;
    private readonly string? _configuredRustDeskPassword;

    public RemoteLauncher(string? configuredRustDeskPath = null, string? configuredRustDeskPassword = null)
    {
        _configuredRustDeskPath = string.IsNullOrWhiteSpace(configuredRustDeskPath) ? null : configuredRustDeskPath.Trim();
        _configuredRustDeskPassword = string.IsNullOrWhiteSpace(configuredRustDeskPassword) ? null : configuredRustDeskPassword.Trim();
    }

    public RemoteLaunchCheck Check(ClientRow client, RemoteChannel? launchChannel = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var requestedChannel = launchChannel ?? client.ActiveChannel ?? RemoteChannel.RustDesk;

        if (string.IsNullOrWhiteSpace(client.MachineName))
        {
            return RemoteLaunchCheck.Error("Selected client has no machine name for remote launch.");
        }

        if (requestedChannel == RemoteChannel.RustDesk)
        {
            var supportsRustDesk = client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(RemoteChannel.RustDesk);
            if (!supportsRustDesk && !CanLaunchRustDesk(client))
            {
                return RemoteLaunchCheck.Error("Selected client does not support RustDesk and has no RustDesk target configured.");
            }

            return BuildRustDeskCheck(client);
        }

        if (!client.IsOnline)
        {
            return RemoteLaunchCheck.Error($"{client.DeviceName} is currently offline.");
        }

        if (client.IsAtLogonScreen)
        {
            return requestedChannel switch
            {
                RemoteChannel.Rdp => BuildRdpCheck(client),
                RemoteChannel.WinRm => BuildWinRmCheck(client),
                _ => RemoteLaunchCheck.Error($"Remote channel {requestedChannel} is not available at the login screen.")
            };
        }

        if (client.ActiveChannel is null)
        {
            return RemoteLaunchCheck.Error("Selected client has no active approved session.");
        }

        if (!client.HasLaunchableActiveSession)
        {
            return RemoteLaunchCheck.Error($"Selected client session is not launchable. Current session state: {client.ActiveSessionStatus}.");
        }

        var supportsRequestedChannel = client.SupportedChannels.Count == 0 || client.SupportedChannels.Contains(requestedChannel);
        if (!supportsRequestedChannel && !(requestedChannel == RemoteChannel.RustDesk && CanLaunchRustDesk(client)))
        {
            return RemoteLaunchCheck.Error($"Selected client does not support the requested channel {requestedChannel}.");
        }

        switch (requestedChannel)
        {
            case RemoteChannel.Rdp:
                return BuildRdpCheck(client);
            case RemoteChannel.WinRm:
                return BuildWinRmCheck(client);
            case RemoteChannel.RustDesk:
                return BuildRustDeskCheck(client);
            default:
                return RemoteLaunchCheck.Error($"Remote channel {requestedChannel} is not wired for local launch yet.");
        }
    }

    public RemoteLaunchCheck Launch(ClientRow client, RemoteChannel? launchChannel = null)
    {
        var check = Check(client, launchChannel);
        if (!check.CanLaunch)
        {
            throw new InvalidOperationException(check.Message);
        }

        StartProcess(check.ExecutablePath!, check.Arguments!);
        return check;
    }

    private static void StartProcess(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private static RemoteLaunchCheck BuildRdpCheck(ClientRow client)
    {
        var executablePath = GetSystemExecutablePath("mstsc.exe");
        if (executablePath is null)
        {
            return RemoteLaunchCheck.Error("RDP client mstsc.exe is not available on this admin machine.");
        }

        var warnings = new List<string>();
        var target = client.TailscaleIpAddresses.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(target))
        {
            target = client.MachineName;
            warnings.Add("No Tailscale IP reported. Falling back to machine name for RDP.");
        }

        return RemoteLaunchCheck.Success(
            executablePath,
            $"/v:{target}",
            warnings,
            warnings.Count == 0
                ? $"RDP launch is ready for {client.DeviceName}."
                : $"RDP launch is ready for {client.DeviceName} with warnings.");
    }

    private static RemoteLaunchCheck BuildWinRmCheck(ClientRow client)
    {
        var executablePath = GetSystemExecutablePath("powershell.exe");
        if (executablePath is null)
        {
            return RemoteLaunchCheck.Error("Windows PowerShell is not available on this admin machine.");
        }

        var warnings = new List<string>();
        if (!client.TailscaleConnected)
        {
            warnings.Add("Client is not marked as Tailscale-connected. WinRM may fail if name resolution or routing is missing.");
        }

        string escapedMachineName = client.MachineName.Replace("'", "''");
        string arguments =
            "-NoExit -Command \"$sessionOption = New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck; " +
            "$session = $null; " +
            "try { $session = New-PSSession -ComputerName '" + escapedMachineName + "' -UseSSL -Port 5986 -SessionOption $sessionOption -ErrorAction Stop; Enter-PSSession -Session $session } " +
            "catch { Write-Host 'WinRM precheck failed:' $_.Exception.Message -ForegroundColor Red } " +
            "finally { if ($session) { Remove-PSSession -Session $session -ErrorAction SilentlyContinue } }\"";
        return RemoteLaunchCheck.Success(
            executablePath,
            arguments,
            warnings,
            warnings.Count == 0
                ? $"WinRM launch is ready for {client.DeviceName}."
                : $"WinRM launch is ready for {client.DeviceName} with warnings.");
    }

    private RemoteLaunchCheck BuildRustDeskCheck(ClientRow client)
    {
        if (!CanLaunchRustDesk(client))
        {
            return RemoteLaunchCheck.Error("Selected client has neither a RustDesk ID nor a direct IP for RustDesk configured.");
        }

        var executablePath = ResolveRustDeskExecutablePath(_configuredRustDeskPath);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return RemoteLaunchCheck.Error("RustDesk is not available on this admin machine. Set STEVENSSUPPORTHELPER_RUSTDESK_PATH or install RustDesk.");
        }

        var target = client.TailscaleIpAddresses.FirstOrDefault();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(target))
        {
            target = client.RustDeskId;
        }
        else if (!string.IsNullOrWhiteSpace(client.RustDeskId))
        {
            warnings.Add("Using direct IP first; RustDesk ID remains available as fallback.");
        }

        var argumentsTemplate = Environment.GetEnvironmentVariable("STEVENSSUPPORTHELPER_RUSTDESK_ARGS_TEMPLATE");
        var rustDeskPassword = !string.IsNullOrWhiteSpace(client.RustDeskPassword)
            ? client.RustDeskPassword
            : _configuredRustDeskPassword;
        var arguments = string.IsNullOrWhiteSpace(argumentsTemplate)
            ? BuildRustDeskArguments(target!, rustDeskPassword)
            : argumentsTemplate
                .Replace("{id}", client.RustDeskId, StringComparison.Ordinal)
                .Replace("{target}", target!, StringComparison.Ordinal)
                .Replace("{password}", rustDeskPassword ?? string.Empty, StringComparison.Ordinal);

        return RemoteLaunchCheck.Success(
            executablePath,
            arguments,
            warnings,
            $"RustDesk launch is ready for {client.DeviceName}.");
    }

    private static bool CanLaunchRustDesk(ClientRow client)
    {
        return !string.IsNullOrWhiteSpace(client.RustDeskId) || client.TailscaleIpAddresses.Count > 0;
    }

    private static string? GetSystemExecutablePath(string executableName)
    {
        var systemDirectory = Environment.SystemDirectory;
        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            return null;
        }

        var fullPath = Path.Combine(systemDirectory, executableName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    private static string BuildRustDeskArguments(string target, string? password)
    {
        var arguments = $"--connect \"{target.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        if (!string.IsNullOrWhiteSpace(password))
        {
            arguments += $" --password \"{password.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return arguments;
    }

    private static string? ResolveRustDeskExecutablePath(string? configuredPath)
    {
        configuredPath ??= Environment.GetEnvironmentVariable("STEVENSSUPPORTHELPER_RUSTDESK_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(programFiles, "RustDesk", "rustdesk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RustDesk", "rustdesk.exe"),
            Path.Combine(localAppData, "Programs", "RustDesk", "rustdesk.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

public sealed record RemoteLaunchCheck(
    bool CanLaunch,
    string Message,
    IReadOnlyList<string> Warnings,
    string? ExecutablePath,
    string? Arguments)
{
    public static RemoteLaunchCheck Error(string message)
    {
        return new RemoteLaunchCheck(false, message, [], null, null);
    }

    public static RemoteLaunchCheck Success(string executablePath, string arguments, IReadOnlyList<string> warnings, string message)
    {
        return new RemoteLaunchCheck(true, message, warnings, executablePath, arguments);
    }
}
