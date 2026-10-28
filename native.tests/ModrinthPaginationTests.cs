using System.Net;
using System.Net.Http;
using System.Text.Json;
using CMCLauncher;

internal static class ModrinthPaginationTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        var requests = 0;
        using var core = new LauncherCore(
            new Store(root),
            new Handler(request =>
            {
                requests++;
                var parameters = request
                    .RequestUri!.Query.TrimStart('?')
                    .Split('&')
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
                var query = parameters["query"];
                var offset = int.Parse(parameters["offset"]);
                var limit = int.Parse(parameters["limit"]);
                Check(
                    parameters["index"] == (query.Length == 0 ? "downloads" : "relevance"),
                    "Pagination changed search ordering"
                );
                Check(
                    parameters["facets"].Contains("project_type:"),
                    "Pagination lost project type"
                );
                var total =
                    query == "empty" ? 0
                    : query == "full" ? 32
                    : 33;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(
                            new
                            {
                                hits = Enumerable
                                    .Range(offset, Math.Min(limit, Math.Max(0, total - offset)))
                                    .Select(id => new
                                    {
                                        project_id = id.ToString(),
                                        title = "Item " + id,
                                        description = "Fixture",
                                        icon_url = "",
                                        downloads = 1000 - id,
                                    }),
                                offset,
                                limit,
                                total_hits = total,
                            }
                        )
                    ),
                };
            })
        );
        var service = new ModrinthService(core, new Store(root));
        foreach (var type in new[] { "mod", "modpack", "resourcepack", "datapack", "shader" })
        foreach (var query in new[] { "", "term & #" })
        {
            var first = await service.SearchPage(query, type, CancellationToken.None);
            var second = await service.SearchPage(query, type, CancellationToken.None, 16);
            var last = await service.SearchPage(query, type, CancellationToken.None, 32);
            Check(
                first.Hits.Count == 16
                    && first.HasNext
                    && first.TotalHits == 33
                    && first.Offset == 0,
                "First page metadata incorrect"
            );
            Check(
                second.Hits[0].Id == "16" && second.HasNext && second.Offset == 16,
                "Second page did not advance offset"
            );
            Check(
                !first.Hits.Select(h => h.Id).Intersect(second.Hits.Select(h => h.Id)).Any(),
                "Pages repeated the same projects"
            );
            Check(
                last.Hits.Count == 1 && !last.HasNext && last.TotalHits == 33,
                "Partial last page metadata incorrect"
            );
        }
        Check(
            !(await service.SearchPage("full", "mod", CancellationToken.None, 16)).HasNext,
            "Full last page must disable next using total_hits"
        );
        var empty = await service.SearchPage("empty", "mod", CancellationToken.None);
        Check(
            empty.Hits.Count == 0 && empty.TotalHits == 0 && !empty.HasNext,
            "Empty search must not paginate"
        );
        var before = requests;
        try
        {
            await service.SearchPage("", "mod", CancellationToken.None, -1);
            throw new Exception("Negative offset accepted");
        }
        catch (ArgumentOutOfRangeException) { }
        try
        {
            await service.SearchPage("", "mod", CancellationToken.None, limit: 101);
            throw new Exception("Oversized page accepted");
        }
        catch (ArgumentOutOfRangeException) { }
        Check(requests == before, "Invalid pagination made a request");
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(respond(request));
    }
}
