using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyFrpcPage(Action<bool, string> check, string directory)
    {
        var savedPage = page;
        var savedLanguage = store.Config.Settings.LauncherLanguage;
        var savedVersion = store.Config.Settings.FrpcVersion;
        var savedConfig = store.Config.Settings.FrpcConfigPath;
        var savedFetch = fetchFrpcReleases;
        var fixtureExe = frpc.Executable("0.71.1");
        var createdFixtureExe = false;
        try
        {
            fetchFrpcReleases = (page, _) =>
                Task.FromResult(
                    new FrpcReleasePage(page == 1 ? ["0.71.1", "0.70.0"] : ["0.69.0"], page == 1)
                );
            frpcOnlineVersions.Clear();
            var source = Path.Combine(directory, "original-frpc.ini");
            await File.WriteAllTextAsync(source, "[common]\nserver_port=7000");
            store.Config.Settings.FrpcConfigPath = source;
            page = "frpc";
            await RenderAsync();
            await frpcConfigurationLoad;
            await frpcVersionsLoad;
            check(
                Descendants(PageBody)
                    .OfType<Expander>()
                    .Single(e => e.Tag?.ToString() == "frpc-download-panel")
                    .IsExpanded,
                "download section opens by default when frpc is not installed"
            );
            var moreReleases = Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Content?.ToString() == Ui("Load more releases"));
            (
                (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(
                        moreReleases
                    ).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
            ).Invoke();
            await Task.Delay(100);
            await frpcVersionsLoad;
            check(
                frpcOnlineVersions.Count == 3,
                "frpc historical version button loads the next online release page"
            );
            check(
                frpcConfigs.Contains(store.Config.Settings.FrpcConfigPath)
                    && store.Config.Settings.FrpcConfigPath != source,
                "existing external frpc configuration migrates to a program-directory copy"
            );
            frpcEditor!.Text = "[common]\nserver_port=7001";
            await SaveFrpcEditor();
            check(
                (await File.ReadAllTextAsync(source)).Contains("7000")
                    && (await frpcConfigs.Read(frpcEditorPath)).Contains("7001"),
                "frpc editor saves only its managed copy"
            );
            frpcConfigName!.Text = "manual-profile";
            await SaveFrpcEditor(true);
            check(
                frpcConfigs.List().Count == 2,
                "multiple copied and manually saved frpc profiles coexist"
            );
            check(
                navigationButtons.ContainsKey("page:frpc"),
                "More navigation exposes the frpc client"
            );
            if (!File.Exists(fixtureExe))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fixtureExe)!);
                await File.WriteAllTextAsync(fixtureExe, "MZ UI fixture only");
                createdFixtureExe = true;
            }
            foreach (var language in new[] { "zh_cn", "en_us" })
            foreach (var width in new[] { 740, 1280 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 820));
                store.Config.Settings.LauncherLanguage = language;
                page = "frpc";
                await RenderAsync();
                await frpcConfigurationLoad;
                await frpcVersionsLoad;
                await Task.Delay(350);
                var downloads = Descendants(PageBody)
                    .OfType<Expander>()
                    .Single(e => e.Tag?.ToString() == "frpc-download-panel");
                var imports = Descendants(PageBody)
                    .OfType<Expander>()
                    .Single(e => e.Tag?.ToString() == "frpc-import-panel");
                var newConfiguration = Descendants(PageBody)
                    .OfType<Expander>()
                    .Single(e => e.Tag?.ToString() == "frpc-new-panel");
                check(
                    !downloads.IsExpanded && !imports.IsExpanded && !newConfiguration.IsExpanded,
                    "installed frpc defaults to collapsed download, new and import panels "
                        + language
                        + " "
                        + width
                );
                var editing = (StackPanel)frpcEditor!.Parent;
                var configurationBody = (StackPanel)editing.Parent;
                check(
                    configurationBody.Children.IndexOf(imports)
                        > configurationBody.Children.IndexOf(editing)
                        && configurationBody.Children.IndexOf(newConfiguration)
                            > configurationBody.Children.IndexOf(editing),
                    "new and import panels follow the saved configuration editor "
                        + language
                        + " "
                        + width
                );
                check(
                    Descendants((FrameworkElement)newConfiguration.Content)
                        .OfType<TextBox>()
                        .Any(t => t.Tag?.ToString() == "frpc-new-editor")
                        || ((StackPanel)newConfiguration.Content)
                            .Children.OfType<TextBox>()
                            .Any(t => t.Tag?.ToString() == "frpc-new-editor"),
                    "new configuration content belongs to the collapsed creation panel "
                        + language
                        + " "
                        + width
                );
                await Capture(Path.Combine(directory, $"frpc-main-{language}-{width}.png"));
                downloads.IsExpanded = true;
                imports.IsExpanded = true;
                newConfiguration.IsExpanded = true;
                await Task.Delay(350);
                await frpcVersionsLoad;
                var fields = Descendants(PageBody).OfType<TextBox>().ToArray();
                var importPath = fields.Single(t => t.Tag?.ToString() == "frpc-import-path");
                importPath.Text =
                    "C:\\very-long-configuration-folder\\" + new string('x', 140) + "\\frpc.toml";
                PageViewport.UpdateLayout();
                var importY = imports
                    .TransformToVisual(PageBody)
                    .TransformPoint(new Windows.Foundation.Point())
                    .Y;
                PageViewport.ChangeView(null, Math.Max(0, importY - 12), null, true);
                await Task.Delay(250);
                var inputPosition = importPath
                    .TransformToVisual(imports)
                    .TransformPoint(new Windows.Foundation.Point());
                check(
                    importPath.ActualWidth > 40
                        && inputPosition.X >= 0
                        && inputPosition.X + importPath.ActualWidth <= imports.ActualWidth - 8,
                    "long import path stays inside its padded panel " + language + " " + width
                );
                await Capture(Path.Combine(directory, $"frpc-import-{language}-{width}.png"));
                check(
                    fields.Any(t => t.Tag?.ToString() == "frpc-import-path")
                        && fields.Any(t => t.Tag?.ToString() == "frpc-config-editor")
                        && fields.Any(t => t.Tag?.ToString() == "frpc-output"),
                    "frpc import, editor and log fields " + language + " " + width
                );
                check(
                    Descendants(PageBody)
                        .OfType<ComboBox>()
                        .Single(c => c.Tag?.ToString() == "frpc-download-version")
                        .Items.Count == 3,
                    "frpc download versions come from the online catalog " + language + " " + width
                );
                check(
                    !frpc.Running && frpcStopButton?.IsEnabled == false,
                    "frpc does not start on page navigation " + language + " " + width
                );
                check(
                    PageBody.ActualWidth <= PageViewport.ActualWidth + 1
                        && fields.All(f => f.ActualWidth <= PageViewport.ActualWidth + 1),
                    "frpc fields fit narrow and wide layouts " + language + " " + width
                );
                await Capture(Path.Combine(directory, $"frpc-{language}-{width}.png"));
                downloads.IsExpanded = false;
                imports.IsExpanded = false;
                newConfiguration.IsExpanded = false;
                await Task.Delay(400);
                PageViewport.UpdateLayout();
                var editorY = frpcEditor!
                    .TransformToVisual(PageBody)
                    .TransformPoint(new Windows.Foundation.Point())
                    .Y;
                PageViewport.ChangeView(null, Math.Max(0, editorY - 20), null, true);
                await Task.Delay(150);
                await Capture(
                    Path.Combine(directory, $"frpc-configurations-{language}-{width}.png")
                );
            }
            var newPanel = Descendants(PageBody)
                .OfType<Expander>()
                .Single(e => e.Tag?.ToString() == "frpc-new-panel");
            newPanel.IsExpanded = true;
            await Task.Delay(200);
            frpcConfigName!.Text = "blank-profile";
            frpcFormat!.SelectedItem = "toml";
            var draft = Descendants(newPanel)
                .OfType<TextBox>()
                .Single(t => t.Tag?.ToString() == "frpc-new-editor");
            var originalText = frpcEditor!.Text;
            draft.Text = "serverAddr = \"127.0.0.1\"\rserverPort = 7000";
            var nameTip = new ToolTip
            {
                Content = Ui("Configuration name"),
                XamlRoot = Content.XamlRoot,
                PlacementTarget = frpcConfigName,
            };
            ToolTipService.SetToolTip(frpcConfigName, nameTip);
            nameTip.IsOpen = true;
            check(
                frpcEditor.Text == originalText,
                "new configuration draft does not overwrite the current saved editor"
            );
            var create = Descendants(newPanel)
                .OfType<Button>()
                .Single(b => b.Content?.ToString() == Ui("Create configuration"));
            (
                (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(create).GetPattern(
                        Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke
                    )
            ).Invoke();
            for (
                var attempt = 0;
                attempt < 30 && (!frpcEditorPath.Contains("blank-profile") || newPanel.IsExpanded);
                attempt++
            )
                await Task.Delay(50);
            check(
                Path.GetExtension(frpcEditorPath) == ".toml"
                    && frpcConfigs.List().Count == 3
                    && !newPanel.IsExpanded,
                "complete creation form saves a named TOML file and closes after success"
            );
            check(
                ((FrameworkElement)newPanel.Content).Visibility == Visibility.Collapsed
                    && !frpcFormat.IsDropDownOpen,
                "successful creation hides the entire form and dismisses its dropdown"
            );
            check(
                !nameTip.IsOpen,
                "configuration name tooltip cannot float after creation collapses its owner"
            );
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot);
            check(
                !ReferenceEquals(focused, frpcConfigName) && !ReferenceEquals(focused, draft),
                "collapsed creation form does not retain input focus"
            );
            newPanel.IsExpanded = true;
            await Task.Delay(100);
            check(
                ((FrameworkElement)newPanel.Content).Visibility == Visibility.Visible,
                "creation form can reopen after successful creation"
            );
            newPanel.IsExpanded = false;
        }
        finally
        {
            store.Config.Settings.FrpcVersion = savedVersion;
            store.Config.Settings.FrpcConfigPath = savedConfig;
            store.Config.Settings.LauncherLanguage = savedLanguage;
            fetchFrpcReleases = savedFetch;
            frpcOnlineVersions.Clear();
            if (createdFixtureExe)
                File.Delete(fixtureExe);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
            page = savedPage;
            await store.Save();
            await RenderAsync();
        }
    }
}
