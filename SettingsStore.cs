using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using IOPath = System.IO.Path;

namespace ClassCodexDownloader;

public static class SettingsStore
{
    private const string SectionName = "ClassCodexDownloader";
    private const string AddonsPathKey = "AddonsPath";

    private static readonly string SettingsFilePath = IOPath.Combine(
        AppContext.BaseDirectory,
        "appsettings.json");

    internal static UiSettings Load()
    {
        var settings = new UiSettings
        {
            AddonsPath = GetDefaultAddonsPath()
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
                if (!string.IsNullOrWhiteSpace(value))
                {
                    settings.AddonsPath = value;
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

            var section = root[SectionName] as JsonObject;
            if (section is null)
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"C:\Program Files (x86)\World of Warcraft\_retail_\Interface\AddOns";
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
}

internal sealed class UiSettings
{
    public string AddonsPath { get; set; } = string.Empty;
}