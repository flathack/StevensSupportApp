using System.Text.Json;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class ClientIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;

    public ClientIdentityStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StevensSupportHelper");
        Directory.CreateDirectory(root);
        _stateFilePath = Path.Combine(root, "client-identity.json");
    }

    public async Task<ClientIdentity?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_stateFilePath);
        return await JsonSerializer.DeserializeAsync<ClientIdentity>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(ClientIdentity identity, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_stateFilePath);
        await JsonSerializer.SerializeAsync(stream, identity, JsonOptions, cancellationToken);
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
        }

        return Task.CompletedTask;
    }
}

public sealed record ClientIdentity(Guid ClientId, string ClientSecret);