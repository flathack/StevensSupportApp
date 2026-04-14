using System.Diagnostics;
using System.IO;

namespace StevensSupportHelper.Admin.Services;

public sealed class LocalServerLauncher
{
    public void Start(string serverProjectPath, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverProjectPath))
        {
            throw new InvalidOperationException("Configure a server project path first.");
        }

        if (!File.Exists(serverProjectPath))
        {
            throw new FileNotFoundException("The configured server project path does not exist.", serverProjectPath);
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Server URL must be a valid absolute http:// or https:// URL.");
        }

        string projectDirectory = Path.GetDirectoryName(serverProjectPath)
            ?? throw new InvalidOperationException("Could not determine the server project directory.");
        string escapedDirectory = EscapePowerShellLiteral(projectDirectory);
        string escapedProjectPath = EscapePowerShellLiteral(serverProjectPath);
        var bindingUrl = BuildBindingUrl(serverUri);
        string escapedBindingUrl = EscapePowerShellLiteral(bindingUrl);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"Set-Location -LiteralPath '{escapedDirectory}'; dotnet run --project '{escapedProjectPath}' --urls '{escapedBindingUrl}'\"",
            WorkingDirectory = projectDirectory,
            UseShellExecute = true
        };

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the local server process.");
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string BuildBindingUrl(Uri serverUri)
    {
        var port = serverUri.IsDefaultPort
            ? (serverUri.Scheme == Uri.UriSchemeHttps ? 443 : 80)
            : serverUri.Port;
        return $"{serverUri.Scheme}://0.0.0.0:{port}";
    }
}
