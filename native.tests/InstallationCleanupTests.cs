using System.Net;
using System.Text.Json.Nodes;
using CMCLauncher;

static class InstallationCleanupTests
{
    sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => send(request, ct);
    }

    sealed class CancelingStream(CancellationTokenSource cancellation)
        : MemoryStream(new byte[4096])
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken ct = default
        )
        {
            cancellation.Cancel();
            return base.ReadAsync(buffer, ct);
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        var store = new Store(root);
        await Store.AtomicJson(
            Store.SafePath(store.Library, "versions/fixture/fixture.json"),
            new JsonObject
            {
                ["id"] = "fixture",
                ["libraries"] = new JsonArray(),
                ["downloads"] = new JsonObject
                {
                    ["client"] = new JsonObject
                    {
                        ["url"] = "https://example.com/client",
                        ["size"] = 6,
                    },
                },
            }
        );
        var shared = Store.SafePath(store.Library, "libraries/keep.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "shared dependency");
        using var cancel = new CancellationTokenSource();
        var instance = new Instance { Name = "Cancel me", GameVersion = "fixture" };
        var observedDirectory = false;
        using (
            var core = new LauncherCore(
                store,
                new Handler(
                    (_, _) =>
                    {
                        observedDirectory = Directory.Exists(instance.Directory);
                        return Task.FromResult(
                            new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StreamContent(new CancelingStream(cancel)),
                            }
                        );
                    }
                )
            )
        )
        {
            try
            {
                await core.InstallNewGame(instance, "", cancel.Token);
                throw new Exception("Cancellation was ignored");
            }
            catch (OperationCanceledException) { }
        }
        Check(
            observedDirectory && !Directory.Exists(instance.Directory),
            "Canceled installation removes its newly created version directory"
        );
        Check(
            File.Exists(shared)
                && File.Exists(Store.SafePath(store.Library, "versions/fixture/fixture.json")),
            "Shared dependencies and existing cached versions survive rollback"
        );
        Check(store.Config.Instances.Count == 0, "Canceled installation is not registered");
        store.EnsureUniqueInstanceName(new Instance { Name = instance.Name });
        var failed = new Instance
        {
            Name = "Failed loader",
            GameVersion = "fixture",
            Loader = "forge",
        };
        using (
            var core = new LauncherCore(
                store,
                new Handler((_, _) => throw new Exception("Network must not be reached"))
            )
        )
        {
            try
            {
                await core.InstallNewGame(failed, "incompatible", CancellationToken.None);
                throw new Exception("Invalid loader accepted");
            }
            catch (Exception ex) when (ex.Message.Contains("不匹配")) { }
        }
        Check(
            !Directory.Exists(failed.Directory),
            "Loader failure also removes the new instance directory"
        );
        var existing = Path.Combine(store.Library, "versions", "Existing");
        Directory.CreateDirectory(existing);
        var keep = Path.Combine(existing, "keep.txt");
        await File.WriteAllTextAsync(keep, "user data");
        using (
            var core = new LauncherCore(
                store,
                new Handler((_, _) => throw new Exception("Network must not be reached"))
            )
        )
        {
            foreach (
                var item in new[]
                {
                    new Instance { Name = "Existing" },
                    new Instance { Name = "Existing", Directory = existing },
                    new Instance { Name = "Imported", ImportRoot = store.Library },
                }
            )
            {
                var rejected = false;
                try
                {
                    await core.InstallNewGame(item, "", CancellationToken.None);
                }
                catch (Exception)
                {
                    rejected = true;
                }
                Check(
                    rejected && File.Exists(keep),
                    "Existing and imported directories cannot be claimed by installation rollback"
                );
            }
        }
        var success = new Instance { Name = "Completed", GameVersion = "fixture" };
        using (
            var core = new LauncherCore(
                store,
                new Handler(
                    (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("client"),
                            }
                        )
                )
            )
        )
            await core.InstallNewGame(success, "", CancellationToken.None);
        Check(
            File.Exists(Path.Combine(success.Directory!, "Completed.json")),
            "Successful installation keeps its complete version directory"
        );
    }
}
