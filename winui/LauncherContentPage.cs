using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CraftLauncher.WinUI;

// Each navigation creates a new Page so WinUI can run its navigation transition.
public sealed partial class LauncherContentPage : Page
{
    public LauncherContentPage() { InitializeComponent(); }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        Content = (UIElement)args.Parameter;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
