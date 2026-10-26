using CMCLauncher;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ModrinthFilterTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run(string root)
    {
        var requests = new List<Uri>();
        using var core = new LauncherCore(new Store(root), new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/v2/tag/game_version" => """[{"version":"1.21.1","version_type":"release"},{"version":"26.4-snapshot-5","version_type":"snapshot"},{"version":"1.21-pre1","version_type":"snapshot"}]""",
                "/v2/tag/category" => """[{"name":"adventure","project_type":"mod","header":"categories"}]""",
                "/v2/tag/loader" => """[{"name":"iris","supported_project_types":["shader"]}]""",
                "/v2/tag/license" => """[{"short":"MIT","name":"MIT License"}]""",
                _ => """{"hits":[],"total_hits":0}"""
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));
        var service = new ModrinthService(core, new Store(root));
        var catalog = await service.FilterCatalog(CancellationToken.None);
        Check(catalog.GameVersions.Any(v => v.Id == "1.21.1" && v.Type == "release") && catalog.GameVersions.Any(v => v.Id == "26.4-snapshot-5" && v.Type == "snapshot") && catalog.GameVersions.Any(v => v.Id == "1.21-pre1" && v.Type == "snapshot") && catalog.Categories.Single().Header == "categories", "Public version/category metadata was lost");
        Check(catalog.Loaders.Single().ProjectTypes.Single() == "shader" && catalog.Licenses.Single().Id == "MIT", "Resource-specific loader/license metadata was lost");
        var count = requests.Count;
        Check(ReferenceEquals(catalog, await service.FilterCatalog(CancellationToken.None)) && requests.Count == count, "Complete metadata should be cached");
        foreach (var type in new[] { "mod", "modpack", "datapack", "resourcepack", "shader" })
        {
            var filters = new CommunitySearchFilters("1.21.1", type == "shader" ? "iris" : "fabric", ["adventure", "16x"],
                "client_only", "MIT", "true", "updated", "author with spaces", "name: OR downloads > 0", "abc",
                "42", "5", "2026-01-01", "2026-02-01", ["ai_content", "telemetry"]);
            await service.SearchPage("hello & #", type, CancellationToken.None, 16, 16, filters);
            var parameters = requests.Last().Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            Check(parameters["index"] == "updated" && parameters["offset"] == "16" && parameters["query"] == "hello & #", "Filters lost sorting, query, or pagination");
            var groups = JsonSerializer.Deserialize<string[][]>(parameters["facets"])!;
            Check(groups.All(g => g.Length == 1), "Different filter fields must be ANDed");
            var facets = groups.SelectMany(g => g).ToArray();
            Check(facets.Contains("project_type:" + type) && facets.Contains("versions:\"1.21.1\"") && facets.Contains("categories:\"16x\""), "Type/version/category facets missing");
            Check(facets.Contains("categories:\"fabric\"") == (type is "mod" or "modpack"), "Inapplicable mod loader was sent");
            Check(facets.Contains("categories:\"iris\"") == (type == "shader"), "Shader loader was lost");
            Check(facets.Contains("environment:\"client_only\"") && facets.Contains("license:\"MIT\"") && facets.Contains("open_source:true"), "Environment/license/source filter missing");
            Check(facets.Contains("title:" + JsonSerializer.Serialize(filters.Title)) && facets.Contains("author:\"author with spaces\""), "Text facet values must be quoted");
            Check(facets.Contains("downloads>=42") && facets.Contains("follows>=5") && facets.Any(f => f.StartsWith("created_timestamp>=")), "Count/date filters missing");
            Check(facets.Contains("disclosure_types:\"telemetry\""), "Content disclosure filter missing");
        }
        foreach (var invalid in new[]
        {
            new CommunitySearchFilters(MinDownloads: "-1"), new CommunitySearchFilters(MinFollows: "1.5"),
            new CommunitySearchFilters(CreatedAfter: "not-a-date"), new CommunitySearchFilters(Sort: "invalid"),
            new CommunitySearchFilters(OpenSource: "maybe")
        })
        {
            var before = requests.Count;
            try { await service.SearchPage("", "mod", CancellationToken.None, filters: invalid); throw new Exception("Invalid filter accepted"); }
            catch (ArgumentException) { }
            Check(requests.Count == before, "Invalid filter should fail before making a request");
        }
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
