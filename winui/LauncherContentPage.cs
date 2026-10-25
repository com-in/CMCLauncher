using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CMCLauncher.WinUI;

// Each navigation creates a new Page so WinUI can run its navigation transition.
public sealed partial class LauncherContentPage : Page
{
    public LauncherContentPage() { InitializeComponent(); }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        // 页面内容在主窗口中构建，通过导航参数交给新的 Page 承载。
        Content = (UIElement)args.Parameter;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
