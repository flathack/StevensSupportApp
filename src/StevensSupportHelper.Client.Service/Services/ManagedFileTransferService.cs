using Microsoft.Extensions.Options;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Service.Services;

public sealed class ManagedFileTransferService(IOptions<ServiceOptions> options)
{
    private readonly ServiceOptions _options = options.Value;
    private readonly string _managedFilesRoot = Environment.ExpandEnvironmentVariables(options.Value.ManagedFilesRoot);

    public async Task ProcessAsync(
        ClientIdentity identity,
        FileTransferDto transfer,
        ServerApiClient serverApiClient,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_managedFilesRoot);

        try
        {
            switch (transfer.Direction)
            {
                case FileTransferDirection.AdminToClient:
                    await ProcessAdminToClientAsync(identity, transfer, serverApiClient, cancellationToken);
                    break;
                case FileTransferDirection.ClientToAdmin:
                    await ProcessClientToAdminAsync(identity, transfer, serverApiClient, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown file transfer direction: {transfer.Direction}");
            }
        }
        catch (Exception exception)
        {
            await serverApiClient.CompleteFileTransferAsync(
                transfer.TransferId,
                new CompleteFileTransferRequest(identity.ClientId, identity.ClientSecret, false, exception.Message, null),
                cancellationToken);
        }
    }

    private async Task ProcessAdminToClientAsync(
        ClientIdentity identity,
        FileTransferDto transfer,
        ServerApiClient serverApiClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transfer.ContentBase64))
        {
            throw new InvalidOperationException("Upload transfer is missing content.");
        }

        byte[] bytes = Convert.FromBase64String(transfer.ContentBase64);
        ValidateSize(bytes.LongLength);
        string targetPath = ResolveManagedPath(transfer.RelativePath);
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
        await serverApiClient.CompleteFileTransferAsync(
            transfer.TransferId,
            new CompleteFileTransferRequest(identity.ClientId, identity.ClientSecret, true, null, null),
            cancellationToken);
    }

    private async Task ProcessClientToAdminAsync(
        ClientIdentity identity,
        FileTransferDto transfer,
        ServerApiClient serverApiClient,
        CancellationToken cancellationToken)
    {
        string sourcePath = ResolveManagedPath(transfer.RelativePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Requested file does not exist in the managed transfer root.", sourcePath);
        }

        byte[] bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        ValidateSize(bytes.LongLength);
        string contentBase64 = Convert.ToBase64String(bytes);

        await serverApiClient.CompleteFileTransferAsync(
            transfer.TransferId,
            new CompleteFileTransferRequest(identity.ClientId, identity.ClientSecret, true, null, contentBase64),
            cancellationToken);
    }

    private string ResolveManagedPath(string relativePath)
    {
        string cleanedRelative = NormalizeRelativePath(relativePath);
        string root = Path.GetFullPath(_managedFilesRoot);
        string fullPath = Path.GetFullPath(Path.Combine(root, cleanedRelative));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Transfer path escapes the managed files root.");
        }

        return fullPath;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        string normalized = (relativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Transfer path is required.");
        }

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("Transfer path must be relative.");
        }

        string[] parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
        {
            throw new InvalidOperationException("Transfer path contains invalid traversal segments.");
        }

        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private void ValidateSize(long bytes)
    {
        if (bytes > _options.MaxTransferBytes)
        {
            throw new InvalidOperationException($"Transfer exceeds the configured maximum of {_options.MaxTransferBytes} bytes.");
        }
    }
}