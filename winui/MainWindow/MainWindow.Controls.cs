using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Text.Json.Nodes;
using Windows.UI.ViewManagement;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    readonly LocalizationCatalog localization = new(Path.Combine(AppContext.BaseDirectory, "locales"), message => RuntimeLog.Write("WARN", message));
    internal IReadOnlyList<InterfaceLanguage> InterfaceLanguages => localization.Languages;
    internal string Ui(string key) => localization.Translate(store.Config.Settings.LauncherLanguage, key);
    internal string UiFormat(string key, params object?[] values) => localization.Format(store.Config.Settings.LauncherLanguage, key, values);
    internal TextBlock Text(string text, double size = 14, bool bold = false)
    {
        var block = new TextBlock { Text = Ui(text), FontSize = size, Foreground = Brush(lightTheme ? "#303145" : "#F0F0FA"), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        originalAppearances.Add(block, new() { FontSize = size });
        block.FontSize = size * (double.IsFinite(store.Config.Settings.UiFontSize) ? Math.Clamp(store.Config.Settings.UiFontSize, 12, 22) : 14) / 14;
        block.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(store.Config.Settings.UiFontFamily) ? "Segoe UI Variable Text" : store.Config.Settings.UiFontFamily);
        return block;
    }
    internal Button Action(string label, System.Action<object, RoutedEventArgs> click, bool primary = false) => Action(label, (sender, args) => { click(sender, args); return Task.CompletedTask; }, primary);
    // 页面操作共用异常处理和防重复点击逻辑，创建时也登记到任务按钮列表。
    internal Button Action(string label, Func<object, RoutedEventArgs, Task> click, bool primary = false)
    {
        var button = new Button { Content = Ui(label), Padding = new Thickness(12, 7, 12, 7), MinWidth = 84, MinHeight = 34, CornerRadius = new CornerRadius(5), IsEnabled = task == null };
        button.Loaded += (_, _) => ApplyPersonalization();
        if (primary)
        {
            button.Background = Brush(PrimaryColor); button.Foreground = new SolidColorBrush(Colors.White); button.BorderThickness = new Thickness(0);
            button.Resources["ButtonBackgroundPointerOver"] = Brush(lightTheme ? "#507F44" : "#637DCD");
            button.Resources["ButtonBackgroundPressed"] = Brush(lightTheme ? "#355B2D" : "#465DA5");
            button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
            button.Resources["ButtonForegroundPressed"] = new SolidColorBrush(Colors.White);
        }
        button.Click += async (sender, args) => { RuntimeLog.Write("ACTION", "点击按钮 · " + label); button.IsEnabled = false; try { await click(sender, args); RuntimeLog.Write("ACTION", "按钮完成 · " + label); } catch (OperationCanceledException) { Report("操作已取消"); RuntimeLog.Write("CANCEL", label); } catch (Exception ex) { RuntimeLog.Write("ERROR", label + " · " + ex); await Error(ex); } finally { button.IsEnabled = task == null; } };
        taskButtons.Add(button); return button;
    }
    // 同时发生的错误只开一个对话框，完整异常由日志保留。
    internal async Task Error(Exception ex)
    {
        Report(ex.Message);
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var detail = ex.Message.Replace("\r\n", "\n").Replace('\r', '\n');
        var firstLine = Ui(detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "操作失败");
            await new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("Operation incomplete"), Content = $"{firstLine}\n\n{Ui("Detailed error information was written to the log.")}", CloseButtonText = Ui("OK") }.ShowAsync();
        }
        finally { dialogOpen = false; }
    }
    internal Border Card(UIElement content) => CreateCard(content, new Thickness(16), 6);

    internal Border CompactCard(UIElement content) => CreateCard(content, new Thickness(11, 7, 11, 7), 5);

    internal Border CreateCard(UIElement content, Thickness padding, double cornerRadius)
    {
        var card = new Border
        {
            Child = content,
            Padding = padding,
            CornerRadius = new CornerRadius(cornerRadius),
            Background = Brush(lightTheme ? "#FFFFFF" : "#242530"),
            BorderBrush = Brush(lightTheme ? "#E4E3ED" : "#3A3B4B"),
            BorderThickness = new Thickness(1)
        };
        themeCards.Add(card);
        return card;
    }

    internal async Task SelectInstance(Instance instance)
    {
        selected = instance; store.Config.SelectedInstanceId = instance.Id; world = null;
        await store.Save();
    }
    internal ComboBox InstanceSelector() { var combo = new BelowComboBox { ItemsSource = store.Config.Instances, DisplayMemberPath = nameof(Instance.Name), SelectedItem = selected, PlaceholderText = Ui("Select an instance"), HorizontalAlignment = HorizontalAlignment.Stretch }; combo.SelectionChanged += async (_, _) => { if (combo.SelectedItem is Instance instance && instance != selected) { await SelectInstance(instance); Render(); } }; return combo; }
    internal StackPanel Buttons(params Button[] buttons) { var panel = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left }; foreach (var button in buttons) panel.Children.Add(button); return panel; }
    // 先用包内图标，再查项目图标缓存；读取或解码失败就显示占位图标。
    async Task<UIElement> ContentIcon(string file, string? project = null, byte[]? knownIcon = null, bool inspected = false)
    {
        var bytes = await Task.Run(() =>
        {
            var embedded = inspected ? knownIcon : ContentManager.Icon(file); if (embedded != null || string.IsNullOrEmpty(project)) return embedded;
            var iconRoot = Path.Combine(store.Library, "mod-icons");
            if (!Directory.Exists(iconRoot) || !System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-zA-Z0-9]+$")) return null;
            var cached = Directory.EnumerateFiles(iconRoot, project + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            return cached == null || new FileInfo(cached).Length > 8 * 1024 * 1024 ? null : File.ReadAllBytes(cached);
        });
        var fallback = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(6), Background = Brush(lightTheme ? "#EEEAF7" : "#343044"), Child = new FontIcon { Glyph = "\uE7FC", FontSize = 20 } };
        if (bytes == null) return fallback;
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            return new Image { Width = 44, Height = 44, Source = bitmap, Stretch = Stretch.Uniform };
        }
        catch { return fallback; }
    }
    UIElement RemoteIcon(string url)
    {
        var holder = new Grid { Width = 44, Height = 44 };
        holder.Children.Add(new FontIcon { Glyph = "\uE7FC", FontSize = 22, Opacity = 0.5 });
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") holder.Children.Add(new Image { Source = new BitmapImage(uri), Stretch = Stretch.Uniform });
        return holder;
    }
    internal void Header(string title, string subtitle)
    {
        PageBackButton.Visibility = page is "version-settings" or "instances" or "resource-detail" ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PageBackButton, Ui(page == "resource-detail" ? "返回列表" : "返回主页"));
        PageTitle.Text = Ui(title); PageSubtitle.Text = Ui(subtitle);
        PageTitle.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
        PageSubtitle.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        HomeCustomizeButton.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        HomeCustomizeButton.Foreground = Brush(lightTheme ? "#41424D" : "#D1D1DC");
        ToolTipService.SetToolTip(HomeCustomizeButton, homeCustomizeMode ? Ui("Finish customizing") : Ui("Customize home"));
    }
    void PageBack_Click(object sender, RoutedEventArgs e)
    {
        if (page == "resource-detail") page = resourceReturnPage;
        else { page = "home"; expandedNavigation = "home"; }
        Render();
    }
    string LoaderCategory(string loader) => loader.ToLowerInvariant() switch { "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", _ => Ui("Vanilla") };
    async Task ImportContent(Instance instance, string type, string? world)
    {
        var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(type == "mod" ? ".jar" : ".zip"); var files = await picker.PickMultipleFilesAsync(); if (files.Count == 0) return; await Run(_ => { ContentManager.Import(store.GameDirectory(instance), type, world, files.Select(f => f.Path)); return Task.CompletedTask; }); Render();
    }
    void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
}
