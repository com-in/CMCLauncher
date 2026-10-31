using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Action<string> recycleInstanceDirectory = path =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
        );

    Func<ContentDialog, Task<ContentDialogResult>> showVersionDeletionDialog = async dialog =>
        await dialog.ShowAsync();

    // 列表和平铺共用筛选结果，收藏优先，再按加载器分组展示。
    void Instances()
    {
        Header("切换版本", "");
        PageBody.Children.Add(
            Buttons(
                Action(
                    "下载游戏",
                    (_, _) =>
                    {
                        page = "downloads";
                        Render();
                    }
                ),
                Action("导入整合包", async (_, _) => await ImportPack())
            )
        );
        var folder = new StackPanel { Spacing = 7 };
        folder.Children.Add(Text(".minecraft 文件夹", 13, true));
        folder.Children.Add(Text(store.MinecraftRoot, 12));
        folder.Children.Add(
            Text(
                "Import PCL/HMCL folders, .minecraft, versions, or a single version folder. Existing files stay in place.",
                12
            )
        );
        folder.Children.Add(
            Buttons(
                Action("设置文件夹", async (_, _) => await ImportLocal()),
                Action(
                    "扫描版本",
                    async (_, _) =>
                    {
                        await SetMinecraftFolder(store.MinecraftRoot);
                        Render();
                    }
                ),
                Action("打开文件夹", (_, _) => OpenFolder(store.MinecraftRoot))
            )
        );
        folder.Children.Add(
            Text(
                "Migrate instances from the Modrinth app or MultiMC/Prism launchers into the library.",
                12
            )
        );
        folder.Children.Add(
            Buttons(
                Action(
                    "迁移其它启动器",
                    async (_, _) =>
                    {
                        await MigrateForeignLauncher();
                    }
                )
            )
        );
        PageBody.Children.Add(CompactCard(folder));
        var query = new TextBox
        {
            PlaceholderText = Ui("Search version name, description, or Minecraft version"),
            Text = instanceQuery,
        };
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                instanceQuery = query.Text.Trim();
                Render();
            }
        };
        var filterIds = new[]
        {
            "全部实例",
            "收藏",
            "原版",
            "Fabric",
            "Forge",
            "NeoForge",
            "Quilt",
        };
        var filters = new[]
        {
            Ui("All versions"),
            Ui("Favorites"),
            Ui("Vanilla"),
            "Fabric",
            "Forge",
            "NeoForge",
            "Quilt",
        };
        var listMode = Ui("List");
        var tileModeName = Ui("Grid");
        instanceFilter = instanceFilter switch
        {
            "All versions" => "全部实例",
            "Favorites" => "收藏",
            "Vanilla" => "原版",
            _ => instanceFilter,
        };
        var filter = new BelowComboBox
        {
            ItemsSource = filters,
            SelectedIndex = Array.IndexOf(filterIds, instanceFilter),
            Width = 140,
        };
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedIndex >= 0 && filterIds[filter.SelectedIndex] != instanceFilter)
            {
                instanceFilter = filterIds[filter.SelectedIndex];
                Render();
            }
        };
        var layout = new BelowComboBox
        {
            ItemsSource = new[] { listMode, tileModeName },
            SelectedIndex = store.Config.Settings.InstanceView == "tile" ? 1 : 0,
            Width = 120,
        };
        layout.SelectionChanged += async (_, _) =>
        {
            if (layout.SelectedIndex < 0)
                return;
            store.Config.Settings.InstanceView = layout.SelectedIndex == 1 ? "tile" : "list";
            await store.Save();
            Render();
        };
        var search = new Grid { ColumnSpacing = 8 };
        search.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.Children.Add(query);
        var find = Action(
            "搜索",
            (_, _) =>
            {
                instanceQuery = query.Text.Trim();
                Render();
            }
        );
        Grid.SetColumn(find, 1);
        search.Children.Add(find);
        Grid.SetColumn(filter, 2);
        search.Children.Add(filter);
        Grid.SetColumn(layout, 3);
        search.Children.Add(layout);
        PageBody.Children.Add(search);
        var allFilter = instanceFilter is "全部实例" or "All versions";
        var favoriteFilter = instanceFilter is "收藏" or "Favorites";
        var loaderFilter = Ui(instanceFilter);
        var instances = store
            .Config.Instances.Where(i =>
                (
                    allFilter
                    || (favoriteFilter ? i.Favorite : LoaderCategory(i.Loader) == loaderFilter)
                )
                && (
                    instanceQuery.Length == 0
                    || (i.Name + " " + i.Description + " " + i.GameVersion).Contains(
                        instanceQuery,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            .OrderByDescending(i => i.Favorite)
            .ToList();
        var tileMode = store.Config.Settings.InstanceView == "tile";
        Grid? tileGrid = null;
        var tileIndex = 0;
        if (instances.Count == 0)
            PageBody.Children.Add(
                Text("没有符合条件的版本。可以下载游戏，或设置已有的 .minecraft 文件夹。", 13)
            );
        foreach (var group in instances.GroupBy(i => LoaderCategory(i.Loader)))
        {
            if (tileMode)
            {
                tileGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
                tileGrid.ColumnDefinitions.Add(new ColumnDefinition());
                tileGrid.ColumnDefinitions.Add(new ColumnDefinition());
                tileIndex = 0;
            }
            PageBody.Children.Add(Text($"{group.Key}  ·  {group.Count()}", 15, true));
            foreach (var instance in group)
            {
                var row = new Grid { ColumnSpacing = 12, MinHeight = 50 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                );
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(InstanceIcon(instance));
                var identity = new StackPanel
                {
                    Spacing = 3,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                identity.Children.Add(Text(instance.Name, 14, true));
                identity.Children.Add(
                    Text(
                        instance.Description.Length > 0
                            ? instance.Description
                            : $"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}",
                        12
                    )
                );
                if (instance.Description.Length > 0)
                {
                    var detail = Text(
                        $"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}",
                        11
                    );
                    detail.Opacity = .65;
                    identity.Children.Add(detail);
                }
                Grid.SetColumn(identity, 1);
                row.Children.Add(identity);
                var favorite = new Button
                {
                    Content = new FontIcon
                    {
                        Glyph = instance.Favorite ? "\uE735" : "\uE734",
                        FontSize = 15,
                    },
                    Width = 30,
                    Height = 28,
                    Padding = new Thickness(0),
                    MinWidth = 30,
                };
                favorite.Click += async (_, _) =>
                {
                    instance.Favorite = !instance.Favorite;
                    await store.Save();
                    Render();
                };
                ToolTipService.SetToolTip(
                    favorite,
                    Ui(instance.Favorite ? "取消收藏" : "收藏版本")
                );
                var remove = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                    Width = 30,
                    Height = 28,
                    Padding = new Thickness(0),
                    MinWidth = 30,
                    IsEnabled = task == null,
                };
                VersionDeleteButton(remove);
                ToolTipService.SetToolTip(remove, Ui("Delete version"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                    remove,
                    Ui("Delete version")
                );
                remove.Click += async (_, _) =>
                {
                    try
                    {
                        await ConfirmDeleteInstance(instance, remove);
                    }
                    catch (Exception ex)
                    {
                        await Error(ex);
                    }
                };
                remove.AddHandler(
                    UIElement.TappedEvent,
                    new Microsoft.UI.Xaml.Input.TappedEventHandler((_, e) => e.Handled = true),
                    true
                );
                var actions = Buttons(
                    favorite,
                    Action(
                        instance == selected ? "当前版本" : "选择",
                        async (_, _) =>
                        {
                            await SelectInstance(instance);
                            page = "home";
                            Render();
                        },
                        instance == selected
                    ),
                    Action(
                        "设置",
                        async (_, _) =>
                        {
                            await SelectInstance(instance);
                            page = "version-settings";
                            kind = "overview";
                            Render();
                        }
                    ),
                    remove
                );
                foreach (
                    var button in actions
                        .Children.OfType<Button>()
                        .Where(b => b.Content is not FontIcon)
                )
                {
                    button.MinWidth = 56;
                    button.MinHeight = 28;
                    button.Padding = new Thickness(8, 4, 8, 4);
                }
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
                AdaptCard(row, identity, actions);
                var card = CompactCard(row);
                card.Tag = "instance-card";
                ToolTipService.SetToolTip(card, store.GameDirectory(instance));
                card.Tapped += async (_, _) =>
                {
                    if (page != "instances")
                        return;
                    await SelectInstance(instance);
                    page = "home";
                    Render();
                };
                if (tileMode && tileGrid != null)
                {
                    card.HorizontalAlignment = HorizontalAlignment.Stretch;
                    var rowIndex = tileIndex / 2;
                    while (tileGrid.RowDefinitions.Count <= rowIndex)
                        tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(card, rowIndex);
                    Grid.SetColumn(card, tileIndex % 2);
                    tileGrid.Children.Add(card);
                    tileIndex++;
                }
                else
                    PageBody.Children.Add(card);
            }
            if (tileMode && tileGrid != null)
                PageBody.Children.Add(tileGrid);
        }
    }

    string? DeletableInstanceDirectory(Instance instance) =>
        store.DeletableInstanceDirectory(instance);

    async Task ConfirmDeleteInstance(Instance instance, FrameworkElement anchor)
    {
        if (task != null)
            throw new InvalidOperationException(
                Ui(
                    "context.instancelibrary.wait_for_the_current_task_to_finish_or_cancel_it_before_deleting"
                )
            );
        var inspection = store.InspectVersionDeletion(instance);
        var directory = inspection.DeletableDirectory;
        var deleteFiles = new CheckBox
        {
            Content = Ui("Also delete version files (move to Recycle Bin)"),
            IsChecked = directory != null && Directory.Exists(directory),
            IsEnabled = directory != null && Directory.Exists(directory),
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text(UiFormat("Remove “{0}” from the launcher.", instance.Name), 14));
        content.Children.Add(deleteFiles);
        if (directory == null)
        {
            var related = string.Join(", ", inspection.RelatedVersions);
            var reason = inspection.Reason switch
            {
                VersionDeletionBlockReason.Dependency => UiFormat(
                    "Version files are kept because these versions reference this profile or client JAR: {0}. Remove or migrate those versions first.",
                    related
                ),
                VersionDeletionBlockReason.SharedDirectory => UiFormat(
                    "This version directory overlaps with: {0}. Only the launcher entry will be removed.",
                    related
                ),
                VersionDeletionBlockReason.UnreadableProfile => UiFormat(
                    "Cannot verify version references because a profile could not be read: {0}. Version files will be kept.",
                    related
                ),
                VersionDeletionBlockReason.UnsafePath => Ui(
                    "The version path could not be verified safely. Version files will be kept."
                ),
                _ => Ui(
                    "This path is not an individual version folder. Shared Minecraft files will be kept."
                ),
            };
            content.Children.Add(Text(reason, 12));
            if (inspection.Directory != null)
                content.Children.Add(Text(inspection.Directory, 12));
        }
        else
        {
            var path = Text(directory, 12);
            path.Opacity = .7;
            content.Children.Add(path);
        }
        if (dialogOpen)
            return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("Delete version?"),
            Content = new ScrollViewer
            {
                Content = content,
                MaxHeight = Math.Clamp(((FrameworkElement)Content).ActualHeight - 240, 120, 480),
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            PrimaryButtonText = Ui("Delete version"),
            CloseButtonText = Ui("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.PrimaryButtonStyle = new Style(typeof(Button))
        {
            BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"],
            Setters =
            {
                new Setter(Control.ForegroundProperty, versionDeleteText),
                new Setter(Control.BackgroundProperty, versionDeleteBackground),
                new Setter(Control.BorderBrushProperty, versionDeleteText),
                new Setter(Control.CornerRadiusProperty, new CornerRadius(5)),
            },
        };
        dialog.Opened += (_, _) =>
        {
            var primary = Descendants(dialog)
                .OfType<Button>()
                .FirstOrDefault(button => button.Name == "PrimaryButton");
            if (primary != null)
                VersionDeleteButton(primary);
        };
        if (store.Config.Settings.ColorPreset != "native")
        {
            dialog.Background = ComboSurfaceBrush;
            dialog.Foreground = ComboTextBrush;
            dialog.Resources["ContentDialogBackground"] = ComboSurfaceBrush;
            dialog.Resources["ContentDialogForeground"] = ComboTextBrush;
        }
        dialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await showVersionDeletionDialog(dialog);
        }
        finally
        {
            dialogOpen = false;
        }
        if (result != ContentDialogResult.Primary)
            return;
        if (task != null)
            throw new InvalidOperationException(
                Ui(
                    "context.instancelibrary.wait_for_the_current_task_to_finish_or_cancel_it_before_deleting"
                )
            );
        if (
            deleteFiles.IsChecked == true
            && !string.Equals(
                directory,
                DeletableInstanceDirectory(instance),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new InvalidOperationException(
                Ui(
                    "context.instancelibrary.the_version_directory_has_changed_please_confirm_deletion_again"
                )
            );
        await DeleteInstance(instance, deleteFiles.IsChecked == true ? directory : null);
    }

    // 先保存配置再移入回收站；出错时恢复列表位置和选中项。
    async Task DeleteInstance(Instance instance, string? directory)
    {
        var index = store.Config.Instances.IndexOf(instance);
        if (index < 0)
            return;
        var previousSelected = selected;
        var previousSelectedId = store.Config.SelectedInstanceId;
        store.Config.Instances.RemoveAt(index);
        if (selected?.Id == instance.Id)
        {
            selected = store.Config.Instances.FirstOrDefault();
            world = null;
        }
        store.Config.SelectedInstanceId = selected?.Id;
        try
        {
            await store.Save();
            if (directory != null && Directory.Exists(directory))
                recycleInstanceDirectory(directory);
        }
        catch
        {
            store.Config.Instances.Insert(Math.Min(index, store.Config.Instances.Count), instance);
            selected = previousSelected;
            store.Config.SelectedInstanceId = previousSelectedId;
            await store.Save();
            throw;
        }
        Report(Ui("Version deleted") + "：" + instance.Name);
        ShowToast(Ui("Version deleted") + " · " + instance.Name);
        if (page == "version-settings")
            page = selected == null ? "home" : "instances";
        Render();
    }

    UIElement InstanceIcon(Instance instance)
    {
        var candidates = new[]
        {
            instance.IconPath,
            Path.Combine(store.GameDirectory(instance), "icon.png"),
            Path.Combine(store.GameDirectory(instance), "PCL", "Logo.png"),
        };
        foreach (var file in candidates)
            if (file != null && File.Exists(file))
                return new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(file))),
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                };
        if (instance.Loader == "vanilla")
        {
            var grass = Path.Combine(AppContext.BaseDirectory, "assets", "grass-block.png");
            if (File.Exists(grass))
                return new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(grass))),
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.Uniform,
                };
        }
        var logo = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "loaders",
            instance.Loader + ".png"
        );
        if (File.Exists(logo))
            return new Image
            {
                Source = new BitmapImage(new Uri(Path.GetFullPath(logo))),
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform,
            };
        var (color, wordmark) = instance.Loader switch
        {
            "fabric" => ("#A4927C", "F"),
            "forge" => ("#547DA5", "FORGE"),
            "neoforge" => ("#C56B3D", "NEO"),
            "quilt" => ("#916BB7", "Q"),
            _ => ("#657080", "MC"),
        };
        return new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(RawColor(color)),
            Child = new TextBlock
            {
                Text = wordmark,
                FontSize = wordmark.Length > 2 ? 8 : 21,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
    }

    async Task<string?> PickMinecraftFolder()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    // 扫描只登记已有版本，不移动原目录；单个损坏版本不会中断其他版本导入。
    async Task SetMinecraftFolder(string path)
    {
        await Run(async ct =>
        {
            await core.RefreshImportedMetadata(ct);
            var scan = await core.ScanLocalInstances(path, ct, AddLog);
            var root = scan.Root;
            var pending = scan.Instances;
            var failed = scan.Failed;
            var previousRoot = store.Config.Settings.MinecraftRoot;
            store.Config.Settings.MinecraftRoot = root;
            store.Config.Instances.AddRange(pending);
            try
            {
                await store.Save();
            }
            catch
            {
                store.Config.Settings.MinecraftRoot = previousRoot;
                foreach (var instance in pending)
                    store.Config.Instances.Remove(instance);
                throw;
            }
            Report(
                $"已设置 .minecraft 文件夹，发现 {pending.Count} 个新版本"
                    + (failed > 0 ? $"，{failed} 个版本读取失败，详情见日志" : "")
            );
        });
    }

    // 名称修改只影响展示；新图标复制到配置目录，避免依赖用户原图片的位置。
    Func<ContentDialog, Task<ContentDialogResult>> showInstanceEditDialog = async dialog =>
        await dialog.ShowAsync();

    async Task EditInstance(string field)
    {
        if (selected is not { } instance || dialogOpen)
            return;
        var title = field switch
        {
            "name" => "Edit name",
            "description" => "Edit description",
            "icon" => "Edit icon",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var name = new TextBox
        {
            Header = Ui("Version name"),
            Text = instance.Name,
            MaxLength = 80,
        };
        var description = new TextBox
        {
            Header = Ui("Description"),
            Text = instance.Description,
            MaxLength = 240,
            AcceptsReturn = true,
        };
        var iconPath = instance.IconPath;
        var iconLabel = Text(iconPath == null ? "使用默认图标" : Path.GetFileName(iconPath), 12);
        var chooseIcon = new Button { Content = Ui("Choose image") };
        chooseIcon.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".ico" })
                picker.FileTypeFilter.Add(ext);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                iconPath = file.Path;
                iconLabel.Text = file.Name;
            }
        };
        var reset = new Button { Content = Ui("Restore default icon") };
        reset.Click += (_, _) =>
        {
            iconPath = null;
            iconLabel.Text = Ui("Use default icon");
        };
        var panel = new StackPanel { Spacing = 10 };
        if (field == "name")
            panel.Children.Add(name);
        else if (field == "description")
            panel.Children.Add(description);
        else
        {
            panel.Children.Add(iconLabel);
            panel.Children.Add(Buttons(chooseIcon, reset));
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui(title),
            Content = panel,
            PrimaryButtonText = Ui("Save"),
            CloseButtonText = Ui("Cancel"),
        };
        if (field == "name")
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);
            name.TextChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text);
        }
        dialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await showInstanceEditDialog(dialog);
        }
        finally
        {
            dialogOpen = false;
        }
        if (result != ContentDialogResult.Primary)
            return;
        if (field == "name" && string.IsNullOrWhiteSpace(name.Text))
            throw new Exception("版本名称不能为空");
        if (field == "icon" && iconPath != null && iconPath != instance.IconPath)
        {
            if (new FileInfo(iconPath).Length > 8 * 1024 * 1024)
                throw new Exception("图标请使用 8 MB 以内的图片");
            var destination = Store.SafePath(
                store.Root,
                "instance-icons/"
                    + instance.Id
                    + "-"
                    + Guid.NewGuid().ToString("N")
                    + Path.GetExtension(iconPath)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(iconPath, destination);
            iconPath = destination;
        }
        if (field == "name")
            instance.Name = name.Text.Trim();
        else if (field == "description")
            instance.Description = description.Text.Trim();
        else
            instance.IconPath = iconPath;
        await store.Save();
        Render();
    }
}