namespace XunxianDpkViewer.Core;

// Keep network regression checks independent of the user's actual settings.
public static class UserPreferences
{
    public static IReadOnlyList<string> BootstrapUrls { get; set; } = Array.Empty<string>();
    public static bool LoadAutoCheckForUpdates() => true;
    public static DateTimeOffset? LoadLastUpdateCheckUtc() => null;
    public static IReadOnlyList<string> LoadUpdateBootstrapUrls() => BootstrapUrls;
    public static void MergeUpdateBootstrapUrls(IEnumerable<string> urls) { }
    public static void SaveLastUpdateCheckUtc(DateTimeOffset value) { }
}
