using System.Diagnostics;

namespace CMCLauncher;

// Each process owns a clean native directory; old DLLs and another running instance cannot alter it.
internal sealed class NativeDirectoryLease : IDisposable
{
    public string Path { get; }
    readonly Action<string> log;
    bool running;

    public NativeDirectoryLease(string game, Action<string> log)
    {
        this.log = log;
        Path = Store.SafePath(game, ".craft-natives/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Watch(Process process)
    {
        process.Exited += (_, _) =>
            _ = Task.Run(async () =>
            {
                string? error = null;
                // Windows may release mapped DLLs shortly after signaling process exit.
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (TryClean(out error))
                        return;
                    await Task.Delay(250);
                }
                log("Could not remove game native directory: " + error);
            });
    }

    public void Started() => running = true;

    public void Dispose()
    {
        if (!running && !TryClean(out var error))
            log("Could not remove game native directory: " + error);
    }

    bool TryClean(out string? error)
    {
        error = null;
        try
        {
            if (
                Directory.Exists(Path)
                && (File.GetAttributes(Path) & FileAttributes.ReparsePoint) == 0
            )
                Directory.Delete(Path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
