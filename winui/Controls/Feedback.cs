using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    internal sealed record PopconfirmRequest(
        FrameworkElement Anchor,
        string Title,
        UIElement Body,
        string ConfirmText
    );

    Func<PopconfirmRequest, Task<bool>>? popconfirmOverride;
    Flyout? activePopconfirm;
    DispatcherQueueTimer? toastTimer;

    void InitializeFeedback()
    {
        toastTimer = DispatcherQueue.CreateTimer();
        toastTimer.Interval = TimeSpan.FromSeconds(4);
        toastTimer.IsRepeating = false;
        toastTimer.Tick += (_, _) => TaskToast.IsOpen = false;
        TaskToast.Closed += (_, _) => toastTimer.Stop();
    }

    internal void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        toastTimer?.Stop();
        RefreshToastPalette();
        TaskToast.MaxWidth = Math.Max(
            0,
            Math.Min(440, ((FrameworkElement)Content).ActualWidth - 48)
        );
        TaskToast.Tag =
            severity == InfoBarSeverity.Success ? "\uE73E"
            : severity == InfoBarSeverity.Error ? "\uE711"
            : "\uE946";
        TaskToast.Severity = severity;
        TaskToast.Message = message;
        TaskToast.IsOpen = true;
        toastTimer?.Start();
    }

    void RefreshToastPalette()
    {
        var color = PaletteColor(PrimaryColor);
        TaskToast.Background = new SolidColorBrush(color);
        TaskToast.BorderBrush = new SolidColorBrush(color);
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        TaskToast.Foreground = new SolidColorBrush(
            luminance > .179 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White
        );
    }

    internal Task<bool> ConfirmPop(
        FrameworkElement anchor,
        string title,
        UIElement body,
        string? confirmText = null
    )
    {
        var request = new PopconfirmRequest(anchor, title, body, confirmText ?? Ui("Delete"));
        if (popconfirmOverride != null)
            return popconfirmOverride(request);
        activePopconfirm?.Hide();
        var result = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var panel = new StackPanel
        {
            Spacing = 12,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
        };
        var heading = Text(title, 16, true);
        heading.Foreground = ComboTextBrush;
        var cancel = new Button
        {
            Content = Ui("Cancel"),
            Tag = "popconfirm-cancel",
            Background = ComboSurfaceBrush,
            Foreground = ComboTextBrush,
            BorderBrush = ComboBorderBrush,
        };
        var confirm = new Button
        {
            Content = request.ConfirmText,
            Tag = "popconfirm-confirm",
            Background = ComboSelectedBrush,
            Foreground = ComboTextBrush,
        };
        panel.Children.Add(heading);
        panel.Children.Add(body);
        panel.Children.Add(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, confirm },
            }
        );
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(
            new Setter(
                FrameworkElement.RequestedThemeProperty,
                lightTheme ? ElementTheme.Light : ElementTheme.Dark
            )
        );
        style.Setters.Add(new Setter(Control.BackgroundProperty, ComboSurfaceBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ComboTextBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, ComboBorderBrush));
        style.Setters.Add(
            new Setter(
                FrameworkElement.MaxWidthProperty,
                Math.Max(120, Math.Min(420, ((FrameworkElement)Content).ActualWidth - 32))
            )
        );
        style.Setters.Add(
            new Setter(
                FrameworkElement.MaxHeightProperty,
                Math.Max(120, ((FrameworkElement)Content).ActualHeight - 48)
            )
        );
        var flyout = new Flyout
        {
            XamlRoot = anchor.XamlRoot,
            Content = panel,
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = style,
        };
        activePopconfirm = flyout;
        cancel.Click += (_, _) => flyout.Hide();
        confirm.Click += (_, _) =>
        {
            result.TrySetResult(true);
            flyout.Hide();
        };
        flyout.Closed += (_, _) =>
        {
            result.TrySetResult(false);
            if (ReferenceEquals(activePopconfirm, flyout))
                activePopconfirm = null;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (anchor is Control { IsLoaded: true, IsEnabled: true } target)
                    target.Focus(FocusState.Programmatic);
            });
        };
        flyout.Opened += (_, _) => cancel.Focus(FocusState.Programmatic);
        flyout.ShowAt(anchor);
        return result.Task;
    }

    internal Task<bool> ConfirmPop(
        FrameworkElement anchor,
        string title,
        string message,
        string? confirmText = null
    )
    {
        var text = Text(message, 13);
        text.Foreground = ComboTextBrush;
        return ConfirmPop(anchor, title, text, confirmText);
    }

    void RecyclePath(string path)
    {
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
            );
        else
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
            );
    }
}
