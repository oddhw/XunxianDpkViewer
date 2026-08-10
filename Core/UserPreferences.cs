using System.Text.Json;

namespace XunxianDpkViewer.Core;

public static class UserPreferences
{
    private sealed class Settings
    {
        public string? LastResourceFolder { get; set; }
        public bool AutoCheckForUpdates { get; set; } = true;
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }
        public List<string> UpdateBootstrapUrls { get; set; } = [];
        public Dictionary<string, List<string>> DisabledCompositeParts { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object SyncRoot = new();

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XunxianDpkViewer",
        "settings.json");

    public static string? LoadResourceFolder()
    {
        lock (SyncRoot) return LoadSettings().LastResourceFolder;
    }

    public static void SaveResourceFolder(string folder)
    {
        lock (SyncRoot)
        {
            Settings settings = LoadSettings();
            settings.LastResourceFolder = Path.GetFullPath(folder);
            SaveSettings(settings);
        }
    }

    public static bool LoadAutoCheckForUpdates()
    {
        lock (SyncRoot) return LoadSettings().AutoCheckForUpdates;
    }

    public static void SaveAutoCheckForUpdates(bool enabled)
    {
        lock (SyncRoot)
        {
            Settings settings = LoadSettings();
            settings.AutoCheckForUpdates = enabled;
            SaveSettings(settings);
        }
    }

    public static DateTimeOffset? LoadLastUpdateCheckUtc()
    {
        lock (SyncRoot) return LoadSettings().LastUpdateCheckUtc;
    }

    public static void SaveLastUpdateCheckUtc(DateTimeOffset value)
    {
        lock (SyncRoot)
        {
            Settings settings = LoadSettings();
            settings.LastUpdateCheckUtc = value.ToUniversalTime();
            SaveSettings(settings);
        }
    }

    public static IReadOnlyList<string> LoadUpdateBootstrapUrls()
    {
        lock (SyncRoot)
        {
            return LoadSettings().UpdateBootstrapUrls
                .Where(IsSecureHttpUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static void MergeUpdateBootstrapUrls(IEnumerable<string> urls)
    {
        lock (SyncRoot)
        {
            Settings settings = LoadSettings();
            settings.UpdateBootstrapUrls = urls
                .Concat(settings.UpdateBootstrapUrls)
                .Where(IsSecureHttpUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            SaveSettings(settings);
        }
    }

    public static IReadOnlySet<string> LoadDisabledCompositeParts(string presetKey)
    {
        lock (SyncRoot)
        {
            Settings settings = LoadSettings();
            return settings.DisabledCompositeParts.TryGetValue(presetKey, out List<string>? keys)
                ? keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveDisabledCompositeParts(string presetKey, IEnumerable<string> disabledPartKeys)
    {
        lock (SyncRoot)
        {
            Settings settings = LoadSettings();
            List<string> keys = disabledPartKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0)
                settings.DisabledCompositeParts.Remove(presetKey);
            else
                settings.DisabledCompositeParts[presetKey] = keys;
            SaveSettings(settings);
        }
    }

    private static Settings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    private static void SaveSettings(Settings settings)
    {
        string? parent = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

        string temporaryPath = SettingsPath + ".new";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    private static bool IsSecureHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
