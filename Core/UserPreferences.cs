using System.Text.Json;

namespace XunxianDpkViewer.Core;

public static class UserPreferences
{
    private sealed record Settings(string? LastResourceFolder);

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XunxianDpkViewer",
        "settings.json");

    public static string? LoadResourceFolder()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.LastResourceFolder;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveResourceFolder(string folder)
    {
        string? parent = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
            new Settings(Path.GetFullPath(folder)),
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
