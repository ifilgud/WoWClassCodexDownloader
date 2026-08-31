using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClassCodexDownloader;

public static class Downloader
{
    public const string CdnBaseUrl = "https://wow-class-codex.s3.us-east-1.amazonaws.com";
    public const string GameVersionId = "retail";
    public const string ReleaseChannel = "production";
    public const int DownloadTimeoutSeconds = 60;

    private const int ChunkSize = 1024 * 1024;

    public static bool IsAddonInstalled(string addonsPathInput)
    {
        if (string.IsNullOrWhiteSpace(addonsPathInput)
            || addonsPathInput.TrimStart().StartsWith("/path/", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var addonsPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(addonsPathInput.Trim()));
            var addonFolder = Path.Combine(addonsPath, "ClassCodex");
            return Directory.Exists(addonFolder);
        }
        catch
        {
            return false;
        }
    }

    public static async Task RunAsync(
        string addonsPathInput,
        Action<string>? log = null,
        Action<string>? errorLog = null,
        CancellationToken cancellationToken = default)
    {
        log ??= static _ => { };
        errorLog ??= log;

        var addonsPathText = addonsPathInput.Trim();

        if (string.IsNullOrWhiteSpace(addonsPathText) || addonsPathText.StartsWith("/path/", StringComparison.Ordinal))
        {
            errorLog("Error: set a valid AddOns path.");
            return;
        }

        var addonsPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(addonsPathText));

        if (!Directory.Exists(addonsPath))
        {
            errorLog($"Error: AddOns folder not found: {addonsPath}");
            return;
        }

        var configUrl = $"{CdnBaseUrl.TrimEnd('/')}/channels/{GameVersionId}/{ReleaseChannel}/config.json";

        log($"Downloading configuration: {configUrl}");
        using var config = await DownloadJsonAsync(configUrl, cancellationToken);
        var configRoot = config.RootElement;

        if (!string.Equals(GetString(configRoot, "gameVersionId"), GameVersionId, StringComparison.Ordinal)
            || !string.Equals(GetString(configRoot, "channel"), ReleaseChannel, StringComparison.Ordinal))
        {
            errorLog("Error: the received configuration does not match the configured game version/channel.");
            return;
        }

        var buildId = GetString(configRoot, "buildId");
        var manifestUrl = GetString(configRoot, "manifestUrl");
        var manifestExpectedHash = GetString(configRoot, "manifestSha256");

        if (string.IsNullOrWhiteSpace(buildId)
            || string.IsNullOrWhiteSpace(manifestUrl)
            || string.IsNullOrWhiteSpace(manifestExpectedHash))
        {
            errorLog("Error: incomplete channel configuration.");
            return;
        }

        log($"Downloading manifest, build: {buildId}");

        var filesToProcess = new List<ManifestFile>();

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"classcodex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var manifestPath = Path.Combine(tempDirectory, "manifest.json");
            await DownloadAsync(manifestUrl, manifestPath, cancellationToken);

            if (!string.Equals(Sha256File(manifestPath), manifestExpectedHash.ToLowerInvariant(), StringComparison.Ordinal))
            {
                errorLog("Error: manifest SHA-256 verification failed.");
                return;
            }

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, Encoding.UTF8, cancellationToken));
            var manifestRoot = manifest.RootElement;

            if (!manifestRoot.TryGetProperty("addon", out var addon)
                || !string.Equals(GetString(addon, "id"), "class-codex", StringComparison.Ordinal)
                || !string.Equals(GetString(addon, "name"), "ClassCodex", StringComparison.Ordinal)
                || !string.Equals(GetString(addon, "gameVersionId"), GameVersionId, StringComparison.Ordinal))
            {
                errorLog("Error: the manifest does not belong to the expected ClassCodex addon.");
                return;
            }

            if (!manifestRoot.TryGetProperty("build", out var build)
                || !string.Equals(GetString(build, "id"), buildId, StringComparison.Ordinal))
            {
                errorLog("Error: the build ID in the configuration does not match the build ID in the manifest.");
                return;
            }

            if (!manifestRoot.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array
                || files.GetArrayLength() == 0)
            {
                errorLog("Error: the manifest does not contain any files.");
                return;
            }

            foreach (var entry in files.EnumerateArray())
            {
                var relative = GetString(entry, "path");
                var expectedHash = GetString(entry, "sha256");

                if (string.IsNullOrWhiteSpace(relative)
                    || !relative.StartsWith("ClassCodex/", StringComparison.Ordinal)
                    || relative.Split('/').Contains("..", StringComparer.Ordinal))
                {
                    errorLog($"Error: unsafe manifest path: {relative ?? "<null>"}");
                    return;
                }

                if (!entry.TryGetProperty("size", out var sizeElement)
                    || sizeElement.ValueKind != JsonValueKind.Number
                    || !sizeElement.TryGetInt64(out var expectedSize)
                    || string.IsNullOrWhiteSpace(expectedHash))
                {
                    errorLog($"Error: invalid manifest entry: {relative}");
                    return;
                }

                filesToProcess.Add(new ManifestFile(relative, expectedSize, expectedHash));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore errors while cleaning temporary files.
            }
        }

        var downloaded = 0;
        var skipped = 0;

        foreach (var file in filesToProcess)
        {
            var target = Path.Combine(addonsPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            var validLocal = File.Exists(target)
                && new FileInfo(target).Length == file.ExpectedSize
                && string.Equals(Sha256File(target), file.ExpectedHash.ToLowerInvariant(), StringComparison.Ordinal);

            if (validLocal)
            {
                skipped++;
                log($"OK       {file.RelativePath}");
                continue;
            }

            log($"DOWNLOAD {file.RelativePath}");

            await DownloadAsync(FileUrl(buildId, file.RelativePath), target, cancellationToken);

            var validDownloaded = File.Exists(target)
                && new FileInfo(target).Length == file.ExpectedSize
                && string.Equals(Sha256File(target), file.ExpectedHash.ToLowerInvariant(), StringComparison.Ordinal);

            if (!validDownloaded)
            {
                try
                {
                    File.Delete(target);
                }
                catch
                {
                    // Ignore errors while trying to delete an invalid file.
                }

                errorLog($"Error: downloaded file verification failed: {file.RelativePath}");
                return;
            }

            downloaded++;
        }

        log(string.Empty);
        log($"Done. Build: {buildId}; downloaded: {downloaded}; already up to date: {skipped}.");
        log($"Addon folder: {Path.Combine(addonsPath, "ClassCodex")}");
    }

    private static string Sha256File(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = destination + ".part";
        try
        {
            using var client = CreateHttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true))
            {
                await source.CopyToAsync(target, ChunkSize, cancellationToken);
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temp, destination);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // Ignore secondary cleanup errors.
            }

            throw;
        }
    }

    private static async Task<JsonDocument> DownloadJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        await using var stream = await client.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string FileUrl(string buildId, string manifestPath)
    {
        var encodedPath = string.Join(
            "/",
            manifestPath
                .Split('/')
                .Select(Uri.EscapeDataString));

        return $"{CdnBaseUrl.TrimEnd('/')}/builds/{GameVersionId}/{buildId}/{encodedPath}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClassCodex-installer/1.0");
        return client;
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private readonly record struct ManifestFile(string RelativePath, long ExpectedSize, string ExpectedHash);
}