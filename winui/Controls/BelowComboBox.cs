using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace CMCLauncher.WinUI;

// Keep native keyboard navigation and selection while anchoring the list below its field.
public sealed class BelowComboBox : ComboBox
{
    public BelowComboBox()
    {
        DefaultStyleKey = typeof(ComboBox);
        MaxDropDownHeight = 360;
        AddControlPalette();
        DropDownOpened += (_, _) =>
        {
            SyncPopupTheme();
            AnchorPopup();
        };
        ActualThemeChanged += (_, _) => SyncPopupTheme();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        SyncPopupTheme();
        AnchorPopup();
    }

    // 可选：按下拉项文本生成该项的字体，用于「所见即所得」预览（如字体选择器）。
    // 在项容器上直接设置，避开弹层内的静态资源查找，避免 XAML 解析崩溃。
    public Func<string, FontFamily>? ItemFontFactory { get; set; }

    static readonly SolidColorBrush TransparentBrush = new(Microsoft.UI.Colors.Transparent);

    static void AddItemPalette(ResourceDictionary resources)
    {
        foreach (
            var state in new[]
            {
                "",
                "PointerOver",
                "Pressed",
                "Selected",
                "SelectedUnfocused",
                "SelectedPointerOver",
                "SelectedPressed",
                "Disabled",
                "SelectedDisabled",
            }
        )
        {
            resources["ComboBoxItemForeground" + state] = state.EndsWith("Disabled")
                ? MainWindow.ComboDisabledTextBrush
                : MainWindow.ComboTextBrush;
            resources["ComboBoxItemBackground" + state] = state switch
            {
                "PointerOver" => MainWindow.ComboHoverBrush,
                "Pressed" => MainWindow.ComboPressedBrush,
                "Selected" or "SelectedUnfocused" => MainWindow.ComboSelectedBrush,
                "SelectedPointerOver" => MainWindow.ComboSelectedHoverBrush,
                "SelectedPressed" => MainWindow.ComboSelectedPressedBrush,
                "SelectedDisabled" => MainWindow.ComboSelectedDisabledBrush,
                _ => TransparentBrush,
            };
            resources["ComboBoxItemBorderBrush" + state] = TransparentBrush;
        }
        resources["ComboBoxItemPillFillBrush"] = MainWindow.ComboAccentBrush;
    }

    void AddControlPalette()
    {
        AddItemPalette(Resources);
        Resources["ComboBoxDropDownBackground"] = MainWindow.ComboSurfaceBrush;
        Resources["ComboBoxDropDownBorderBrush"] = MainWindow.ComboBorderBrush;
        Resources["ComboBoxDropDownForeground"] = MainWindow.ComboTextBrush;
        Resources["ComboBoxHeaderForeground"] = MainWindow.ComboTextBrush;
        Resources["ComboBoxPlaceHolderForeground"] = MainWindow.ComboMutedBrush;
        foreach (
            var state in new[]
            {
                "",
                "PointerOver",
                "Pressed",
                "Disabled",
                "Focused",
                "FocusedPressed",
                "Unfocused",
            }
        )
        {
            var disabled = state == "Disabled";
            Resources["ComboBoxBackground" + state] = state switch
            {
                "PointerOver" => MainWindow.ComboHoverBrush,
                "Pressed" or "FocusedPressed" => MainWindow.ComboPressedBrush,
                _ => MainWindow.ComboSurfaceBrush,
            };
            Resources["ComboBoxForeground" + state] = disabled
                ? MainWindow.ComboDisabledTextBrush
                : MainWindow.ComboTextBrush;
            Resources["ComboBoxBorderBrush" + state] =
                disabled ? MainWindow.ComboBorderBrush
                : state == "Focused" ? MainWindow.ComboAccentBrush
                : MainWindow.ComboBorderBrush;
            Resources["ComboBoxDropDownGlyphForeground" + state] = disabled
                ? MainWindow.ComboDisabledTextBrush
                : MainWindow.ComboMutedBrush;
        }
        Resources["ComboBoxBackgroundBorderBrushFocused"] = MainWindow.ComboAccentBrush;
        Resources["ComboBoxBackgroundBorderBrushUnfocused"] = MainWindow.ComboBorderBrush;
        Resources["ComboBoxDropDownBackgroundPointerOver"] = MainWindow.ComboHoverBrush;
        Resources["ComboBoxDropDownBackgroundPointerPressed"] = MainWindow.ComboPressedBrush;
        Background = MainWindow.ComboSurfaceBrush;
        Foreground = MainWindow.ComboTextBrush;
        BorderBrush = MainWindow.ComboBorderBrush;
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        // 弹层与正文不在同一棵可视树，虚拟化生成的选项也要有自己的资源入口。
        if (element is ComboBoxItem container)
        {
            AddItemPalette(container.Resources);
            if (ItemFontFactory is { } factory && item is string display)
                container.FontFamily = factory(display);
        }
        base.PrepareContainerForItemOverride(element, item);
    }

    void SyncPopupTheme()
    {
        if (DropDownPopup?.Child is FrameworkElement child)
            child.RequestedTheme = ActualTheme;
        if (PopupBorder is { } border)
        {
            border.Background = MainWindow.ComboSurfaceBrush;
            border.BorderBrush = MainWindow.ComboBorderBrush;
        }
    }

    internal Border? PopupBorder => GetTemplateChild("PopupBorder") as Border;

    internal Popup? DropDownPopup => GetTemplateChild("Popup") as Popup;
    internal Microsoft.UI.Xaml.FrameworkElement? InputBorder =>
        GetTemplateChild("Background") as Microsoft.UI.Xaml.FrameworkElement;

    // 模板应用和下拉打开时都重新定位，窗口尺寸和滚动位置可能已经变化。
    void AnchorPopup()
    {
        if (DropDownPopup is not { } popup)
            return;
        if (Items.Count == 0)
        {
            if (IsDropDownOpen)
                IsDropDownOpen = false;
            return;
        }
        var target = InputBorder ?? this;
        popup.PlacementTarget = target;
        popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        popup.VerticalOffset = 0;
        popup.HorizontalOffset = 0;
        if (XamlRoot?.Content is not FrameworkElement root || !target.IsLoaded)
            return;
        var bottom = target
            .TransformToVisual(root)
            .TransformPoint(new Point(0, target.ActualHeight))
            .Y;
        var available = root.ActualHeight - bottom - 8;
        // 底部空间不足时先把输入框滚上来，排队后再按新的位置计算弹层高度。
        if (available < 180)
        {
            for (
                DependencyObject? parent = VisualTreeHelper.GetParent(this);
                parent != null;
                parent = VisualTreeHelper.GetParent(parent)
            )
            {
                if (parent is not ScrollViewer scroll)
                    continue;
                var top = TransformToVisual(scroll).TransformPoint(new Point()).Y;
                scroll.ChangeView(null, Math.Max(0, scroll.VerticalOffset + top - 72), null, true);
                DispatcherQueue.TryEnqueue(AnchorPopup);
                break;
            }
        }
        MaxDropDownHeight = Math.Clamp(available, 80, 360);
    }
}