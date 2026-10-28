using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    FrpcConfigurations frpcConfigs = null!;
    readonly Dictionary<string, string> frpcDrafts = new(StringComparer.OrdinalIgnoreCase);
    TextBox? frpcEditor,
        frpcConfigName;
    ComboBox? frpcFormat;
    string frpcEditorPath = "";
    Task frpcConfigurationLoad = Task.CompletedTask;
    bool changingFrpcEditor;
    string frpcNewName = "";
    string frpcNewFormat = "toml";
    string frpcNewText = "";

    async Task SaveFrpcEditor(bool copy = false)
    {
        await frpcConfigurationLoad;
        if (frpc.Running)
            throw new IOException(Ui("Stop frpc before changing configurations."));
        if (frpcEditor == null || frpcConfigName == null)
            return;
        var text = frpcEditor.Text;
        if (copy || frpcEditorPath.Length == 0)
        {
            var created = await frpcConfigs.Create(
                frpcConfigName.Text.Trim(),
                frpcFormat?.SelectedItem as string ?? "toml",
                text,
                default
            );
            frpcEditorPath = created.Path;
            frpcDrafts[created.Path] = text;
        }
        else
            await frpcConfigs.Save(frpcEditorPath, text, default);
        store.Config.Settings.FrpcConfigPath = frpcConfigs.Owned(frpcEditorPath);
        await store.Save();
    }

    void BuildFrpcConfigurations()
    {
        var generation = renderVersion;
        var selected = new BelowComboBox
        {
            Header = Ui("Select configuration"),
            PlaceholderText = Ui("Choose or create a configuration"),
            Padding = new Thickness(12, 7, 36, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "frpc-configurations",
        };
        var info = Text(Ui("Loading configurations…"), 12);
        info.MaxLines = 1;
        info.TextWrapping = TextWrapping.NoWrap;
        info.TextTrimming = TextTrimming.CharacterEllipsis;
        var choiceRevision = 0;
        Expander? importPanel = null;
        var importPath = new TextBox
        {
            Header = Ui("File or folder path"),
            PlaceholderText = Ui("Enter a file or folder path"),
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Tag = "frpc-import-path",
        };
        frpcConfigName = new TextBox
        {
            Header = Ui("Configuration name"),
            PlaceholderText = Ui("Enter a configuration name"),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Text = frpcNewName,
            MaxLength = 80,
            Tag = "frpc-config-name",
        };
        frpcFormat = new BelowComboBox
        {
            Header = Ui("Format"),
            Padding = new Thickness(12, 7, 36, 7),
            ItemsSource = new[] { "toml", "ini" },
            SelectedItem = frpcNewFormat,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "frpc-config-format",
        };
        frpcEditor = new TextBox
        {
            Header = Ui("Configuration content"),
            Text = "",
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 180,
            MaxHeight = 360,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            MaxLength = 2 * 1024 * 1024,
            Tag = "frpc-config-editor",
        };
        var editing = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        frpcConfigName.TextChanged += (_, _) => frpcNewName = frpcConfigName.Text;
        frpcFormat.SelectionChanged += (_, _) =>
            frpcNewFormat = frpcFormat.SelectedItem as string ?? "toml";
        frpcEditor.TextChanged += (_, _) =>
        {
            if (!changingFrpcEditor)
                frpcDrafts[frpcEditorPath] = frpcEditor.Text;
        };
        async Task Choose(string path)
        {
            var revision = ++choiceRevision;
            var text =
                frpcDrafts.TryGetValue(path, out var draft) ? draft
                : path.Length == 0 ? ""
                : await frpcConfigs.Read(path);
            if (generation != renderVersion || revision != choiceRevision)
                return;
            changingFrpcEditor = true;
            frpcEditorPath = path;
            frpcEditor.Text = text;
            editing.Visibility = path.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            changingFrpcEditor = false;
            store.Config.Settings.FrpcConfigPath = path;
            await store.Save();
            info.Text =
                path.Length == 0 ? Ui("Expand New configuration or Import to begin.") : path;
            ToolTipService.SetToolTip(info, info.Text);
            UpdateFrpcStatus();
        }
        var changingSelection = false;
        selected.SelectionChanged += async (_, _) =>
        {
            if (changingSelection)
                return;
            try
            {
                await Choose((selected.SelectedItem as FrpcConfiguration)?.Path ?? "");
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        async Task Reload(string? choosePath = null)
        {
            try
            {
                var path = choosePath ?? store.Config.Settings.FrpcConfigPath;
                if (path.Length > 0 && File.Exists(path))
                {
                    if (frpcConfigs.Contains(path))
                        frpcConfigs.Owned(path);
                    else
                        path = (await frpcConfigs.Import(path, default)).First().Path;
                }
                var items = frpcConfigs.List();
                if (generation != renderVersion)
                    return;
                changingSelection = true;
                selected.ItemsSource = items;
                selected.SelectedItem = items.FirstOrDefault(c =>
                    c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                );
                changingSelection = false;
                await Choose((selected.SelectedItem as FrpcConfiguration)?.Path ?? "");
            }
            catch (Exception ex)
            {
                if (generation == renderVersion)
                    info.Text = Ui("Could not load configurations: ") + ex.Message;
            }
        }
        async Task Import(string source)
        {
            if (frpc.Running)
                throw new IOException(Ui("Stop frpc before importing configurations."));
            List<FrpcConfiguration> imported = [];
            await Run(
                async ct => imported = await frpcConfigs.Import(source, ct),
                Ui("Import frpc configurations")
            );
            await Reload(imported.First().Path);
            if (importPanel != null)
                importPanel.IsExpanded = false;
        }
        var imports = new FlowPanel();
        imports.Children.Add(
            Action(Ui("Add path"), async (_, _) => await Import(importPath.Text.Trim().Trim('"')))
        );
        imports.Children.Add(
            Action(
                Ui("Choose files"),
                async (_, _) =>
                {
                    var picker = new FileOpenPicker();
                    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                    picker.FileTypeFilter.Add(".ini");
                    picker.FileTypeFilter.Add(".toml");
                    var files = await picker.PickMultipleFilesAsync();
                    foreach (var file in files)
                        await Import(file.Path);
                }
            )
        );
        imports.Children.Add(
            Action(
                Ui("Choose folder"),
                async (_, _) =>
                {
                    var picker = new FolderPicker();
                    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                    picker.FileTypeFilter.Add("*");
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null)
                        await Import(folder.Path);
                }
            )
        );
        var importBody = new StackPanel
        {
            Spacing = 10,
            MinWidth = 0,
            Children =
            {
                importPath,
                imports,
                Text(
                    Ui(
                        "Imports use copies beside the launcher. Choose a folder to preserve certificates and relative includes."
                    ),
                    12
                ),
            },
        };
        importPanel = new Expander
        {
            Header = Ui("Import"),
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = "frpc-import-panel",
            Content = importBody,
        };
        var identity = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        identity.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        identity.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        identity.Children.Add(frpcConfigName);
        Grid.SetColumn(frpcFormat, 1);
        identity.Children.Add(frpcFormat);
        bool? identityNarrow = null;
        identity.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 400;
            if (identityNarrow == narrow)
                return;
            identityNarrow = narrow;
            identity.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(140);
            Grid.SetColumnSpan(frpcConfigName, narrow ? 2 : 1);
            Grid.SetColumn(frpcFormat, narrow ? 0 : 1);
            Grid.SetColumnSpan(frpcFormat, narrow ? 2 : 1);
            Grid.SetRow(frpcFormat, narrow ? 1 : 0);
        };
        var newActions = new FlowPanel();
        var newEditor = new TextBox
        {
            Header = Ui("New configuration content"),
            Text = frpcNewText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            MinHeight = 180,
            MaxHeight = 360,
            MaxLength = 2 * 1024 * 1024,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Tag = "frpc-new-editor",
        };
        newEditor.TextChanged += (_, _) => frpcNewText = newEditor.Text;
        var newBody = new StackPanel
        {
            Spacing = 12,
            MinWidth = 0,
            Visibility = Visibility.Collapsed,
            Children = { identity, newEditor, newActions },
        };
        var newPanel = new Expander
        {
            Header = Ui("New configuration"),
            Content = newBody,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = "frpc-new-panel",
        };
        newPanel.RegisterPropertyChangedCallback(
            Expander.IsExpandedProperty,
            (_, _) =>
            {
                if (!newPanel.IsExpanded)
                {
                    // 先移走焦点和下拉弹层，再隐藏内容，避免收起后残留输入框标题。
                    frpcFormat.IsDropDownOpen = false;
                    selected.Focus(FocusState.Programmatic);
                    if (Content.XamlRoot != null)
                    {
                        foreach (
                            var popup in VisualTreeHelper
                                .GetOpenPopupsForXamlRoot(Content.XamlRoot)
                                .ToArray()
                        )
                        {
                            if (popup.Child == null)
                                continue;
                            var elements = new[] { popup.Child }
                                .Concat(Descendants(popup.Child))
                                .ToArray();
                            var caption = Ui("Configuration name");
                            if (
                                !elements
                                    .OfType<ToolTip>()
                                    .Any(t => t.Content?.ToString() == caption)
                                && !elements.OfType<TextBlock>().Any(t => t.Text == caption)
                            )
                                continue;
                            foreach (var tip in elements.OfType<ToolTip>())
                                tip.IsOpen = false;
                            popup.IsOpen = false;
                        }
                    }
                }
                newBody.Visibility = newPanel.IsExpanded
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        );
        newActions.Children.Add(
            Action(
                Ui("Create configuration"),
                async (_, _) =>
                {
                    if (frpc.Running)
                        throw new IOException(Ui("Stop frpc before creating configurations."));
                    var created = await frpcConfigs.Create(
                        frpcConfigName.Text.Trim(),
                        frpcFormat.SelectedItem as string ?? "toml",
                        newEditor.Text,
                        default
                    );
                    await Reload(created.Path);
                    frpcFormat.IsDropDownOpen = false;
                    selected.Focus(FocusState.Programmatic);
                    newPanel.IsExpanded = false;
                    frpcConfigName.Text = "";
                    newEditor.Text = "";
                    ShowToast(Ui("Create frpc configuration succeeded"));
                },
                true
            )
        );
        newActions.Children.Add(Action(Ui("Clear content"), (_, _) => newEditor.Text = ""));
        newActions.Children.Add(
            Action(
                Ui("Copy current content"),
                (_, _) =>
                {
                    newEditor.Text = frpcEditor.Text;
                    if (frpcEditorPath.Length > 0)
                        frpcFormat.SelectedItem = Path.GetExtension(frpcEditorPath).TrimStart('.');
                }
            )
        );
        // 原生 Expander 的内容测量可能被长路径撑大，显式保留两侧内边距。
        void FitBody(Expander panel, FrameworkElement body)
        {
            var width = Math.Max(0, panel.ActualWidth - 48);
            if (double.IsNaN(body.Width) || Math.Abs(body.Width - width) > .5)
                body.Width = width;
        }
        importPanel.SizeChanged += (_, _) => FitBody(importPanel, importBody);
        importPanel.Expanding += (_, _) => FitBody(importPanel, importBody);
        newPanel.SizeChanged += (_, _) => FitBody(newPanel, newBody);
        newPanel.Expanding += (_, _) => FitBody(newPanel, newBody);
        var actions = new FlowPanel();
        actions.Children.Add(
            Action(
                Ui("Save"),
                async (_, _) =>
                {
                    await SaveFrpcEditor();
                    await Reload(frpcEditorPath);
                    ShowToast(Ui("Save frpc configuration succeeded"));
                },
                true
            )
        );
        actions.Children.Add(
            Action(
                Ui("Verify"),
                async (_, _) =>
                {
                    await SaveFrpcEditor();
                    await Reload(frpcEditorPath);
                    await Run(
                        ct =>
                            frpc.Verify(
                                store.Config.Settings.FrpcVersion,
                                frpcConfigs.Owned(frpcEditorPath),
                                ct
                            ),
                        Ui("Verify frpc configuration")
                    );
                }
            )
        );
        actions.Children.Add(
            Action(
                Ui("Save as"),
                (_, _) =>
                {
                    newEditor.Text = frpcEditor.Text;
                    if (frpcEditorPath.Length > 0)
                        frpcFormat.SelectedItem = Path.GetExtension(frpcEditorPath).TrimStart('.');
                    newPanel.IsExpanded = true;
                    frpcConfigName.Focus(FocusState.Programmatic);
                }
            )
        );
        actions.Children.Add(
            Action(
                Ui("Delete"),
                async (sender, _) =>
                {
                    if (frpc.Running)
                        throw new IOException(Ui("Stop frpc before deleting configurations."));
                    if (frpcEditorPath.Length == 0)
                        return;
                    var path = frpcConfigs.Owned(frpcEditorPath);
                    if (
                        !await ConfirmPop(
                            (FrameworkElement)sender,
                            Ui("Delete configuration?"),
                            Path.GetRelativePath(frpcConfigs.Root, path)
                        )
                    )
                        return;
                    RecyclePath(path);
                    frpcDrafts.Remove(path);
                    store.Config.Settings.FrpcConfigPath = "";
                    await store.Save();
                    await Reload("");
                    ShowToast(Ui("Delete frpc configuration succeeded"));
                }
            )
        );
        actions.Children.Add(
            Action(
                Ui("Open folder [context 293]"),
                (_, _) =>
                {
                    Directory.CreateDirectory(frpcConfigs.Root);
                    OpenFolder(frpcConfigs.Root);
                }
            )
        );
        foreach (var button in actions.Children.OfType<Button>())
        {
            button.MinWidth = 64;
            button.Padding = new Thickness(10, 6, 10, 6);
        }
        editing.Children.Add(frpcEditor);
        editing.Children.Add(actions);
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        Text(Ui("Configurations"), 16, true),
                        selected,
                        info,
                        editing,
                        newPanel,
                        importPanel,
                    },
                }
            )
        );
        frpcConfigurationLoad = Reload();
    }
}
