using System.IO;
using System.Text.Json;

namespace StevensSupportHelper.Admin.Services;

public sealed class PowerShellTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _templatesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StevensSupportHelper",
        "powershell-templates.json");

    public IReadOnlyList<PowerShellTemplateDefinition> Load()
    {
        if (!File.Exists(_templatesPath))
        {
            return
            [
                new PowerShellTemplateDefinition("Top Prozesse", "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 20 ProcessName, Id, CPU, WorkingSet64"),
                new PowerShellTemplateDefinition("Dienste", "Get-Service | Sort-Object Status, DisplayName | Select-Object Status, DisplayName, Name"),
                new PowerShellTemplateDefinition("Freier Speicher", "Get-PSDrive -PSProvider FileSystem | Select-Object Name, Used, Free")
            ];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PowerShellTemplateDefinition>>(File.ReadAllText(_templatesPath), JsonOptions)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<PowerShellTemplateDefinition> templates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_templatesPath)!);
        File.WriteAllText(_templatesPath, JsonSerializer.Serialize(templates, JsonOptions));
    }
}

public sealed record PowerShellTemplateDefinition(string Name, string Script);
