using CMCLauncher;
using System.Net;
using System.Net.Http;

static class FrpcFallbackTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(handler(request)); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run(string root)
    {
        foreach (var code in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            var store = new Store(Path.Combine(root, ((int)code).ToString()));
            var requests = 0; var offline = false; var hash = new string('a', 64);
            using var core = new LauncherCore(store, new Handler(request =>
            {
                if (request.RequestUri!.Host == "api.github.com")
                {
                    requests++; var limited = new HttpResponseMessage(code);
                    limited.Headers.Add("X-RateLimit-Remaining", "0"); limited.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());
                    return limited;
                }
                if (offline) return new(HttpStatusCode.ServiceUnavailable);
                if (request.RequestUri.AbsolutePath.EndsWith("checksums.txt")) return new(HttpStatusCode.OK) { Content = new StringContent(hash + "  frp_0.71.1_windows_amd64.zip\n") };
                if (request.Method == HttpMethod.Head)
                {
                    var content = new ByteArrayContent([]); content.Headers.ContentLength = 1234;
                    return new(HttpStatusCode.OK) { Content = content };
                }
                return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.Query == "?page=2" ? "<a href='/fatedier/frp/releases/tag/v0.70.0'>old</a>" : "<a href=\"/fatedier/frp/releases/tag/v0.71.1\">latest</a><a href='/fatedier/frp/releases/tag/v0.71.1'>duplicate</a><a href='/other/repo/releases/tag/v9.9.9'>untrusted</a><a rel='next'>Next</a>") };
            }));
            using (var manager = new FrpcManager(store, core))
            {
                var first = await manager.Releases(1, default);
                Check(first.Source == "web" && first.Versions.SequenceEqual(["0.71.1"]) && first.HasMore, "403/429 falls back to exact official release links");
                var second = await manager.Releases(2, default);
                Check(second.Versions.SequenceEqual(["0.70.0"]) && !second.HasMore && requests == 1, "API cooldown prevents repeated calls while webpage pagination continues");
                var release = await manager.FindRelease("0.71.1", default);
                Check(release.Sha256 == hash && release.Size == 1234 && release.Url.StartsWith("https://github.com/fatedier/frp/releases/download/"), "Download metadata also falls back with official SHA-256 intact");
            }
            offline = true;
            using (var manager = new FrpcManager(store, core))
            {
                var cached = await manager.Releases(1, default);
                Check(cached.IsCached && cached.Versions.SequenceEqual(["0.71.1"]), "Saved online catalog survives restart and complete network failure");
                Check((await manager.FindRelease("0.71.1", default)).Sha256 == hash, "Release integrity metadata has an offline fallback");
                try { await manager.Releases(1, new CancellationToken(true)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
            }
        }
    }
}
