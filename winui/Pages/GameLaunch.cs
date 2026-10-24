using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Text.Json.Nodes;
using Windows.UI.ViewManagement;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    Process? game;
    bool launching;
    TextBlock? launchStatusText;
    ProgressBar? launchCardProgress;
    Button? launchCardCancel;

    async Task Launch(Instance instance)
    {
        if (task != null) throw new Exception("已有任务进行中，请稍候或取消当前任务");
        launching = true;
        launchProgressValue = 0; RefreshLaunchCard("正在准备启动…");
        var progress = new Progress<(double Value, string Stage)>(update => UpdateLaunchProgress(update.Value, update.Stage));
        try
        {
            await Run(async ct =>
            {
                var launchStarted = DateTimeOffset.UtcNow;
                var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount) ?? throw new Exception("请先添加账号");
                MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
                UpdateLaunchProgress(0, "正在验证账号…");
                if (account.Type == "microsoft")
                {
                    var refreshed = await auth.Refresh(account, MicrosoftAuth.ConfiguredClientId, ct);
                    store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed; account = refreshed; await store.Save();
                }
                var launchedGame = await core.Launch(instance, account, CredentialVault.Read(account), ct, progress);
                game = launchedGame;
                instance.LastPlayed = DateTime.Now; await store.Save();
                var cancelledByLauncher = false;
                launchedGame.Exited += (_, _) =>
                {
                    Report(instance.Name + "：游戏已退出");
                    if (!cancelledByLauncher) DispatcherQueue.TryEnqueue(async () => await HandleGameExit(instance, launchedGame, launchStarted));
                };
                UpdateLaunchProgress(99, "等待游戏窗口出现…");
                try
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested(); launchedGame.Refresh();
                        if (launchedGame.HasExited) throw new Exception($"游戏在窗口出现前退出，退出码 {launchedGame.ExitCode}。请查看运行日志。");
                        if (launchedGame.MainWindowHandle != IntPtr.Zero) break;
                        await Task.Delay(250, ct);
                    }
                }
                catch (OperationCanceledException) { cancelledByLauncher = true; if (!launchedGame.HasExited) launchedGame.Kill(true); throw; }
                UpdateLaunchProgress(100, "游戏窗口已出现");
                await Task.Delay(350, ct);
            }, "启动游戏 · " + instance.Name);
            Report("游戏正在运行");
        }
        finally
        {
            launching = false; RefreshLaunchCard();
        }
    }
    void UpdateLaunchProgress(double value, string stage)
    {
        if (!launching) return;
        launchProgressValue = Math.Max(launchProgressValue, Math.Clamp(value, 0, 100));
        if (activeWork != null) { activeWork.Progress = launchProgressValue; activeWork.Stage = stage; RefreshTaskMetrics(); }
        RefreshLaunchCard(stage);
    }
}
