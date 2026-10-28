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

public sealed partial class MainWindow : Window
{
    readonly Store store;
    readonly LauncherCore core;
    readonly ModrinthService modrinth;
    readonly MicrosoftAuth auth;
    string page = "home",
        kind = "resourcepack";
    string instanceFilter = "全部实例",
        instanceQuery = "",
        modFilter = "全部 Mods",
        modQuery = "";
    Instance? selected;
    string? world;
    int renderVersion;

    // 暴露给 CMCPage 子类的内部属性
    internal Store Store => store;
    internal LauncherCore Core => core;
    internal ModrinthService ModrinthService => modrinth;
    internal MicrosoftAuth Auth => auth;
    internal Instance? SelectedInstance => selected;
    internal string CurrentPage
    {
        get => page;
        set => page = value;
    }
    internal string ExpandedNavigation
    {
        get => expandedNavigation;
        set => expandedNavigation = value;
    }
    internal int RenderVersion => renderVersion;
    internal CancellationTokenSource? resourceSearch;
    SearchResult? selectedProject;
    string resourceReturnPage = "download-mod";
    Func<string, CancellationToken, Task<CommunityProject>> fetchProject = null!;
    Func<string, CancellationToken, Task<List<CommunityVersion>>> fetchProjectVersions = null!;
    string communityGameCategory = "全部";
    Func<
        string,
        string,
        CancellationToken,
        int,
        int,
        CommunitySearchFilters?,
        Task<CommunitySearchPage>
    > searchResourcePage = null!;
    Func<string, string, string?, Task<List<ContentItem>>> listContent = (
        directory,
        type,
        targetWorld
    ) => Task.Run(() => ContentManager.ListFiles(directory, type, targetWorld));
    Task contentLoad = Task.CompletedTask;
    internal readonly Queue<string> logs = new();
    internal TextBox? logView;
    bool dialogOpen;
    internal StackPanel PageBody = new() { Spacing = 11 };

    // UI 验证使用单独的数据目录，避免改动日常使用的实例和账号配置。
    public MainWindow(string[] args)
    {
        store = new Store(
            args.Length == 2 && args[0] == "--verify-ui"
                ? Path.Combine(Path.GetFullPath(args[1]), "data")
                : null
        );
        InitializeComponent();
        InitializeCommunityFilterDrawer();
        InitializeFeedback();
        RuntimeLog.DebugEnabled = store.Config.Settings.DebugLogging;
        RuntimeLog.Written += RuntimeLogWritten;
        ModpackDropRoot.AddHandler(
            UIElement.DragEnterEvent,
            new DragEventHandler(Modpack_DragOver),
            true
        );
        ModpackDropRoot.AddHandler(
            UIElement.DragOverEvent,
            new DragEventHandler(Modpack_DragOver),
            true
        );
        ModpackDropRoot.AddHandler(UIElement.DropEvent, new DragEventHandler(Modpack_Drop), true);
        TaskDrawer.RegisterPropertyChangedCallback(
            SplitView.IsPaneOpenProperty,
            (_, _) => SyncTaskDrawerOverlay()
        );
        TaskDrawer.SizeChanged += (_, _) => ApplyPersonalization();
        TaskDrawerTitle.Text = Ui("Tasks in progress");
        TaskManagerLabel.Text = Ui("Tasks");
        PageTitle.Text = Ui(PageTitle.Text);
        PageSubtitle.Text = Ui(PageSubtitle.Text);
        ToolTipService.SetToolTip(TaskDrawerBackButton, Ui("Back"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerBackButton, Ui("Back"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);
        CustomTitleBar.SizeChanged += (_, _) => UpdateTitleBarInset();
        AppWindow.Changed += (_, _) => UpdateTitleBarInset();
        // Frame 加载前先缓存正文，等它准备好后再执行首次导航。
        PageFrame.Loaded += (_, _) =>
        {
            pageFrameReady = true;
            if (pendingPageBody is { } initial)
            {
                pendingPageBody = null;
                NavigatePage(initial);
            }
        };
        BuildNavigation();
        Title = "CMCLauncher";
        TitleIcon.Source = new BitmapImage(
            new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.png"))
        );
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.ico"));
        core = new LauncherCore(store);
        InitializeFrpc(
            args.Length == 2 && args[0] == "--verify-ui"
                ? Path.Combine(Path.GetFullPath(args[1]), "program", "frpc", "configs")
                : null
        );
        modrinth = new ModrinthService(core, store);
        auth = new MicrosoftAuth(core);
        fetchVersions = ct => core.VersionCatalog(ct);
        fetchDownloadLoaderBuilds = core.LoaderBuilds;
        fetchProject = modrinth.ProjectDetails;
        fetchProjectVersions = modrinth.ProjectVersions;
        searchResourcePage = modrinth.SearchPage;
        fetchCommunityFilters = modrinth.FilterCatalog;
        core.Status += Report;
        modrinth.Status += Report;
        core.DownloadBytes += bytes =>
        {
            if (activeWork is { } work)
                Interlocked.Add(ref work.Bytes, bytes);
        };
        taskViewTimer = DispatcherQueue.CreateTimer();
        taskViewTimer.Interval = TimeSpan.FromMilliseconds(500);
        taskViewTimer.Tick += (_, _) => RefreshTaskMetrics();
        taskViewTimer.Start();
        // 捕获回调所属任务，排队期间任务若已结束或更换，就丢弃这次进度。
        core.DownloadProgress += value =>
        {
            var activeTask = task;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (activeTask != null && task == activeTask && !launching)
                    UpdateDownloadProgress(value);
            });
        };
        core.Log += line => DispatcherQueue.TryEnqueue(() => AddLog(line));
        core.DebugLog += RuntimeLog.Debug;
        ApplyTheme();
        Render();
        RestoreWindowSize();

        if (
            AppWindow.Presenter is OverlappedPresenter presenter
            && store.Config.Settings.StartMaximized
        )
        {
            presenter.Maximize();
        }

        InitializeWindowSizeSaving();
        systemUi.ColorValuesChanged += SystemThemeChanged;
        RuntimeLog.Write("INFO", "应用启动 · 程序目录=" + AppContext.BaseDirectory);
        Closed += (_, _) =>
        {
            RuntimeLog.Written -= RuntimeLogWritten;
            core.DebugLog -= RuntimeLog.Debug;
            RuntimeLog.Write("INFO", "应用关闭");
            taskViewTimer.Stop();
            toastTimer?.Stop();
            activePopconfirm?.Hide();
            resourceSearch?.Cancel();
            systemUi.ColorValuesChanged -= SystemThemeChanged;
            task?.Cancel();
            core.Dispose();
        };
    }

    // 日志始终记录，但只有仍属于当前任务的消息才能更新任务阶段。
    internal void Report(string message)
    {
        var reportingTask = task;
        DispatcherQueue.TryEnqueue(() =>
        {
            AddLog(message);
            if (reportingTask == null || reportingTask != task || activeWork == null)
                return;
            activeWork.Stage = message;
            RefreshTaskMetrics();
        });
    }

    internal void AddLog(string line) => RuntimeLog.Write("INFO", line);

    void RuntimeLogWritten(string line) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            logs.Enqueue(line);
            while (logs.Count > 1000)
                logs.Dequeue();
            if (page == "logs" && logView != null)
            {
                var text = logView.Text + Environment.NewLine + line;
                logView.Text = text.Length > 250_000 ? text[^250_000..] : text;
            }
        });

    // 切换界面语言后需重建导航栏和所有文本
    internal async Task SwitchLanguage(string lang)
    {
        localization.Reload();
        store.Config.Settings.LauncherLanguage = lang;
        await store.Save();
        LocalizeStaticChrome();
        NavigationMenu.Children.Clear();
        pageNavigationAnimation?.Stop();
        pageNavigationAnimation = null;
        displayedPageRoute = null;
        PageSurface.Opacity = 1;
        navigationButtons.Clear();
        navigationButtonPalettes.Clear();
        navigationIndicators.Clear();
        navigationSubmenus.Clear();
        animatedSubmenus.Clear();
        animatedNavigationIndicators.Clear();
        selectedRootIndicator = selectedPageIndicator = null;
        BuildNavigation();
        Render();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? windowSizeSaveTimer;

    private void RestoreWindowSize()
    {
        var settings = store.Config.Settings;

        int width = Math.Clamp(settings.WindowWidth, 800, 3000);
        int height = Math.Clamp(settings.WindowHeight, 500, 2000);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
    }

    private void InitializeWindowSizeSaving()
    {
        windowSizeSaveTimer = DispatcherQueue.CreateTimer();
        windowSizeSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
        windowSizeSaveTimer.IsRepeating = false;

        windowSizeSaveTimer.Tick += async (_, _) =>
        {
            try
            {
                await store.Save();
            }
            catch (Exception ex)
            {
                RuntimeLog.Write("ERROR", "保存窗口尺寸失败 · " + ex);
            }
        };

        AppWindow.Changed += (sender, args) =>
        {
            if (!args.DidSizeChange)
                return;

            if (
                sender.Presenter is not OverlappedPresenter presenter
                || presenter.State != OverlappedPresenterState.Restored
            )
                return;

            var size = sender.Size;

            if (size.Width < 800 || size.Height < 500)
                return;

            store.Config.Settings.WindowWidth = size.Width;
            store.Config.Settings.WindowHeight = size.Height;

            windowSizeSaveTimer.Stop();
            windowSizeSaveTimer.Start();
        };
    }
}
