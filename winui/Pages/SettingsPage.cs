using CMCLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed class SettingsPage : CMCPage
{
    public SettingsPage(MainWindow window)
        : base(window) { }

    public override void Build()
    {
        Header("启动器设置", "");
        var settings = Store.Config.Settings;
        PageBody.Children.Add(Window.BuildLauncherUpdateSettings());

        var launcherLanguage = new BelowComboBox
        {
            Header = Ui("Launcher language"),
            ItemsSource = Window.InterfaceLanguages,
            SelectedItem =
                Window.InterfaceLanguages.FirstOrDefault(language =>
                    language.Code == settings.LauncherLanguage
                ) ?? Window.InterfaceLanguages.FirstOrDefault(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        launcherLanguage.SelectionChanged += async (_, _) =>
        {
            if (
                launcherLanguage.SelectedItem is not InterfaceLanguage language
                || language.Code == settings.LauncherLanguage
            )
                return;
            await SwitchLanguage(language.Code);
        };

        var language = new BelowComboBox
        {
            Header = Ui("Game language"),
            ItemsSource = new[]
            {
                Ui("context.settingspage.system_default"),
                Ui("Simplified Chinese"),
                Ui("Traditional Chinese"),
                "English",
                Ui("Japanese"),
                Ui("context.settingspage.keep_current"),
            },
            SelectedIndex = settings.GameLanguage switch
            {
                "zh_cn" => 1,
                "zh_tw" => 2,
                "en_us" => 3,
                "ja_jp" => 4,
                "preserve" => 5,
                _ => 0,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var memory = new NumberBox
        {
            Header = Ui("Maximum memory / MB"),
            Value = settings.MemoryMb,
            Minimum = 512,
            Maximum = 65536,
            SmallChange = 512,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var downloadThreads = new NumberBox
        {
            Header = Ui("Download threads"),
            Value = LauncherCore.DownloadConcurrency(settings),
            Minimum = 1,
            Maximum = 256,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };

        var automaticMemory = new ToggleSwitch
        {
            Header = Ui("Game memory"),
            OnContent = Ui("Automatic [context 467]"),
            OffContent = Ui("Manual"),
            IsOn = settings.AutoMemory,
        };
        var memoryHint = Text("", 12);
        void RefreshMemory()
        {
            memory.IsEnabled = !automaticMemory.IsOn;
            memoryHint.Text = automaticMemory.IsOn
                ? (
                    UiFormat(
                        "Recommended: {0:N0} MB. Recalculated at each launch based on physical and available memory.",
                        GameMemory.Resolve(new Settings())
                    )
                )
                : Ui("Set the maximum Java heap size manually.");
        }
        automaticMemory.Toggled += (_, _) => RefreshMemory();
        RefreshMemory();

        var customJvmArgs = new TextBox
        {
            Header = Ui("Custom JVM arguments"),
            PlaceholderText = "-Dexample=1 -XX:+UseG1GC",
            Text = settings.CustomJvmArgs,
            AcceptsReturn = false,
        };
        var jvmHint = Text("", 12);

        var proxy = new TextBox
        {
            Header = Ui("Download proxy (blank uses system proxy)"),
            PlaceholderText = "http://127.0.0.1:7890",
            Text = settings.Proxy,
        };
        var ignoreSslErrors = new ToggleSwitch
        {
            Header = Ui("Ignore SSL certificate errors"),
            IsOn = settings.IgnoreSslErrors,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        var sslWarning = Text(
            Ui("Skipping certificate validation reduces connection security."),
            12
        );

        var windowMode = new BelowComboBox
        {
            Header = Ui("Default window mode"),
            ItemsSource = new[] { Ui("Windowed"), Ui("Maximized") },
            SelectedIndex = settings.StartMaximized ? 1 : 0,
        };
        windowMode.SelectionChanged += async (_, _) =>
        {
            if (windowMode.SelectedIndex < 0)
                return;
            settings.StartMaximized = windowMode.SelectedIndex == 1;
            await PersistSettings();
        };

        var projectIntro = new ToggleSwitch
        {
            Header = Ui("Expand project descriptions by default [context 568]"),
            IsOn = settings.ExpandProjectIntroduction,
            OnContent = Ui("Expand"),
            OffContent = Ui("Collapse"),
        };

        var localizedWindow = new BelowComboBox
        {
            Header = Ui("Default window mode"),
            ItemsSource = new[] { Ui("Windowed"), Ui("Maximized") },
            SelectedIndex = windowMode.SelectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        localizedWindow.SelectionChanged += async (_, _) =>
        {
            if (localizedWindow.SelectedIndex >= 0)
            {
                windowMode.SelectedIndex = localizedWindow.SelectedIndex;
                settings.StartMaximized = localizedWindow.SelectedIndex == 1;
                await PersistSettings();
            }
        };

        var launcherVisibility = new BelowComboBox
        {
            Header = Ui("Launcher visibility"),
            ItemsSource = new[]
            {
                Ui("Launcher stays visible"),
                Ui("Close launcher after launch"),
                Ui("Hide launcher, close when game exits"),
                Ui("Hide launcher, reopen when game exits"),
                Ui("Minimize launcher after launch"),
            },
            SelectedIndex = settings.LauncherVisibility switch
            {
                "close" => 1,
                "hide_close" => 2,
                "hide_reopen" => 3,
                "minimize" => 4,
                _ => 0,
            },
        };
        launcherVisibility.SelectionChanged += async (_, _) =>
        {
            if (launcherVisibility.SelectedIndex < 0)
                return;
            settings.LauncherVisibility = launcherVisibility.SelectedIndex switch
            {
                1 => "close",
                2 => "hide_close",
                3 => "hide_reopen",
                4 => "minimize",
                _ => "keep",
            };
            await PersistSettings();
        };

        var gamePriority = new BelowComboBox
        {
            Header = Ui("Game process priority"),
            ItemsSource = new[]
            {
                Ui("Normal priority"),
                Ui("Above normal priority"),
                Ui("High priority"),
                Ui("Realtime priority"),
                Ui("Below normal priority"),
                Ui("Idle priority"),
            },
            SelectedIndex = settings.PlayerPriority switch
            {
                "above" => 1,
                "high" => 2,
                "realtime" => 3,
                "below" => 4,
                "idle" => 5,
                _ => 0,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        gamePriority.SelectionChanged += async (_, _) =>
        {
            if (gamePriority.SelectedIndex < 0)
                return;
            settings.PlayerPriority = gamePriority.SelectedIndex switch
            {
                1 => "above",
                2 => "high",
                3 => "realtime",
                4 => "below",
                5 => "idle",
                _ => "normal",
            };
            await PersistSettings();
        };

        var gameWindowTitle = new TextBox
        {
            Header = Ui("Custom game window title"),
            PlaceholderText = Ui("Blank keeps the game default"),
            Text = settings.GameWindowTitle,
            AcceptsReturn = false,
        };
        gameWindowTitle.TextChanged += async (_, _) =>
        {
            settings.GameWindowTitle = gameWindowTitle.Text.TrimStart();
            await PersistSettings();
        };

        var localizedGameLanguage = new BelowComboBox
        {
            Header = Ui("Game language"),
            ItemsSource = new[]
            {
                Ui("context.settingspage.system_default"),
                Ui("Simplified Chinese"),
                Ui("Traditional Chinese"),
                "English",
                Ui("Japanese"),
                Ui("context.settingspage.keep_current"),
            },
            SelectedIndex = language.SelectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        localizedGameLanguage.SelectionChanged += async (_, _) =>
        {
            if (localizedGameLanguage.SelectedIndex >= 0)
            {
                language.SelectedIndex = localizedGameLanguage.SelectedIndex;
                settings.GameLanguage = language.SelectedIndex switch
                {
                    1 => "zh_cn",
                    2 => "zh_tw",
                    3 => "en_us",
                    4 => "ja_jp",
                    5 => "preserve",
                    _ => "auto",
                };
                await PersistSettings();
            }
        };

        foreach (
            var control in new UIElement[]
            {
                launcherLanguage,
                localizedWindow,
                projectIntro,
                localizedGameLanguage,
                launcherVisibility,
                gamePriority,
                gameWindowTitle,
                customJvmArgs,
                jvmHint,
                automaticMemory,
                memory,
                memoryHint,
                downloadThreads,
                proxy,
                ignoreSslErrors,
                sslWarning,
            }
        )
            PageBody.Children.Add(control);

        async Task PersistSettings()
        {
            try
            {
                await Store.Save();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        }

        language.SelectionChanged += async (_, _) =>
        {
            if (language.SelectedIndex < 0)
                return;
            settings.GameLanguage = language.SelectedIndex switch
            {
                1 => "zh_cn",
                2 => "zh_tw",
                3 => "en_us",
                4 => "ja_jp",
                5 => "preserve",
                _ => "auto",
            };
            await PersistSettings();
        };
        projectIntro.Toggled += async (_, _) =>
        {
            settings.ExpandProjectIntroduction = projectIntro.IsOn;
            await PersistSettings();
        };
        automaticMemory.Toggled += async (_, _) =>
        {
            settings.AutoMemory = automaticMemory.IsOn;
            await PersistSettings();
        };
        memory.ValueChanged += async (_, _) =>
        {
            if (double.IsNaN(memory.Value) || memory.Value < 512 || memory.Value > 65536)
                return;
            settings.MemoryMb = (int)memory.Value;
            await PersistSettings();
        };
        downloadThreads.ValueChanged += async (_, _) =>
        {
            if (double.IsNaN(downloadThreads.Value))
                return;
            var value = Math.Clamp((int)Math.Round(downloadThreads.Value), 1, 256);
            if (downloadThreads.Value != value)
                downloadThreads.Value = value;
            settings.DownloadThreads = value;
            await PersistSettings();
        };

        void RefreshJvmHint()
        {
            var tokens = LauncherCore.SplitJvmArgs(customJvmArgs.Text);
            jvmHint.Text = tokens.Count > 0
                ? UiFormat("Custom JVM arguments ({0}): {1}", tokens.Count, string.Join(" ", tokens))
                : "";
        }
        customJvmArgs.TextChanged += async (_, _) =>
        {
            settings.CustomJvmArgs = customJvmArgs.Text.Trim();
            await PersistSettings();
            RefreshJvmHint();
        };

        var proxyHint = Text("", 12);
        PageBody.Children.Add(proxyHint);
        proxy.TextChanged += async (_, _) =>
        {
            var value = proxy.Text.Trim();
            if (
                value.Length > 0
                && (
                    !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("http" or "https" or "socks5")
                    || string.IsNullOrWhiteSpace(uri.Host)
                )
            )
            {
                proxyHint.Text = Ui(
                    "Enter a valid HTTP, HTTPS, or SOCKS5 proxy address. The last valid setting remains active."
                );
                return;
            }
            proxyHint.Text = "";
            settings.Proxy = value;
            Core.ConfigureNetwork();
            await PersistSettings();
        };
        ignoreSslErrors.Toggled += async (_, _) =>
        {
            settings.IgnoreSslErrors = ignoreSslErrors.IsOn;
            Core.ConfigureNetwork();
            await PersistSettings();
        };

        PageBody.Children.Add(Text(UiFormat("Current version: {0}", LauncherUpdates.CurrentVersion), 12));
    }
}