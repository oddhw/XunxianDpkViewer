using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XunxianDpkViewer.Core;

if (args.Length != 2)
    throw new ArgumentException("Pass a signed manifest and its matching EXE.");

string manifestJson = File.ReadAllText(args[0]);
UpdateChannelManifest manifest = JsonSerializer.Deserialize<UpdateChannelManifest>(manifestJson)!;
string manifestUrl = "https://api.github.com/repos/oddhw/XunxianDpkViewer/contents/update/stable.json?ref=main";
string rawUrl = "https://raw.githubusercontent.com/oddhw/XunxianDpkViewer/main/update/stable.json";
string assetUrl = "https://api.github.com/repos/oddhw/XunxianDpkViewer/releases/assets/12345";
TimeSpan requestTimeout = TimeSpan.FromMilliseconds(150);

void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

async Task Run(string name, Func<Task> check)
{
    await check();
    Console.WriteLine("PASS " + name);
}

await Run("GitHub contents envelope and retired cached URLs", async () =>
{
    UserPreferences.BootstrapUrls = new[] { "https://gitcode.com/oddhw/XunxianDpkViewer/raw/main/update/stable.json",
        "https://gitee.com/oddhw/XunxianDpkViewer/raw/master/update/stable.json" };
    using var handler = new FakeHandler((request, _) =>
    {
        Require(request.RequestUri!.AbsoluteUri == manifestUrl, "A retired source was requested.");
        return Task.FromResult(JsonResponse(JsonSerializer.Serialize(new
        {
            encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestJson))
        })));
    });
    using var client = new HttpClient(handler);
    var service = new UpdateService(client, new[] { manifestUrl }, requestTimeout);
    UpdateCheckResult result = await service.CheckAsync("3.1.1", true);
    Require(result.IsUpdateAvailable && result.Manifest?.Version == manifest.Version, "The envelope was not validated.");
    UserPreferences.BootstrapUrls = Array.Empty<string>();
});

await Run("Timed-out request retries and accepts signed raw JSON", async () =>
{
    int attempts = 0;
    using var handler = new FakeHandler(async (_, token) =>
    {
        if (++attempts == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return JsonResponse(manifestJson);
    });
    using var client = new HttpClient(handler);
    var service = new UpdateService(client, new[] { manifestUrl }, requestTimeout);
    Require((await service.CheckAsync("3.1.1", true)).IsUpdateAvailable && attempts == 2, "Timeout retry failed.");
});

await Run("HTML and 404 sources cannot hide a valid signed source", async () =>
{
    using var handler = new FakeHandler((request, _) => Task.FromResult(
        request.RequestUri!.AbsoluteUri == rawUrl ? JsonResponse(manifestJson) :
        request.RequestUri.Host == "html.invalid" ? JsonResponse("<html>sign in</html>") :
        new HttpResponseMessage(HttpStatusCode.NotFound)));
    using var client = new HttpClient(handler);
    var service = new UpdateService(client, new[] { "https://html.invalid/", "https://missing.invalid/", rawUrl }, requestTimeout);
    Require((await service.CheckAsync("3.1.1", true)).IsUpdateAvailable, "A failed source hid the signed manifest.");
});

await Run("Proxy connection failure uses direct fallback", async () =>
{
    using var proxyHandler = new FakeHandler((_, _) => throw new HttpRequestException("Proxy connection failed."));
    using var directHandler = new FakeHandler((_, _) => Task.FromResult(JsonResponse(manifestJson)));
    using var proxyClient = new HttpClient(proxyHandler);
    using var directClient = new HttpClient(directHandler);
    var service = new UpdateService(proxyClient, new[] { rawUrl }, requestTimeout, directClient);
    Require((await service.CheckAsync("3.1.1", true)).IsUpdateAvailable, "Direct fallback failed.");
});

await Run("GitHub API direct failure uses system proxy fallback", async () =>
{
    using var proxyHandler = new FakeHandler((_, _) => Task.FromResult(JsonResponse(manifestJson)));
    using var directHandler = new FakeHandler((_, _) => throw new HttpRequestException("Direct connection failed."));
    using var proxyClient = new HttpClient(proxyHandler);
    using var directClient = new HttpClient(directHandler);
    var service = new UpdateService(proxyClient, new[] { manifestUrl }, requestTimeout, directClient);
    Require((await service.CheckAsync("3.1.1", true)).IsUpdateAvailable, "System proxy fallback failed.");
});

await Run("Modified version fails publisher verification", async () =>
{
    JsonNode modified = JsonNode.Parse(manifestJson)!;
    modified["version"] = "99.0";
    using var handler = new FakeHandler((_, _) => Task.FromResult(JsonResponse(modified.ToJsonString())));
    using var client = new HttpClient(handler);
    var service = new UpdateService(client, new[] { manifestUrl }, requestTimeout);
    Require((await service.CheckAsync("3.1.1", true)).Manifest is null, "Unsigned version modification was accepted.");
});

await Run("Caller cancellation is preserved", async () =>
{
    using var handler = new FakeHandler(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return JsonResponse(manifestJson);
    });
    using var client = new HttpClient(handler);
    var service = new UpdateService(client, new[] { manifestUrl }, TimeSpan.FromSeconds(10));
    using var cancellation = new CancellationTokenSource(50);
    try { await service.CheckAsync("3.1.1", true, cancellation.Token); }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
    throw new InvalidOperationException("Cancellation was swallowed.");
});

string releaseMetadata = JsonSerializer.Serialize(new
{
    assets = new[] { new { name = "XunxianDpkViewer.exe", url = assetUrl } }
});

foreach (string apiBehavior in new[] { "success", "timeout", "disconnected" })
{
    bool useOriginal = apiBehavior != "success";
    await Run(useOriginal ? $"API body {apiBehavior} falls back to signed original URL" : "API asset downloads with signed package validation", async () =>
    {
        bool apiRequested = false;
        bool originalRequested = false;
        using var handler = new FakeHandler((request, _) =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("/releases/tags/", StringComparison.Ordinal)) return Task.FromResult(JsonResponse(releaseMetadata));
            if (url == assetUrl)
            {
                apiRequested = true;
                Require(request.Headers.Accept.Any(value => value.MediaType == "application/octet-stream"), "Missing API binary header.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(useOriginal ? new StallingStream(apiBehavior == "disconnected") : File.OpenRead(args[1]))
                });
            }
            Require(url == manifest.Packages[0].Url, "Unexpected package URL.");
            originalRequested = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(args[1])) });
        });
        using var client = new HttpClient(handler);
        var service = new UpdateService(client, Array.Empty<string>(), requestTimeout);
        UpdateDownloadResult result = await service.DownloadAsync(manifest);
        Require(apiRequested && originalRequested == useOriginal, "The expected transport fallback was not used.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.FilePath))) == manifest.Packages[0].Sha256,
            "Downloaded package differs from the signed release.");
        File.Delete(result.FilePath);
        Directory.Delete(Path.GetDirectoryName(result.FilePath)!);
    });
}

await Run("Corrupt API and original packages are rejected", async () =>
{
    using var handler = new FakeHandler((request, _) => Task.FromResult(
        request.RequestUri!.AbsolutePath.Contains("/releases/tags/", StringComparison.Ordinal) ? JsonResponse(releaseMetadata) :
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) }));
    using var client = new HttpClient(handler);
    var service = new UpdateService(client, Array.Empty<string>(), requestTimeout);
    try { await service.DownloadAsync(manifest); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("A corrupt update package was accepted.");
});

Console.WriteLine("UPDATE SOURCE CHECKS PASSED");

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

sealed class StallingStream(bool disconnected = false) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => WaitAsync(token);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => new(WaitAsync(token));
    private async Task<int> WaitAsync(CancellationToken token)
    {
        if (disconnected) throw new IOException("Connection closed.");
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return 0;
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
