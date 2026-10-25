using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace CraftLauncher.WinUI;

// 每个蓝条保留自己的动画状态，重复刷新导航时不重播，连续切换时从当前画面接着过渡。
internal sealed class AnimatedNavigationIndicator
{
    readonly Border indicator;
    readonly CompositeTransform transform = new() { ScaleY = .55 };
    Storyboard? animation;
    bool selected;
    internal bool IsAnimating => animation != null;

    internal AnimatedNavigationIndicator(Border indicator)
    {
        this.indicator = indicator;
        indicator.RenderTransform = transform;
        indicator.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
        indicator.IsHitTestVisible = false;
    }

    internal void SetSelected(bool value, bool animate, double enteringOffset = 0)
    {
        if (selected == value)
        {
            if (!animate && animation != null) { animation.Stop(); animation = null; Finish(); }
            return;
        }
        selected = value;
        var opacity = indicator.Opacity;
        var scale = transform.ScaleY;
        var offset = transform.TranslateY;
        animation?.Stop(); animation = null;
        indicator.Opacity = opacity; transform.ScaleY = scale; transform.TranslateY = offset;
        if (!animate) { Finish(); return; }
        if (value && opacity < .01) transform.TranslateY = enteringOffset;

        var storyboard = new Storyboard();
        Add(indicator, "Opacity", indicator.Opacity, value ? 1 : 0);
        Add(transform, "ScaleY", transform.ScaleY, value ? 1 : .55);
        Add(transform, "TranslateY", transform.TranslateY, 0);
        animation = storyboard;
        storyboard.Completed += (_, _) =>
        {
            // 被新切换替代的动画不能在完成时把蓝条改回旧状态。
            if (!ReferenceEquals(animation, storyboard)) return;
            storyboard.Stop(); animation = null; Finish();
        };
        storyboard.Begin();

        void Add(DependencyObject target, string property, double from, double to)
        {
            var tween = new DoubleAnimation
            {
                From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(tween, target); Storyboard.SetTargetProperty(tween, property);
            storyboard.Children.Add(tween);
        }
    }

    void Finish()
    {
        indicator.Opacity = selected ? 1 : 0;
        transform.ScaleY = selected ? 1 : .55;
        transform.TranslateY = 0;
    }
}
