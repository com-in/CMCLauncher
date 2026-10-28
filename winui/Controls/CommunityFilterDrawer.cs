using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Func<Task>? applyCommunityFilters;
    System.Action? resetCommunityFilters;
    Button? communityFilterReturnFocus;
    int communityFilterGeneration;
    bool communityFilterCloseRequested;

    void InitializeCommunityFilterDrawer()
    {
        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape, ScopeOwner = CommunityFilterDrawer, IsEnabled = false };
        escape.Invoked += (_, args) => { CloseCommunityFilterDrawer(); args.Handled = true; };
        CommunityFilterDrawer.KeyboardAccelerators.Add(escape);
        CommunityFilterDrawer.PaneClosing += (_, args) => args.Cancel = !communityFilterCloseRequested;
        ModpackDropRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (!CommunityFilterDrawer.IsPaneOpen) return;
            var point = args.GetCurrentPoint(CommunityFilterDrawer).Position;
            if (point.Y < 0 || point.X < CommunityFilterDrawer.ActualWidth - CommunityFilterDrawer.OpenPaneLength)
                CloseCommunityFilterDrawer();
        }), true);
        CommunityFilterDrawer.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, (_, _) =>
        {
            CommunityFilterScrim.Visibility = CommunityFilterDrawer.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            escape.IsEnabled = CommunityFilterDrawer.IsPaneOpen;
        });
        CommunityFilterDrawer.PaneOpened += (_, _) =>
        {
            if (CommunityFilterDrawer.IsPaneOpen) CommunityFilterCloseButton.Focus(FocusState.Programmatic);
        };
        CommunityFilterDrawer.KeyDown += (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Escape || !CommunityFilterDrawer.IsPaneOpen) return;
            CloseCommunityFilterDrawer(); args.Handled = true;
        };
    }

    void OpenCommunityFilterDrawer(UIElement content, Button opener, Func<Task> apply, System.Action reset)
    {
        TaskDrawer.IsPaneOpen = false;
        applyCommunityFilters = apply; resetCommunityFilters = reset;
        communityFilterReturnFocus = opener; communityFilterGeneration = renderVersion;
        CommunityFilterDrawerBody.Content = content;
        CommunityFilterDrawerScroll.ChangeView(null, 0, null, true);
        CommunityFilterDrawerTitle.Text = Ui("More filters");
        CommunityFilterDrawerHint.Text = Ui("Combine filters to narrow down resources.");
        CommunityFilterResetButton.Content = Ui("Reset");
        CommunityFilterApplyButton.Content = Ui("Apply filters");
        ToolTipService.SetToolTip(CommunityFilterCloseButton, Ui("Close filters"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CommunityFilterCloseButton, Ui("Close filters"));
        UpdateCommunityFilterDrawerWidth();
        communityFilterCloseRequested = false;
        CommunityFilterDrawer.IsPaneOpen = true;
        ApplyPersonalization();

    }

    void UpdateCommunityFilterDrawerWidth()
    {
        var size = store.Config.Settings.UiFontSize;
        var scale = double.IsFinite(size) ? Math.Clamp(size, 14, 22) / 14 : 1;
        CommunityFilterDrawer.OpenPaneLength = Math.Min(540 * Math.Max(1, scale), Math.Max(0, ((FrameworkElement)Content).ActualWidth - 24));
    }

    // 用自己的遮罩、关闭键和 Escape 关闭，避免 Overlay 在窗口重排时误触发轻关闭。
    void CloseCommunityFilterDrawer()
    {
        communityFilterCloseRequested = true;
        CommunityFilterDrawer.IsPaneOpen = false;
    }

    // 导航时释放旧页面的控件和回调，避免在其他资源页应用过期条件。
    void ClearCommunityFilterDrawer()
    {
        communityFilterReturnFocus = null;
        CloseCommunityFilterDrawer();
        CommunityFilterDrawerBody.Content = null;
        applyCommunityFilters = null; resetCommunityFilters = null;
    }

    void CommunityFilterClose_Click(object sender, RoutedEventArgs args) => CloseCommunityFilterDrawer();
    void CommunityFilterScrim_Tapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true; CloseCommunityFilterDrawer();
    }
    void CommunityFilterDrawer_Closed(object sender, object args)
    {
        if (!CommunityFilterDrawer.IsPaneOpen && !TaskDrawer.IsPaneOpen && communityFilterReturnFocus is { IsLoaded: true } opener)
            opener.Focus(FocusState.Programmatic);
    }
    void CommunityFilterReset_Click(object sender, RoutedEventArgs args) => resetCommunityFilters?.Invoke();
    async void CommunityFilterApply_Click(object sender, RoutedEventArgs args)
    {
        if (applyCommunityFilters is not { } apply || communityFilterGeneration != renderVersion) return;
        CommunityFilterApplyButton.IsEnabled = false;
        CloseCommunityFilterDrawer();
        try { await apply(); }
        catch (Exception ex) { await Error(ex); }
        finally { CommunityFilterApplyButton.IsEnabled = true; }
    }
}
