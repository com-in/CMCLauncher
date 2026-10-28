using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CMCLauncher.WinUI;

// 按按钮实际宽度换行，末行不拉伸，避免页签之间出现大片空白。
sealed class FlowPanel : Panel
{
    const double Gap = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        return Layout(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    Size Layout(double width, bool arrange)
    {
        double x = 0,
            y = 0,
            height = 0,
            used = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += height + Gap;
                x = 0;
                height = 0;
            }
            if (arrange)
                child.Arrange(new Rect(x, y, Math.Min(size.Width, width), size.Height));
            used = Math.Max(used, x + size.Width);
            x += size.Width + Gap;
            height = Math.Max(height, size.Height);
        }
        return new Size(Math.Min(width, used), y + height);
    }
}
