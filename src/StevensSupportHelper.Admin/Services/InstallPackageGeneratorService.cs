using System.IO;
using System.IO.Compression;
using System.Text;

namespace StevensSupportHelper.Admin.Services;

public sealed class InstallPackageGeneratorService
{
    public string BuildPackage(
        string clientInstallerPath,
        string packageGeneratorPath,
        string installerConfigText,
        string outputZipPath)
    {
        if (!File.Exists(clientInstallerPath))
        {
            throw new FileNotFoundException("Client installer not found.", clientInstallerPath);
        }

        if (string.IsNullOrWhiteSpace(packageGeneratorPath) || !Directory.Exists(packageGeneratorPath))
        {
            throw new DirectoryNotFoundException($"Package generator path not found: {packageGeneratorPath}");
        }

        if (string.IsNullOrWhiteSpace(outputZipPath))
        {
            throw new InvalidOperationException("Output ZIP path is required.");
        }

        var outputDirectory = Path.GetDirectoryName(outputZipPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Output ZIP path has no valid target directory.");
        }

        Directory.CreateDirectory(outputDirectory);

        var tempRoot = Path.Combine(Path.GetTempPath(), "StevensSupportHelperPackageGenerator", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var installerFileName = Path.GetFileName(clientInstallerPath);
            var tempInstallerPath = Path.Combine(tempRoot, installerFileName);
            File.Copy(clientInstallerPath, tempInstallerPath, overwrite: true);

            var configPath = Path.Combine(tempRoot, "client.installer.config");
            File.WriteAllText(configPath, installerConfigText, new UTF8Encoding(false));

            foreach (var sourceFile in Directory.GetFiles(packageGeneratorPath, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(sourceFile);
                if (string.Equals(fileName, installerFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "client.installer.config", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(sourceFile, Path.Combine(tempRoot, fileName), overwrite: true);
            }

            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }

            ZipFile.CreateFromDirectory(tempRoot, outputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return outputZipPath;
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

    public string BuildDefaultInstallerConfigText(string serverUrl)
    {
        var samplePath = ResolveInstallerSampleConfigPath();
        if (!File.Exists(samplePath))
        {
            throw new FileNotFoundException("client.installer.config.sample was not found.", samplePath);
        }

        var text = File.ReadAllText(samplePath);
        return text.Replace("http://100.123.81.119:5000", serverUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveInstallerSampleConfigPath()
    {
        var fileName = "client.installer.config.sample";
        var baseDirectory = AppContext.BaseDirectory;

        foreach (var candidate in EnumerateCandidateSamplePaths(baseDirectory, fileName))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(baseDirectory, fileName);
    }

    private static IEnumerable<string> EnumerateCandidateSamplePaths(string baseDirectory, string fileName)
    {
        yield return Path.Combine(baseDirectory, fileName);

        var currentDirectory = new DirectoryInfo(baseDirectory);
        while (currentDirectory is not null)
        {
            yield return Path.Combine(currentDirectory.FullName, fileName);

            var publishCandidate = Path.Combine(currentDirectory.FullName, "publish");
            if (Directory.Exists(publishCandidate))
            {
                foreach (var publishedSample in Directory.GetFiles(publishCandidate, fileName, SearchOption.AllDirectories))
                {
                    yield return publishedSample;
                }
            }

            currentDirectory = currentDirectory.Parent;
        }
    }
}
