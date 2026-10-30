using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Process? game;
    bool launching;
    TextBlock? launchStatusText;
    ProgressBar? launchCardProgress;
    Button? launchCardCancel;

    // 先验证账号并补全依赖，出现游戏窗口后才把启动任务标记完成。
    async Task Launch(Instance instance)
    {
        if (task != null)
            throw new Exception("已有任务进行中，请稍候或取消当前任务");
        launching = true;
        launchProgressValue = 0;
        RefreshLaunchCard("正在准备启动…");
        var progress = new Progress<(double Value, string Stage)>(update =>
            UpdateLaunchProgress(update.Value, update.Stage)
        );
        try
        {
            await Run(
                async ct =>
                {
                    var launchStarted = DateTimeOffset.UtcNow;
                    var account =
                        store.Config.Accounts.FirstOrDefault(a =>
                            a.Key == store.Config.SelectedAccount
                        )
                        ?? throw new Exception("请先添加账号");
                    MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
                    UpdateLaunchProgress(0, "正在验证账号…");
                    if (account.Type == "yggdrasil")
                    {
                        // Refresh the ownership account before sending any third-party token.
                        var owner = store.Config.Accounts.First(
                            MicrosoftAuth.IsVerifiedMinecraftPlayer
                        );
                        var verified = await auth.Refresh(
                            owner,
                            MicrosoftAuth.ConfiguredClientId,
                            ct
                        );
                        store.Config.Accounts[store.Config.Accounts.IndexOf(owner)] = verified;
                        using var external = new ExternalAuth(store.Config.Settings);
                        var refreshed = await external.Refresh(store.Config.Accounts, account, ct);
                        store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed;
                        account = refreshed;
                        await store.Save();
                    }
                    if (account.Type == "microsoft")
                    {
                        var refreshed = await auth.Refresh(
                            account,
                            MicrosoftAuth.ConfiguredClientId,
                            ct
                        );
                        store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed;
                        account = refreshed;
                        await store.Save();
                    }
                    var launchedGame = await core.Launch(
                        instance,
                        account,
                        CredentialVault.Read(account),
                        ct,
                        progress
                    );
                    game = launchedGame;
                    // 用户取消启动导致的退出不弹崩溃报告，其他异常退出交给报告逻辑判断。
                    var cancelledByLauncher = false;
                    var monitoringDone = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    async Task ObserveExit()
                    {
                        try
                        {
                            // WaitForExitAsync also handles a process that exited before this call
                            // and waits for the redirected output streams to finish.
                            await launchedGame.WaitForExitAsync();
                            await monitoringDone.Task;
                            Report(instance.Name + "：游戏已退出");
                            if (!cancelledByLauncher)
                                await HandleGameExit(instance, launchedGame, launchStarted);
                        }
                        catch (Exception ex)
                        {
                            AddLog("Game exit monitoring failed: " + ex.Message);
                        }
                        finally
                        {
                            await monitoringDone.Task;
                            if (ReferenceEquals(game, launchedGame))
                                game = null;
                            launchedGame.Dispose();
                        }
                    }
                    _ = ObserveExit();
                    try
                    {
                        instance.LastPlayed = DateTime.Now;
                        await store.Save();
                        UpdateLaunchProgress(99, "等待游戏窗口出现…");
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            launchedGame.Refresh();
                            if (launchedGame.HasExited)
                                throw new Exception(
                                    $"游戏在窗口出现前退出，退出码 {launchedGame.ExitCode}。请查看运行日志。"
                                );
                            if (launchedGame.MainWindowHandle != IntPtr.Zero)
                                break;
                            await Task.Delay(250, ct);
                        }
                        UpdateLaunchProgress(100, "游戏窗口已出现");
                        await Task.Delay(350, ct);
                        if (launchedGame.HasExited)
                            throw new Exception(
                                $"游戏在启动期间退出，退出码 {launchedGame.ExitCode}。请查看运行日志。"
                            );
                    }
                    catch (OperationCanceledException)
                    {
                        cancelledByLauncher = true;
                        if (!launchedGame.HasExited)
                            launchedGame.Kill(true);
                        await launchedGame.WaitForExitAsync();
                        throw;
                    }
                    finally
                    {
                        monitoringDone.TrySetResult();
                    }
                },
                "启动游戏 · " + instance.Name
            );
            Report("游戏正在运行");
        }
        finally
        {
            launching = false;
            RefreshLaunchCard();
        }
    }

    // 并发依赖的进度回调可能乱序，显示值只前进，不往回跳。
    void UpdateLaunchProgress(double value, string stage)
    {
        if (!launching)
            return;
        launchProgressValue = Math.Max(launchProgressValue, Math.Clamp(value, 0, 100));
        if (activeWork != null)
        {
            activeWork.Progress = launchProgressValue;
            activeWork.Stage = stage;
            RefreshTaskMetrics();
        }
        RefreshLaunchCard(stage);
    }
}
