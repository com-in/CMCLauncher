using System.Net.Http;
using System.Text.Json.Nodes;
using CMCLauncher;

static class CurseForgeCredentialsTests
{
    public static async Task Run(string root)
    {
        if (CurseForgeCredentials.ConfiguredApiKey is { } key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains('\r') || key.Contains('\n'))
                throw new Exception("Invalid build credential");
            return;
        }
        using var core = new LauncherCore(new Store(root), new NoNetwork());
        var files = new JsonArray(new JsonObject { ["projectID"] = 1, ["fileID"] = 2 });
        try
        {
            await new CurseForgeService(core).ResolveFiles(files, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "CurseForge API key is not configured in this build.")
        {
            return;
        }
        throw new Exception("A missing build key must fail before sending a request");
    }

    sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => throw new Exception("A keyless build must not make a CurseForge request");
    }
}
