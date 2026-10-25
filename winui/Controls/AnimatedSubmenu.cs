using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace CMCLauncher.WinUI;

// 通过裁剪容器播放高度动画，收起过程中的内容不会溢出到相邻菜单。
internal sealed class AnimatedSubmenu
{
    readonly StackPanel content;
    readonly Border viewport;
    Storyboard? animation;
    bool expanded;
    internal Border View => viewport;
    internal bool IsAnimating => animation != null;

    internal AnimatedSubmenu(StackPanel content)
    {
        this.content = content;
        viewport = new Border { Child = content, Height = 0, Opacity = 0, Visibility = Visibility.Collapsed };
        viewport.SizeChanged += (_, e) => viewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    internal void SetExpanded(bool value, bool animate)
    {
        if (expanded == value) return;
        expanded = value;
        // 连续点击时从当前画面接着播，不从上一次动画的起点重新跳起。
        var currentHeight = viewport.ActualHeight;
        var currentOpacity = viewport.Opacity;
        animation?.Stop(); animation = null;
        viewport.Height = currentHeight; viewport.Opacity = currentOpacity;
        content.Visibility = Visibility.Visible; viewport.Visibility = Visibility.Visible;
        content.Measure(new Size(Math.Max(1, viewport.ActualWidth > 0 ? viewport.ActualWidth : 200), double.PositiveInfinity));
        var targetHeight = value ? content.DesiredSize.Height : 0;
        viewport.IsHitTestVisible = value;
        if (!animate) { Finish(); return; }
        var storyboard = new Storyboard();
        Add(viewport, "Height", currentHeight, targetHeight);
        Add(viewport, "Opacity", currentOpacity, value ? 1 : 0);
        animation = storyboard;
        storyboard.Completed += (_, _) =>
        {
            // 旧动画的完成回调可能晚到，不能让它覆盖新动画的展开状态。
            if (!ReferenceEquals(animation, storyboard)) return;
            storyboard.Stop(); animation = null; Finish();
        };
        storyboard.Begin();

        void Add(DependencyObject target, string property, double from, double to)
        {
            var tween = new DoubleAnimation { From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(210)), EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(tween, target); Storyboard.SetTargetProperty(tween, property);
            storyboard.Children.Add(tween);
        }
        void Finish()
        {
            // 展开完成后恢复自动高度，后续字号和内容变化仍能正常参与布局。
            viewport.Height = value ? double.NaN : 0; viewport.Opacity = value ? 1 : 0;
            viewport.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            content.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
