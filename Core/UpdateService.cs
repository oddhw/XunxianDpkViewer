using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XunxianDpkViewer.Core;

public sealed class UpdateService
{
    private static readonly string[] BuiltInBootstrapUrls =
    [
        "https://gitcode.com/oddhw/XunxianDpkViewer/raw/main/update/stable.json",
        "https://gitcode.com/oddhw/XunxianDpkViewer/raw/master/update/stable.json",
        "https://gitee.com/oddhw/XunxianDpkViewer/raw/master/update/stable.json",
        "https://raw.githubusercontent.com/oddhw/XunxianDpkViewer/main/update/stable.json",
        "https://github.com/oddhw/XunxianDpkViewer/releases/latest/download/stable.json"
    ];

    private static readonly string[] UpdateSigningPublicKeys =
    [
        // 2.2 and earlier release key. Keep it so 2.2.1 can bridge existing installations.
        "BgIAAACkAABSU0ExAAwAAAEAAQBN/0stnV9weID7c3sXFwvIW/52uYYM+CZzD0RfWEA8QRXpKi4hy8Qn+PtG9DCoRDO2dBcqSBnQ2DwIU/XBQVe4XHc+KbcBhR1oApNsR5l+vbWlolRGdjA7fEg5AA1fL8rcxBmV5/+GxkiOaqCx1r0BxVgZ8YV+2iCRXoGIJ4+MVI0U++DsJdE98bqzS7A9Gys6Y0teJwK++oy5x6UyJTQPpuIaHda0/k3LiB8Lt40Z4vnJJBhcrvVEV03sqAFWFseBrjn7lVevFPeysmpLVLAw2LgrvHoW6P2anANhjMbcwp6VKWnZtcEtOonB1Qm65kJh72FCeVOIm81Zy67G0M6ZDiAS5TYtV5+mSQHxCFFYvaXVQY1OFnNwwhHHQFRaDlEkdIsexkR4NQhyoPa6Xwe5yJmUwghTiJtTnnBkrqNwY3IhYrlzrrflX6VmRjHH//N9RnWD/udCIP+maqie+SLrqSUY3Sj2vU/D1edo1YoVoVKN4rKNmXNFAmw++4pTAq4=",
        // Current release key.
        "BgIAAACkAABSU0ExABAAAAEAAQCVkg9EgvSxaacAGd83JA+KRFf2NN+uRw/+yzf2j8a0jwh2YUQXlseghjR0K1tgS2xlJTkVOm63nUCTWRW3TM88v5itx1kb2DMH03bt25H2DVycoUL3UkCkIMOm7POaHNAL9R7g/KH55QYog63dLX4LvAT9XZeeUabR0Z0SqQl4F/3Y9JSTOWGEOzmkSjDt6Mv4qBKzMXDlQrTNfFcZEP+58nV06027gT8bLqs/rELOcWoQY145U12Sk0CGrAHY1w1kcIbvnWuzNpbriHb+tp3kK4JhibrCchdOjVkLHoIzQQfEgoKRLXGK+R4NX8tBAJeSp1uydWqaC6/nCSMQzR3U2m57nY4dwc11XaV7jC4WMlNMYOoOWU/vL1pXlZk2XhxFLmpQsVkr0RMEnAbdfsyfNhKh4vU0zsdVPDk/OP/h5XSeHIZWzhKrirFra1tvKzulIfdWi3mYi6Ut2K/KpjuZstJKUyTPzBLhiu3PYs1TxanMDcALnqwkeLPug+vDMXQhqSxrEnpsxlJl18sYwcGyZBZNOch6dhZMZ3VzVGsBwl9jv6OLqCv/rPxH7XXT6qZIR3TrUNgTDfgdoaHy4IJbPK+EE350cUsKiVPMzlbGp9REhXufFVH1HmIvm0m+npFdh6au05WCfJPzigmUssH6P4HOARkaesoH8Y3ho73YyQ=="
    ];

    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (!force)
        {
            if (!UserPreferences.LoadAutoCheckForUpdates())
                return new UpdateCheckResult(false, false, null, null, "已关闭自动检查更新");

            DateTimeOffset? lastCheck = UserPreferences.LoadLastUpdateCheckUtc();
            if (lastCheck is not null && DateTimeOffset.UtcNow - lastCheck < TimeSpan.FromHours(24))
                return new UpdateCheckResult(false, false, null, null, "今天已经检查过更新");
        }

        string[] urls = UserPreferences.LoadUpdateBootstrapUrls()
            .Concat(BuiltInBootstrapUrls)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ManifestCandidate[] candidates = await Task.WhenAll(
            urls.Select(url => DownloadManifestCandidateAsync(url, cancellationToken)));
        UpdateChannelManifest? newestManifest = null;
        string? newestManifestUrl = null;
        var errors = new List<string>();

        foreach (ManifestCandidate candidate in candidates)
        {
            if (candidate.Manifest is null)
            {
                if (!string.IsNullOrWhiteSpace(candidate.Error))
                    errors.Add(candidate.Error);
                continue;
            }

            UpdateChannelManifest manifest = candidate.Manifest;
            UserPreferences.MergeUpdateBootstrapUrls(manifest.BootstrapUrls);
            if (newestManifest is null ||
                CompareVersions(manifest.Version, newestManifest.Version) > 0)
            {
                newestManifest = manifest;
                newestManifestUrl = candidate.Url;
            }
        }

        if (newestManifest is null)
        {
            string message = errors.Count == 0
                ? "更新源暂时没有提供有效版本信息"
                : string.Join(Environment.NewLine, errors);
            return new UpdateCheckResult(true, false, null, null, message);
        }

        UserPreferences.SaveLastUpdateCheckUtc(DateTimeOffset.UtcNow);
        bool available = CompareVersions(newestManifest.Version, currentVersion) > 0;
        return new UpdateCheckResult(
            true,
            available,
            newestManifest,
            newestManifestUrl,
            available ? null : "当前已是最新版本");
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateChannelManifest manifest,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (UpdatePackage package in manifest.Packages
                     .Where(IsValidPackage)
                     .OrderBy(item => item.Priority))
        {
            try
            {
                progress?.Report(new UpdateDownloadProgress(
                    0,
                    package.Size > 0 ? package.Size : null,
                    0,
                    GetPackageLabel(package)));
                return await DownloadPackageAsync(manifest.Version, package, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"更新文件处理失败：{exception.Message}", exception);
            }
            catch (Exception exception)
            {
                errors.Add($"{GetPackageLabel(package)}：{exception.Message}");
            }
        }

        throw new InvalidOperationException(
            errors.Count == 0
                ? "更新清单中没有可用的下载地址。"
                : $"更新下载失败。{string.Join("；", errors.Take(3))}");
    }

    public static int CompareVersions(string? left, string? right)
    {
        Version leftVersion = ParseVersion(left);
        Version rightVersion = ParseVersion(right);
        return leftVersion.CompareTo(rightVersion);
    }

    public static bool ValidateManifestFile(string path)
    {
        try
        {
            UpdateChannelManifest? manifest = JsonSerializer.Deserialize<UpdateChannelManifest>(
                File.ReadAllText(path),
                JsonOptions);
            return manifest is not null && IsValidManifest(manifest);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<UpdateChannelManifest?> DownloadManifestAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!IsSecureHttpUrl(url)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using HttpResponseMessage response = await Client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await JsonSerializer.DeserializeAsync<UpdateChannelManifest>(
            stream,
            JsonOptions,
            timeout.Token);
    }

    private static async Task<ManifestCandidate> DownloadManifestCandidateAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            UpdateChannelManifest? manifest = await DownloadManifestAsync(url, cancellationToken);
            if (manifest is null)
                return new ManifestCandidate(url, null, $"{GetHostLabel(url)}：没有返回版本信息");
            if (!IsValidManifest(manifest))
                return new ManifestCandidate(url, null, $"{GetHostLabel(url)}：版本清单签名无效");
            return new ManifestCandidate(url, manifest, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ManifestCandidate(url, null, $"{GetHostLabel(url)}：{exception.Message}");
        }
    }

    private static async Task<UpdateDownloadResult> DownloadPackageAsync(
        string version,
        UpdatePackage package,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string versionFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XunxianDpkViewer",
            "updates",
            SanitizePathPart(version));
        string updateFolder = Path.Combine(versionFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateFolder);

        string finalPath = Path.Combine(updateFolder, "XunxianDpkViewer.exe");
        string temporaryPath = finalPath + ".download";
        string sourceLabel = GetPackageLabel(package);

        try
        {
            byte[] actualHashBytes;
            long received = 0;
            long? totalBytes;

            using (HttpResponseMessage response = await Client.GetAsync(
                       package.Url,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                totalBytes = response.Content.Headers.ContentLength;

                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var output = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 1024 * 128,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] buffer = new byte[1024 * 128];
                    while (true)
                    {
                        int count = await input.ReadAsync(buffer, cancellationToken);
                        if (count == 0) break;

                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        hash.AppendData(buffer, 0, count);
                        received += count;
                        double? percentage = totalBytes > 0
                            ? received * 100d / totalBytes.Value
                            : null;
                        progress?.Report(new UpdateDownloadProgress(
                            received,
                            totalBytes,
                            percentage,
                            sourceLabel));
                    }

                    await output.FlushAsync(cancellationToken);
                    actualHashBytes = hash.GetHashAndReset();
                }
            }

            // All streams must be closed before the temporary file is renamed.
            string actualHash = Convert.ToHexString(actualHashBytes);
            string expectedHash = NormalizeHash(package.Sha256);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新文件的 SHA-256 校验失败，已拒绝安装。");
            if (!VerifyPackageSignature(actualHashBytes, package.Signature))
                throw new InvalidDataException("更新文件没有通过发布者签名校验，已拒绝安装。");
            if (package.Size > 0 && received != package.Size)
                throw new InvalidDataException($"更新文件大小不正确（应为 {package.Size:N0} 字节，实际 {received:N0} 字节）。");

            File.Move(temporaryPath, finalPath, overwrite: true);
            return new UpdateDownloadResult(finalPath, package);
        }
        catch
        {
            TryDeleteDirectory(updateFolder);
            throw;
        }
    }

    private static bool IsValidManifest(UpdateChannelManifest manifest)
    {
        return manifest.SchemaVersion == 1 &&
               TryParseVersion(manifest.Version, out _) &&
               manifest.Packages.Any(IsValidPackage) &&
               VerifyManifestSignature(manifest);
    }

    private static bool IsValidPackage(UpdatePackage package)
    {
        return IsSecureHttpUrl(package.Url) &&
               NormalizeHash(package.Sha256).Length == 64 &&
               TryDecodeBase64(package.Signature, out byte[]? signature) &&
               signature.Length >= 256;
    }

    private static bool IsSecureHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHash(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[7..];
        return new string(normalized.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    private static bool VerifyPackageSignature(byte[] hash, string signatureValue)
    {
        try
        {
            if (!TryDecodeBase64(signatureValue, out byte[]? signature)) return false;
            return VerifyPublisherSignature(hash, signature);
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyManifestSignature(UpdateChannelManifest manifest)
    {
        if (!TryDecodeBase64(manifest.Signature, out byte[]? signature)) return false;
        byte[] payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(BuildManifestSignaturePayload(manifest)));
        return VerifyPublisherSignature(payloadHash, signature);
    }

    private static bool VerifyPublisherSignature(byte[] hash, byte[] signature)
    {
        foreach (string publicKey in UpdateSigningPublicKeys)
        {
            try
            {
                using var rsa = new RSACryptoServiceProvider();
                rsa.ImportCspBlob(Convert.FromBase64String(publicKey));
                if (rsa.VerifyHash(
                        hash,
                        signature,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                // Try the next trusted release key.
            }
        }

        return false;
    }

    internal static string BuildManifestSignaturePayload(UpdateChannelManifest manifest)
    {
        var builder = new StringBuilder()
            .Append(manifest.SchemaVersion).Append('\n')
            .Append(manifest.Channel.Trim()).Append('\n')
            .Append(manifest.Version.Trim()).Append('\n');
        foreach (string url in manifest.BootstrapUrls)
            builder.Append("B:").Append(url.Trim()).Append('\n');
        foreach (UpdatePackage package in manifest.Packages)
        {
            builder.Append("P:")
                .Append(package.Url.Trim()).Append('|')
                .Append(NormalizeHash(package.Sha256)).Append('|')
                .Append(package.Signature.Trim()).Append('|')
                .Append(package.Size).Append('|')
                .Append(package.Priority).Append('\n');
        }
        return builder.ToString();
    }

    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value ?? string.Empty);
            return bytes.Length > 0;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    private static Version ParseVersion(string? value)
    {
        return TryParseVersion(value, out Version? version)
            ? version
            : new Version(0, 0, 0, 0);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        string normalized = (value ?? string.Empty).Trim().TrimStart('v', 'V');
        int suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0) normalized = normalized[..suffixIndex];

        if (!Version.TryParse(normalized, out Version? parsed))
        {
            version = new Version(0, 0, 0, 0);
            return false;
        }

        version = new Version(
            Math.Max(0, parsed.Major),
            Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build),
            Math.Max(0, parsed.Revision));
        return true;
    }

    private static string SanitizePathPart(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Where(character => !invalid.Contains(character)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string GetHostLabel(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
    }

    private static string GetPackageLabel(UpdatePackage package)
    {
        return string.IsNullOrWhiteSpace(package.Label)
            ? GetHostLabel(package.Url)
            : package.Label.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A later download attempt can choose another version folder.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup is retried after the updater exits.
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(12)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        string version = typeof(UpdateService).Assembly.GetName().Version?.ToString(2) ?? "1.0";
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("XunxianDpkViewer", version));
        return client;
    }

    private sealed record ManifestCandidate(
        string Url,
        UpdateChannelManifest? Manifest,
        string? Error);
}
