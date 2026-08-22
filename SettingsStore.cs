using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace ClassCodexDownloader;

public static class SettingsStore
{
    private const string SectionName = "ClassCodexDownloader";
    private const string AddonsPathKey = "AddonsPath";
    private const string LegacyWindowsDefaultAddonsPath = @"C:\Program Files (x86)\World of Warcraft\_retail_\Interface\AddOns";

    private static readonly string SettingsFilePath = IOPath.Combine(
        AppContext.BaseDirectory,
        "appsettings.json");

    internal static UiSettings Load()
    {
        var defaultAddonsPath = GetDefaultAddonsPath();
        var settings = new UiSettings
        {
            AddonsPath = defaultAddonsPath
        };

        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Save(settings.AddonsPath);
                return settings;
            }

            var json = File.ReadAllText(SettingsFilePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(SectionName, out var section)
                && section.ValueKind == JsonValueKind.Object
                && section.TryGetProperty(AddonsPathKey, out var addonsPath)
                && addonsPath.ValueKind == JsonValueKind.String)
            {
                var value = addonsPath.GetString();
                if (ShouldUseSavedAddonsPath(value, defaultAddonsPath))
                {
                    settings.AddonsPath = value!;
                }
            }
        }
        catch
        {
            // If reading fails, keep defaults.
        }

        return settings;
    }

    internal static void Save(string addonsPath)
    {
        try
        {
            var directory = IOPath.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new UiSettings
            {
                AddonsPath = addonsPath
            };

            JsonObject root;
            if (File.Exists(SettingsFilePath))
            {
                var existing = JsonNode.Parse(File.ReadAllText(SettingsFilePath)) as JsonObject;
                root = existing ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            if (root[SectionName] is not JsonObject section)
            {
                section = new JsonObject();
                root[SectionName] = section;
            }

            section[AddonsPathKey] = settings.AddonsPath;

            var json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Do not block app due to settings save errors.
        }
    }

    public static string GetDefaultAddonsPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var discoveredPath = TryDiscoverWindowsAddonsPath();
            if (!string.IsNullOrWhiteSpace(discoveredPath))
            {
                return discoveredPath;
            }

            return LegacyWindowsDefaultAddonsPath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return IOPath.Combine(home, "Games", "World of Warcraft", "_retail_", "Interface", "AddOns");
    }

    public static string ResolveAddonsPath()
    {
        var settings = Load();
        return string.IsNullOrWhiteSpace(settings.AddonsPath)
            ? GetDefaultAddonsPath()
            : settings.AddonsPath;
    }

    private static bool ShouldUseSavedAddonsPath(string? savedPath, string discoveredOrDefaultPath)
    {
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var hasDiscoveredPath = !ArePathsEqual(discoveredOrDefaultPath, LegacyWindowsDefaultAddonsPath);
        var savedIsLegacyDefault = ArePathsEqual(savedPath, LegacyWindowsDefaultAddonsPath);

        return !(hasDiscoveredPath && savedIsLegacyDefault);
    }

    private static bool ArePathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        var normalized = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        normalized = normalized.Replace('/', IOPath.DirectorySeparatorChar).Replace('\\', IOPath.DirectorySeparatorChar);
        return normalized.TrimEnd(IOPath.DirectorySeparatorChar);
    }

    [SupportedOSPlatform("windows")]
    private static string? TryDiscoverWindowsAddonsPath()
    {
        foreach (var installPath in GetInstallPathsFromRegistry())
        {
            var addonsPath = TryBuildAddonsPath(installPath);
            if (!string.IsNullOrWhiteSpace(addonsPath))
            {
                return addonsPath;
            }
        }

        foreach (var installPath in GetInstallPathsFromBattleNetConfig())
        {
            var addonsPath = TryBuildAddonsPath(installPath);
            if (!string.IsNullOrWhiteSpace(addonsPath))
            {
                return addonsPath;
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetInstallPathsFromRegistry()
    {
        var keyPaths = new[]
        {
            @"SOFTWARE\Blizzard Entertainment\World of Warcraft",
            @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft"
        };

        var valueNames = new[]
        {
            "InstallPath",
            "Path",
            "GamePath",
            "InstallLocation"
        };

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                }
                catch
                {
                    continue;
                }

                using (baseKey)
                {
                    foreach (var keyPath in keyPaths)
                    {
                        RegistryKey? subKey;
                        try
                        {
                            subKey = baseKey.OpenSubKey(keyPath);
                        }
                        catch
                        {
                            continue;
                        }

                        if (subKey is null)
                        {
                            continue;
                        }

                        using (subKey)
                        {
                            foreach (var valueName in valueNames)
                            {
                                var rawValue = subKey.GetValue(valueName) as string;
                                if (!string.IsNullOrWhiteSpace(rawValue))
                                {
                                    yield return rawValue;
                                }
                            }

                            foreach (var dynamicValueName in subKey.GetValueNames())
                            {
                                var rawValue = subKey.GetValue(dynamicValueName) as string;
                                if (!string.IsNullOrWhiteSpace(rawValue)
                                    && rawValue.Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase))
                                {
                                    yield return rawValue;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetInstallPathsFromBattleNetConfig()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
        {
            yield break;
        }

        var candidateFiles = new[]
        {
            IOPath.Combine(programData, "Battle.net", "Battle.net.config"),
            IOPath.Combine(programData, "Battle.net", "Agent", "product.db")
        };

        foreach (var candidateFile in candidateFiles)
        {
            if (!File.Exists(candidateFile))
            {
                continue;
            }

            byte[] content;
            try
            {
                content = File.ReadAllBytes(candidateFile);
            }
            catch
            {
                continue;
            }

            foreach (var text in ExtractTextSegments(content))
            {
                if (!text.Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidatePath = ExtractPathCandidate(text);
                if (!string.IsNullOrWhiteSpace(candidatePath))
                {
                    yield return candidatePath;
                }
            }
        }
    }

    private static string? TryBuildAddonsPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        var candidate = installPath.Trim().Trim('\"').Replace('/', IOPath.DirectorySeparatorChar);
        candidate = Environment.ExpandEnvironmentVariables(candidate);

        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var executableFolder = IOPath.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(executableFolder))
            {
                candidate = executableFolder;
            }
        }

        var normalizedCandidate = candidate.TrimEnd(IOPath.DirectorySeparatorChar);

        var directAddons = IOPath.Combine(normalizedCandidate, "Interface", "AddOns");
        if (Directory.Exists(directAddons))
        {
            return directAddons;
        }

        var retailPath = normalizedCandidate;

        var retailMarker = $"{IOPath.DirectorySeparatorChar}_retail_";
        var markerIndex = retailPath.IndexOf(retailMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            retailPath = retailPath[..(markerIndex + retailMarker.Length)];
        }
        else
        {
            var possibleRetailPath = IOPath.Combine(retailPath, "_retail_");
            if (Directory.Exists(possibleRetailPath))
            {
                retailPath = possibleRetailPath.TrimEnd(IOPath.DirectorySeparatorChar);
            }
        }

        var addonsPath = IOPath.Combine(retailPath, "Interface", "AddOns");
        return Directory.Exists(addonsPath) ? addonsPath : null;
    }

    private static IEnumerable<string> ExtractTextSegments(byte[] data)
    {
        var chars = new List<char>(256);

        static bool IsPathChar(char c) =>
            char.IsLetterOrDigit(c)
            || c is ':' or '\\' or '/' or '_' or '-' or '.' or ' ' or '(' or ')';

        foreach (var b in data)
        {
            var c = (char)b;
            if (IsPathChar(c))
            {
                chars.Add(c);
            }
            else
            {
                if (chars.Count >= 8)
                {
                    yield return new string(chars.ToArray());
                }

                chars.Clear();
            }
        }

        if (chars.Count >= 8)
        {
            yield return new string(chars.ToArray());
        }
    }

    private static string? ExtractPathCandidate(string input)
    {
        var wowIndex = input.IndexOf("World of Warcraft", StringComparison.OrdinalIgnoreCase);
        if (wowIndex < 0)
        {
            return null;
        }

        var start = wowIndex;
        while (start > 0)
        {
            var c = input[start - 1];
            if (c is '\\' or '/' || char.IsLetterOrDigit(c) || c is ':' or '_' or '-' or '.' or ' ' or '(' or ')')
            {
                start--;
                continue;
            }

            break;
        }

        var end = wowIndex + "World of Warcraft".Length;
        while (end < input.Length)
        {
            var c = input[end];
            if (c is '\\' or '/' || char.IsLetterOrDigit(c) || c is ':' or '_' or '-' or '.' or ' ' or '(' or ')')
            {
                end++;
                continue;
            }

            break;
        }

        var candidate = input[start..end].Trim().Trim('\"');
        if (candidate.Length >= 3 && char.IsLetter(candidate[0]) && candidate[1] == ':')
        {
            return candidate;
        }

        return null;
    }
}

internal sealed class UiSettings
{
    public string AddonsPath { get; set; } = string.Empty;
}