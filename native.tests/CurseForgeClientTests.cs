using CMCLauncher;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class CurseForgeClientTests
{
    const string TestKey = "fixture-key-not-a-real-credential";
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json")
    };
    static async Task<T> Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static CurseForgeProject Project(bool? distribution = true, bool available = true) =>
        new(7, 432, "Fixture", "Summary", distribution, available, new("https://www.curseforge.com/minecraft/mc-mods/fixture"),
            [new(1, "Author", "https://www.curseforge.com/members/fixture")]);
    static CurseForgeFile File(string? url = "https://edge.forgecdn.net/files/1/2/fixture.jar", bool available = true, bool earlyAccess = false) =>
        new(9, 432, 7, "Fixture file", "fixture.jar", available, earlyAccess, 123, url, ["1.21.1"], [new(new string('a', 40), 1)]);

    public static async Task Run()
    {
        using (var transport = CurseForgeClient.CreateTransport())
            Check(!transport.UseProxy && !transport.AllowAutoRedirect && !transport.UseCookies &&
                transport.SslOptions.RemoteCertificateValidationCallback != null, "CurseForge transport must remain direct, redirect-free and certificate-validated");

        var calls = 0; var keyReads = 0;
        using (var disabled = new CurseForgeClient(() => { keyReads++; return TestKey; }, false,
            new Handler(_ => { calls++; return Json(new { }); })))
        {
            Check(!disabled.IsEnabled, "CurseForge should be disabled by default");
            await Reject<InvalidOperationException>(() => disabled.SearchAsync("fixture"));
            Check(calls == 0 && keyReads == 0, "Disabled integration must neither read credentials nor make requests");
        }
        using (var missingKey = new CurseForgeClient(() => null, true, new Handler(_ => { calls++; return Json(new { }); })))
        {
            await Reject<InvalidOperationException>(() => missingKey.GetProjectAsync(7));
            Check(calls == 0, "Missing API key must fail before sending a request");
        }

        // 所有请求只到模拟传输；即使测试失败，也不需要真实密钥或 CurseForge 服务。
        using (var client = new CurseForgeClient(() => TestKey, true, new Handler(request =>
        {
            calls++;
            Check(request.RequestUri!.Scheme == "https" && request.RequestUri.Host == "api.curseforge.com", "Unexpected API origin");
            Check(request.Headers.GetValues("x-api-key").Single() == TestKey, "Missing per-request authentication");
            Check(!request.RequestUri.AbsoluteUri.Contains(TestKey), "Credential leaked into URL");
            Check(request.Headers.CacheControl is { NoCache: true, NoStore: true }, "Metadata must not be cached");
            Check(request.Headers.UserAgent.ToString().Contains("CMCLauncher/"), "Missing application identity");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/v1/mods/search")
            {
                Check(request.RequestUri.Query.Contains("gameId=432") && request.RequestUri.Query.Contains("searchFilter=a%26b%20%23"), "Search filters must be escaped");
                return Json(new { data = new[] { Project() }, pagination = new CurseForgePagination(0, 20, 1, 1) });
            }
            if (path == "/v1/mods/7/files")
                return Json(new { data = new[] { File() }, pagination = new CurseForgePagination(0, 20, 1, 1) });
            if (path == "/v1/mods/7") return Json(new { data = Project() });
            if (path == "/v1/mods/7/files/9") return Json(new { data = File() });
            throw new Exception("Unexpected API route");
        })))
        {
            var search = await client.SearchAsync("a&b #");
            Check(search.Data.Single().Authors!.Single().Name == "Author" && search.Pagination.TotalCount == 1, "Project attribution or pagination lost");
            Check((await client.GetFilesAsync(7)).Data.Single().Hashes!.Single().Algo == 1, "File hashes lost");
            Check((await client.GetFileAccessAsync(7, 9)).DownloadUri?.Host == "edge.forgecdn.net", "Official permitted URL not exposed");
            var previous = calls;
            await Reject<ArgumentOutOfRangeException>(() => client.SearchAsync("", pageSize: 51));
            await Reject<ArgumentOutOfRangeException>(() => client.SearchAsync("", index: int.MaxValue));
            await Reject<ArgumentOutOfRangeException>(() => client.GetFilesAsync(0));
            await Reject<ArgumentOutOfRangeException>(() => client.GetFileAccessAsync(7, 0));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Reject<OperationCanceledException>(() => client.SearchAsync("", ct: cancellation.Token));
            Check(calls == previous, "Invalid input or cancellation caused network activity");
        }

        foreach (var project in new[] { Project(false), Project(null), Project(available: false) })
        {
            var requests = 0;
            using var denied = new CurseForgeClient(() => TestKey, true, new Handler(_ => { requests++; return Json(new { data = project }); }));
            var access = await denied.GetFileAccessAsync(7, 9);
            Check(!access.IsAllowed && access.DownloadUri == null && requests == 1, "Denied/unknown distribution must not query files or expose downloads");
        }

        var deniedFiles = new[]
        {
            File(null), File(""), File(available: false), File(earlyAccess: true),
            File("http://edge.forgecdn.net/files/1/2/f.jar"), File("https://edge.forgecdn.net.evil.example/f.jar"),
            File("https://user@edge.forgecdn.net/f.jar"), File("https://edge.forgecdn.net:444/f.jar"),
            File("https://edge.forgecdn.net/f.jar?api-key=secret"), File("https://edge.forgecdn.net/f.jar#fragment")
        };
        foreach (var file in deniedFiles)
        {
            using var denied = new CurseForgeClient(() => TestKey, true, new Handler(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/files/9") ? Json(new { data = file }) : Json(new { data = Project() })));
            var access = await denied.GetFileAccessAsync(7, 9);
            Check(!access.IsAllowed && access.DownloadUri == null, "Unavailable or unreviewed URL exposed");
        }
        using (var mismatch = new CurseForgeClient(() => TestKey, true, new Handler(_ => Json(new { data = File() with { ModId = 8 } }))))
            await Reject<InvalidDataException>(() => mismatch.GetFileAsync(7, 9));
        using (var mismatch = new CurseForgeClient(() => TestKey, true, new Handler(_ => Json(new { data = Project() with { GameId = 1 } }))))
            await Reject<InvalidDataException>(() => mismatch.GetProjectAsync(7));

        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.TemporaryRedirect })
        {
            var requests = 0;
            using var failing = new CurseForgeClient(() => TestKey, true, new Handler(_ =>
            {
                requests++;
                var response = new HttpResponseMessage(status) { Content = new StringContent(TestKey) };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                response.Headers.Location = new Uri("https://unexpected.example/");
                return response;
            }));
            var error = await Reject<CurseForgeApiException>(() => failing.GetProjectAsync(7));
            Check(error.StatusCode == status && error.RetryAfter == TimeSpan.FromSeconds(30), "HTTP failure details lost");
            Check(requests == 1 && !error.ToString().Contains(TestKey), "Failure retried, redirected or leaked credentials");
        }
        using (var malformed = new CurseForgeClient(() => TestKey, true, new Handler(_ =>
            new(HttpStatusCode.OK) { Content = new StringContent("not JSON " + TestKey) })))
        {
            var error = await Reject<InvalidDataException>(() => malformed.GetProjectAsync(7));
            Check(!error.ToString().Contains(TestKey), "Parser error leaked response content");
        }
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
