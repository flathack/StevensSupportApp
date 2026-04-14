using System.Text.Json;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Tray.Services;

internal sealed class PendingUpdateStore(TrayOptions options)
{
    private readonly TrayOptions _options = options;

    public PendingClientUpdate? Load()
    {
        var statusPath = GetStatusPath();
        if (!File.Exists(statusPath))
        {
            return null;
        }

        var json = File.ReadAllText(statusPath);
        return JsonSerializer.Deserialize<PendingClientUpdate>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public string GetStatusPath()
    {
        return Path.Combine(_options.UpdatesRoot, "pending-update.json");
    }
}
