using StevensSupportHelper.Shared.Diagnostics;

namespace StevensSupportHelper.Server.Tests;

public sealed class AppDiagnosticsTests
{
    [Fact]
    public void WriteEvent_CreatesLogFileAndWritesMessage()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));

        AppDiagnostics.WriteEvent("Server", "Startup", "Server booted.", rootDirectory: logRoot);

        var logFile = AppDiagnostics.GetLogFilePath("Server", logRoot);
        var content = File.ReadAllText(logFile);

        Assert.Contains("[Startup] Server booted.", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEvent_WithException_WritesExceptionDetails()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "ssh-tests", Guid.NewGuid().ToString("N"));
        var exception = new InvalidOperationException("boom");

        AppDiagnostics.WriteEvent("ClientTray", "UnhandledException", "Unexpected failure.", exception, logRoot);

        var logFile = AppDiagnostics.GetLogFilePath("ClientTray", logRoot);
        var content = File.ReadAllText(logFile);

        Assert.Contains("Unexpected failure.", content, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
        Assert.Contains("boom", content, StringComparison.Ordinal);
    }
}
