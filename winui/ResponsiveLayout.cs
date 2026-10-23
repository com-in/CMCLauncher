using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    void PageViewport_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        // A finite width is required before measuring long text in star columns.
        if (args.NewSize.Width > 0) PageFrame.Width = args.NewSize.Width;
        UpdateTaskDrawerWidth();
    }
    void UpdateTaskDrawerWidth()
    {
        var fontSize = store.Config.Settings.UiFontSize;
        var fontScale = (double.IsFinite(fontSize) ? Math.Clamp(fontSize, 12, 22) : 14) / 14;
        TaskDrawer.OpenPaneLength = Math.Min(480 * Math.Max(1, fontScale), Math.Max(0, ((FrameworkElement)Content).ActualWidth - 24));
    }
    static void AdaptCard(Grid card, FrameworkElement detail, FrameworkElement actions)
    {
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 480;
            Grid.SetColumnSpan(detail, narrow ? 2 : 1);
            Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 1 : 2);
            Grid.SetColumnSpan(actions, narrow ? 2 : 1);
            actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            actions.Margin = narrow ? new Thickness(0, 7, 0, 0) : new Thickness(0);
        };
    }
}
