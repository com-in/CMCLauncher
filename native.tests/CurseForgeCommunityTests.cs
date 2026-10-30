using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class CurseForgeCommunityTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static async Task Reject(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception("Expected CurseForge request rejection.");
    }

    public static async Task Run(string root)
    {
        var store = new Store(root);
        using var core = new LauncherCore(
            store,
            new Handler(request =>
            {
                var start = Parameter(request.RequestUri!, "offset");
                var count = Parameter(request.RequestUri!, "limit");
                var hits = new JsonArray(
                    Enumerable
                        .Range(Math.Min(start, 3), Math.Min(count, Math.Max(0, 3 - start)))
                        .Select(i =>
                            (JsonNode)
                                new JsonObject
                                {
                                    ["project_id"] = "mr" + i,
                                    ["title"] = "Modrinth " + i,
                                }
                        )
                        .ToArray()
                );
                return Json(new JsonObject { ["hits"] = hits, ["total_hits"] = 3 }.ToJsonString());
            })
        );
        var modrinth = new ModrinthService(core, store);
        var denied = false;
        var badHash = false;
        var calls = new List<Uri>();
        using var api = new CurseForgeClient(
            () => "fixture-not-a-real-key",
            true,
            new Handler(request =>
            {
                Check(
                    request.Headers.GetValues("x-api-key").Single() == "fixture-not-a-real-key",
                    "Missing per-request authentication"
                );
                var uri = request.RequestUri!;
                calls.Add(uri);
                if (uri.AbsolutePath.EndsWith("/mods/8/files"))
                {
                    var offset = Parameter(uri, "index");
                    var limit = Parameter(uri, "pageSize");
                    return Json(
                        new JsonObject
                        {
                            ["data"] = new JsonArray(
                                Enumerable
                                    .Range(offset, limit)
                                    .Select(i =>
                                        (JsonNode)
                                            new JsonObject
                                            {
                                                ["id"] = i + 1,
                                                ["modId"] = 8,
                                                ["gameId"] = 432,
                                                ["isAvailable"] = i % 2 == 0,
                                                ["gameVersions"] = new JsonArray(
                                                    "1.21.1",
                                                    "NeoForge"
                                                ),
                                            }
                                    )
                                    .ToArray()
                            ),
                            ["pagination"] = new JsonObject { ["totalCount"] = 10000 },
                        }.ToJsonString()
                    );
                }
                if (uri.Host == "edge.forgecdn.net")
                    return new(HttpStatusCode.OK)
                    {
                        Content = new StringContent("abc", Encoding.UTF8),
                    };
                if (uri.AbsolutePath.EndsWith("/categories"))
                    return Json("""{"data":[{"id":6,"slug":"mc-mods","name":"Mods"}]}""");
                if (uri.AbsolutePath.EndsWith("/mods/search"))
                {
                    var start = Parameter(uri, "index");
                    var count = Parameter(uri, "pageSize");
                    var hits = new JsonArray(
                        Enumerable
                            .Range(Math.Min(start, 33), Math.Min(count, Math.Max(0, 33 - start)))
                            .Select(i =>
                                (JsonNode)
                                    new JsonObject
                                    {
                                        ["id"] = 7 + i,
                                        ["gameId"] = 432,
                                        ["name"] = "Fixture " + i,
                                    }
                            )
                            .ToArray()
                    );
                    return Json(
                        new JsonObject
                        {
                            ["data"] = hits,
                            ["pagination"] = new JsonObject { ["totalCount"] = 33 },
                        }.ToJsonString()
                    );
                }
                if (uri.AbsolutePath.EndsWith("/description"))
                    return Json("""{"data":"<p>First</p><p>Second &amp; third</p>"}""");
                if (uri.AbsolutePath.EndsWith("/mods/7"))
                    return Json(
                        "{\"data\":{\"id\":7,\"gameId\":432,\"classId\":6,\"name\":\"Fixture\",\"allowModDistribution\":"
                            + (!denied).ToString().ToLowerInvariant()
                            + ",\"authors\":[{\"name\":\"Author\"}]}}"
                    );
                var file = new JsonObject
                {
                    ["id"] = 9,
                    ["modId"] = 7,
                    ["gameId"] = 432,
                    ["isAvailable"] = true,
                    ["fileName"] = "fixture.jar",
                    ["displayName"] = "Fixture file",
                    ["releaseType"] = 1,
                    ["fileLength"] = 3,
                    ["downloadUrl"] = "https://edge.forgecdn.net/files/1/2/fixture.jar",
                    ["gameVersions"] = new JsonArray("1.21.1", "NeoForge"),
                    ["hashes"] = new JsonArray(
                        new JsonObject
                        {
                            ["algo"] = 1,
                            ["value"] = badHash
                                ? new string('0', 40)
                                : "a9993e364706816aba3e25717850c26c9cd0d89d",
                        }
                    ),
                };
                return uri.AbsolutePath.EndsWith("/files")
                    ? Json(
                        new JsonObject
                        {
                            ["data"] = new JsonArray(file),
                            ["pagination"] = new JsonObject { ["totalCount"] = 1 },
                        }.ToJsonString()
                    )
                    : Json(new JsonObject { ["data"] = file }.ToJsonString());
            })
        );
        using var service = new CurseForgeCommunityService(core, store, modrinth, api);
        var search = await service.SearchPage(
            "hello world",
            "mod",
            default,
            16,
            16,
            new(GameVersion: "1.21.1", Loader: "neoforge", Categories: ["12"], Source: "curseforge")
        );
        Check(
            search.TotalHits == 33
                && search.Hits.Count == 16
                && search.Hits[0].Id == "curseforge:23"
                && search.Hits[0].Source == "curseforge",
            "Source identity or total lost"
        );
        var query = Uri.UnescapeDataString(calls.Last().Query);
        Check(
            query.Contains("index=16")
                && query.Contains("modLoaderType=6")
                && query.Contains("categoryIds=[12]")
                && query.Contains("searchFilter=hello world"),
            "CurseForge filters or pagination lost"
        );
        var details = await service.ProjectDetails("curseforge:7", default);
        Check(
            details.Authors == "Author"
                && details.Body.Contains("Second & third")
                && !details.Body.Contains('<'),
            "Project attribution or description lost"
        );
        var versions = await service.ProjectVersions("curseforge:7", default);
        Check(
            versions.Single().Loaders.SequenceEqual(["neoforge"])
                && versions[0].Id == "curseforge:9",
            "Version loader or identity lost"
        );
        Directory.CreateDirectory(root);
        var beforePage = calls.Count;
        var largePage = await service.ProjectVersionPage("curseforge:8", 0, 50, default);
        Check(
            calls.Count == beforePage + 1
                && largePage.NextOffset == 50
                && largePage.TotalHits == 10000
                && largePage.Versions.Count == 25
                && largePage.HasNext,
            "Large CurseForge projects fetch one bounded page and advance past filtered files"
        );
        var nextLargePage = await service.ProjectVersionPage(
            "curseforge:8",
            largePage.NextOffset,
            50,
            default
        );
        Check(
            nextLargePage.Offset == 50
                && nextLargePage.NextOffset == 100
                && !nextLargePage
                    .Versions.Select(v => v.Id)
                    .Intersect(largePage.Versions.Select(v => v.Id))
                    .Any(),
            "CurseForge version pagination does not repeat files"
        );
        var target = Path.Combine(root, "fixture.jar");
        await service.DownloadFile(7, 9, target, default);
        Check(System.IO.File.ReadAllText(target) == "abc", "Authenticated download failed");
        System.IO.File.Delete(target);
        denied = true;
        var before = calls.Count(u => u.Host == "edge.forgecdn.net");
        await Reject(() => service.DownloadFile(7, 9, target, default));
        Check(
            calls.Count(u => u.Host == "edge.forgecdn.net") == before,
            "Distribution denial reached CDN"
        );
        denied = false;
        badHash = true;
        await Reject(() => service.DownloadFile(7, 9, target, default));
        Check(
            !System.IO.File.Exists(target) && !Directory.GetFiles(root, "*.part").Any(),
            "Failed download left a partial file"
        );
        var callsBeforeUnsafeUrl = calls.Count;
        await Reject(() =>
            api.DownloadAuthenticated(
                new Uri("https://example.com/fixture.jar"),
                target,
                new string('a', 40),
                3,
                default
            )
        );
        Check(
            calls.Count == callsBeforeUnsafeUrl,
            "An untrusted CDN must not receive authentication"
        );
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                await service.DownloadFile(7, 9, target, cancelled.Token);
                throw new Exception("Cancelled download succeeded");
            }
            catch (OperationCanceledException) { }
        }
        Check(
            !System.IO.File.Exists(target) && !Directory.GetFiles(root, "*.part").Any(),
            "Cancellation left an installed or partial file"
        );
        var community = new CommunityService(modrinth, service);
        var merged = new List<string>();
        for (var offset = 0; offset < 36; offset += 16)
        {
            var result = await community.SearchPage(
                "fixture",
                "mod",
                default,
                offset,
                16,
                new(Source: "all")
            );
            Check(
                result.TotalHits == 36 && result.Offset == offset,
                "Combined source totals or page offset lost"
            );
            merged.AddRange(result.Hits.Select(h => h.Id));
        }
        Check(
            merged.Count == 36
                && merged.Distinct().Count() == 36
                && merged
                    .Take(6)
                    .SequenceEqual([
                        "mr0",
                        "curseforge:7",
                        "mr1",
                        "curseforge:8",
                        "mr2",
                        "curseforge:9",
                    ]),
            "Combined pages skip, repeat or misorder projects"
        );
        using var unavailable = new CurseForgeCommunityService(
            core,
            store,
            modrinth,
            new CurseForgeClient(() => null, false)
        );
        var fallback = await new CommunityService(modrinth, unavailable).SearchPage(
            "fixture",
            "mod",
            default,
            0,
            16,
            new(Source: "all")
        );
        Check(
            fallback.Hits.Count == 3 && fallback.Warning?.Contains("CurseForge") == true,
            "Unavailable source must leave usable results and an explicit warning"
        );
        foreach (var a in new[] { 0, 1, 3, 16, 33 })
        foreach (var b in new[] { 0, 1, 4, 17, 34 })
        {
            var expected = new List<bool>();
            for (var i = 0; i < Math.Max(a, b); i++)
            {
                if (i < a)
                    expected.Add(false);
                if (i < b)
                    expected.Add(true);
            }
            for (var n = 0; n <= expected.Count; n++)
            {
                var count = CommunityService.Counts(n, a, b);
                Check(
                    count.A == expected.Take(n).Count(v => !v)
                        && count.B == expected.Take(n).Count(v => v),
                    "Combined source page boundary is unstable"
                );
            }
        }
    }

    static HttpResponseMessage Json(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/json"),
        };

    static int Parameter(Uri uri, string name) =>
        int.Parse(
            uri
                .Query.TrimStart('?')
                .Split('&')
                .Single(p => p.StartsWith(name + "=", StringComparison.Ordinal))
                .Split('=')[1]
        );

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(response(request));
    }
}
