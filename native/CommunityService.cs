namespace CMCLauncher;

public sealed class CommunityService(
    ModrinthService modrinth,
    CurseForgeCommunityService curseforge
)
{
    public Task<CommunityProject> ProjectDetails(string id, CancellationToken ct) =>
        CurseForgeCommunityService.IsProject(id)
            ? curseforge.ProjectDetails(id, ct)
            : modrinth.ProjectDetails(id, ct);

    public Task<List<CommunityVersion>> ProjectVersions(string id, CancellationToken ct) =>
        CurseForgeCommunityService.IsProject(id)
            ? curseforge.ProjectVersions(id, ct)
            : modrinth.ProjectVersions(id, ct);

    // Alternate sources, then continue with the longer list. Counts keep page jumps stable.
    internal static (int A, int B) Counts(int n, int a, int b)
    {
        var paired = (int)Math.Min(n, 2L * Math.Min(a, b));
        var extra = n - paired;
        return ((paired + 1) / 2 + (a > b ? extra : 0), paired / 2 + (b >= a ? extra : 0));
    }

    public async Task<CommunitySearchPage> SearchPage(
        string query,
        string type,
        CancellationToken ct,
        int offset,
        int limit,
        CommunitySearchFilters? filters = null
    )
    {
        filters ??= new();
        if (offset < 0 || limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Task<CommunitySearchPage> Fetch(bool cf, int start, int count) =>
            cf
                ? curseforge.SearchPage(query, type, ct, start, count, filters)
                : modrinth.SearchPage(query, type, ct, start, count, filters);
        if (filters.Source == "modrinth")
            return await Fetch(false, offset, limit);
        if (filters.Source == "curseforge")
            return await Fetch(true, offset, limit);
        if (filters.Source != "all")
            throw new ArgumentException("Unknown community source.");
        async Task<CommunitySearchPage?> Probe(bool cf)
        {
            try
            {
                return await Fetch(cf, 0, 1);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
        var probes = await Task.WhenAll(Probe(false), Probe(true));
        if (probes.All(p => p == null))
            throw new InvalidOperationException("Neither community source is available.");
        if (probes.Any(p => p == null))
        {
            var cf = probes[0] == null;
            var result = await Fetch(cf, offset, limit);
            return result with
            {
                Warning = cf
                    ? "Modrinth is unavailable. Showing CurseForge results only."
                    : "CurseForge is unavailable. Showing Modrinth results only.",
            };
        }
        var a = probes[0]!.TotalHits;
        var b = probes[1]!.TotalHits;
        var total = checked(a + b);
        var before = Counts(Math.Min(offset, total), a, b);
        var after = Counts((int)Math.Min((long)offset + limit, total), a, b);
        async Task<IReadOnlyList<SearchResult>> Range(bool cf, int start, int end) =>
            end == start ? [] : (await Fetch(cf, start, end - start)).Hits;
        var pages = await Task.WhenAll(
            Range(false, before.A, after.A),
            Range(true, before.B, after.B)
        );
        var hits = new List<SearchResult>();
        int ai = 0,
            bi = 0;
        for (var n = offset; n < Math.Min((long)offset + limit, total); n++)
        {
            var current = Counts(n, a, b);
            var next = Counts(n + 1, a, b);
            if (next.A > current.A)
            {
                if (ai < pages[0].Count)
                    hits.Add(pages[0][ai++]);
            }
            else if (bi < pages[1].Count)
                hits.Add(pages[1][bi++]);
        }
        return new(hits, offset, limit, total);
    }
}
