using System.Text.Json.Serialization;

namespace XunxianDpkViewer.Core;

public sealed class UpdateChannelManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; init; } = string.Empty;

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("bootstrapUrls")]
    public List<string> BootstrapUrls { get; init; } = [];

    [JsonPropertyName("packages")]
    public List<UpdatePackage> Packages { get; init; } = [];

    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;
}

public sealed class UpdatePackage
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 100;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;
}

public sealed record UpdateCheckResult(
    bool Checked,
    bool IsUpdateAvailable,
    UpdateChannelManifest? Manifest,
    string? ManifestUrl,
    string? Message);

public sealed record UpdateDownloadResult(
    string FilePath,
    UpdatePackage Package);

public readonly record struct UpdateDownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double? Percentage,
    string? SourceLabel = null);
