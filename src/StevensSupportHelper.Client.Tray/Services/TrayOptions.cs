using System.Text.Json;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Tray.Services;

internal sealed class TrayOptions
{
    public string ServerBaseUrl { get; }
    public string UpdatesRoot { get; }

    public TrayOptions()
    {
        var settings = LoadSettings();
        ServerBaseUrl = Environment.GetEnvironmentVariable("STEVENSSUPPORTHELPER_SERVER_URL")
            ?? settings?.ServerBaseUrl
            ?? "http://localhost:5000";
        UpdatesRoot = Environment.GetEnvironmentVariable("STEVENSSUPPORTHELPER_UPDATES_ROOT")
            ?? settings?.UpdatesRoot
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "StevensSupportHelper",
                "Updates");
    }

    private static TraySettings? LoadSettings()
    {
        foreach (var settingsPath in EnumerateSettingsPaths())
        {
            if (!File.Exists(settingsPath))
            {
                continue;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<TraySettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (settings is not null)
            {
                return settings;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSettingsPaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StevensSupportHelper",
            "tray-settings.json");

        yield return Path.Combine(AppContext.BaseDirectory, "tray-settings.json");
    }
}

internal sealed record TraySettings(string? ServerBaseUrl, string? UpdatesRoot);
