using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    sealed class WorkItem
    {
        internal string Name = "", Stage = "", State = "进行中";
        internal bool Launch;
        internal double? Progress;
        internal long Bytes;
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal TimeSpan? CompletedElapsed;
        internal long LastBytes;
        internal double LastSample, Speed;
    }
    WorkItem? activeWork;
    readonly List<WorkItem> recentWork = [];
    readonly DispatcherQueueTimer taskViewTimer;
    TextBlock? taskStage, taskMetrics, taskElapsed;
    ProgressBar? taskProgress;
    Grid? launchCardProgressRow;
    TextBlock? launchCardPercent;
    double launchProgressValue;
    string launchStage = "正在准备启动…";
    void RefreshLaunchCard(string? stage = null)
    {
        if (stage != null) launchStage = stage;
        if (launchStatusText == null || launchCardProgressRow == null || launchCardProgress == null || launchCardCancel == null || launchCardPercent == null) return;
        launchCardProgressRow.Visibility = launching ? Visibility.Visible : Visibility.Collapsed;
        launchCardProgress.Value = launchProgressValue;
        launchCardPercent.Text = $"{launchProgressValue:0}%";
        launchCardCancel.IsEnabled = launching && task != null && !task.IsCancellationRequested;
        var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount);
        launchStatusText.Text = Ui(launching ? activeWork?.Stage ?? launchStage : selected == null ? "从选择版本开始" : account == null ? "添加账号后即可启动" : !MicrosoftAuth.CanUseAccount(store.Config.Accounts, account) ? "请先验证 Microsoft 正版账号" : "已准备就绪");
    }

    void TaskManager_Click(object sender, RoutedEventArgs e)
    {
        Tasks(); TaskDrawer.IsPaneOpen = !TaskDrawer.IsPaneOpen;
    }
    void TaskDrawer_Close(object sender, RoutedEventArgs e) => TaskDrawer.IsPaneOpen = false;
    void TaskDrawerScrim_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) { e.Handled = true; TaskDrawer.IsPaneOpen = false; }
    void SyncTaskDrawerOverlay()
    {
        TaskDrawerScrim.Visibility = TaskDrawer.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        SyncNavigation();
        RefreshTaskMetrics();
    }
    void TaskDrawer_StateChanged(object sender, object args) => SyncTaskDrawerOverlay();
    void Tasks()
    {
        TaskDrawerBody.Children.Clear(); taskStage = null; taskMetrics = null; taskElapsed = null; taskProgress = null;
        if (activeWork is { Launch: false } work)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Text(Ui(work.Name), 17, true));
            taskStage = Text(work.Stage, 13); panel.Children.Add(taskStage);
            taskProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5 }; panel.Children.Add(taskProgress);
            taskMetrics = Text("", 12); panel.Children.Add(taskMetrics);
            taskElapsed = Text("", 12); panel.Children.Add(taskElapsed);
            var cancel = new Button { Content = Ui("取消任务", "Cancel task"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6, 12, 6) };
            cancel.Click += (_, _) => { task?.Cancel(); cancel.IsEnabled = false; work.Stage = "正在取消…"; RefreshTaskMetrics(); };
            panel.Children.Add(cancel); TaskDrawerBody.Children.Add(panel);
        }
        else TaskDrawerBody.Children.Add(Text("当前没有进行中的任务。", 14));
        if (recentWork.Any(w => !w.Launch))
        {
            TaskDrawerBody.Children.Add(Text("最近任务", 15, true));
            foreach (var recent in recentWork.Where(w => !w.Launch))
            {
                var panel = new StackPanel { Spacing = 4 };
                panel.Children.Add(Text(Ui(recent.Name) + " · " + Ui(recent.State), 14, true));
                panel.Children.Add(Text(Ui(recent.Stage), 12));
                panel.Children.Add(Text(store.Config.Settings.LauncherLanguage == "en_us" ? $"Received {ByteSize(Interlocked.Read(ref recent.Bytes))}" : $"已接收 {ByteSize(Interlocked.Read(ref recent.Bytes))}", 12));
                panel.Children.Add(Text(store.Config.Settings.LauncherLanguage == "en_us" ? $"Elapsed {FormatDuration(Elapsed(recent))}" : $"耗时 {FormatDuration(Elapsed(recent))}", 12));
                TaskDrawerBody.Children.Add(CompactCard(panel));
            }
        }
        RefreshTaskMetrics();
        ApplyPersonalization();
    }
    void UpdateTaskCount(int count)
    {
        TaskCountText.Text = Math.Max(0, count).ToString(System.Globalization.CultureInfo.InvariantCulture);
        TaskCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    void RefreshTaskMetrics()
    {
        UpdateTaskCount(activeWork is { Launch: false } ? 1 : 0);
        RefreshLaunchCard();
        if (activeWork is not { } work) return;
        var bytes = Interlocked.Read(ref work.Bytes); var now = work.Clock.Elapsed.TotalSeconds;
        if (now - work.LastSample >= .45)
        {
            work.Speed = Math.Max(0, (bytes - work.LastBytes) / (now - work.LastSample));
            work.LastBytes = bytes; work.LastSample = now;
        }
        if (!TaskDrawer.IsPaneOpen || taskStage == null || taskMetrics == null || taskElapsed == null || taskProgress == null) return;
        var english = store.Config.Settings.LauncherLanguage == "en_us";
        var label = english ? work.Launch ? "Launch progress" : "Current file progress" : work.Launch ? "启动进度" : "当前文件进度";
        var progressText = work.Progress is double progressValue ? progressValue.ToString("0") + "%" : english ? "Preparing" : "准备中";
        taskStage.Text = Ui(work.Stage);
        taskProgress.IsIndeterminate = !work.Progress.HasValue; taskProgress.Value = work.Progress ?? 0;
        taskMetrics.Text = english
            ? $"{label}: {progressText}\nSpeed: {ByteSize(work.Speed)}/s   ·   Received {ByteSize(bytes)}"
            : $"{label}：{progressText}\n速度：{ByteSize(work.Speed)}/s   ·   已接收 {ByteSize(bytes)}";
        taskElapsed.Text = english ? $"Elapsed: {FormatDuration(Elapsed(work))}" : $"耗时：{FormatDuration(Elapsed(work))}";
    }
    static TimeSpan Elapsed(WorkItem work) => work.CompletedElapsed ?? work.Clock.Elapsed;
    static string FormatDuration(TimeSpan elapsed)
    {
        var totalHours = (int)elapsed.TotalHours;
        return totalHours > 0 ? $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
    static string ByteSize(double bytes) => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024 * 1024 * 1024):0.0} GiB" : bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024):0.0} MiB" : bytes >= 1024 ? $"{bytes / 1024:0.0} KiB" : $"{bytes:0} B";
}
