namespace StevensSupportHelper.Shared.Diagnostics;

public static class AppDiagnostics
{
    private static readonly Lock SyncRoot = new();

    public static string GetLogFilePath(string componentName, string? rootDirectory = null)
    {
        var safeComponentName = string.IsNullOrWhiteSpace(componentName)
            ? "application"
            : string.Concat(componentName.Trim().Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')).ToLowerInvariant();

        var baseDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "StevensSupportHelper",
                "Logs")
            : rootDirectory;

        Directory.CreateDirectory(baseDirectory);
        return Path.Combine(baseDirectory, $"{safeComponentName}.log");
    }

    public static void WriteEvent(
        string componentName,
        string eventType,
        string message,
        Exception? exception = null,
        string? rootDirectory = null)
    {
        var logFilePath = GetLogFilePath(componentName, rootDirectory);
        var lines = new List<string>
        {
            $"[{DateTimeOffset.UtcNow:O}] [{eventType}] {message}"
        };

        if (exception is not null)
        {
            lines.Add(exception.ToString());
        }

        lines.Add(string.Empty);

        lock (SyncRoot)
        {
            File.AppendAllLines(logFilePath, lines);
        }
    }
}
