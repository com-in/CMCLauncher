using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace CraftLauncher.WinUI;

// Keep native keyboard navigation and selection while anchoring the list below its field.
public sealed class BelowComboBox : ComboBox
{
    public BelowComboBox()
    {
        DefaultStyleKey = typeof(ComboBox);
        MaxDropDownHeight = 360;
        DropDownOpened += (_, _) => AnchorPopup();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AnchorPopup();
    }

    internal Popup? DropDownPopup => GetTemplateChild("Popup") as Popup;
    internal Microsoft.UI.Xaml.FrameworkElement? InputBorder => GetTemplateChild("Background") as Microsoft.UI.Xaml.FrameworkElement;

    // 模板应用和下拉打开时都重新定位，窗口尺寸和滚动位置可能已经变化。
    void AnchorPopup()
    {
        if (DropDownPopup is not { } popup) return;
        if (Items.Count == 0)
        {
            if (IsDropDownOpen) IsDropDownOpen = false;
            return;
        }
        var target = InputBorder ?? this;
        popup.PlacementTarget = target;
        popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        popup.VerticalOffset = 0;
        popup.HorizontalOffset = 0;
        if (XamlRoot?.Content is not FrameworkElement root || !target.IsLoaded) return;
        var bottom = target.TransformToVisual(root).TransformPoint(new Point(0, target.ActualHeight)).Y;
        var available = root.ActualHeight - bottom - 8;
        // 底部空间不足时先把输入框滚上来，排队后再按新的位置计算弹层高度。
        if (available < 180)
        {
            for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ScrollViewer scroll) continue;
                var top = TransformToVisual(scroll).TransformPoint(new Point()).Y;
                scroll.ChangeView(null, Math.Max(0, scroll.VerticalOffset + top - 72), null, true);
                DispatcherQueue.TryEnqueue(AnchorPopup);
                break;
            }
        }
        MaxDropDownHeight = Math.Clamp(available, 80, 360);
    }
}
