using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Task schematicLoad = Task.CompletedTask;
    Task schematicAvailabilityLoad = Task.CompletedTask;
    string schematicQuery = "";
    readonly HashSet<string> expandedSchematicFolders = new(StringComparer.OrdinalIgnoreCase);

    void Schematics(Instance instance, string directory)
    {
        var search = new TextBox { PlaceholderText = Ui("Search schematic files"), Text = schematicQuery, Tag = "schematic-search" };
        var rows = new StackPanel { Spacing = 8 };
        var status = Text(Ui("Loading schematics…"), 12);
        PageBody.Children.Add(Buttons(
            Action(Ui("Import schematics"), async (_, _) =>
            {
                var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                foreach (var extension in SchematicManager.Extensions) picker.FileTypeFilter.Add(extension);
                var files = await picker.PickMultipleFilesAsync(); if (files.Count == 0) return;
                await Run(ct => SchematicManager.Import(directory, files.Select(f => f.Path).ToArray(), ct), Ui("Import schematics"));
                Render();
            }, true),
            Action(Ui("Open schematics folder"), (_, _) => OpenFolder(directory)),
            Action("刷新", (_, _) => Render())));
        PageBody.Children.Add(search);
        PageBody.Children.Add(Text(Ui("Supports .litematic, .schematic, .schem, and Create .nbt blueprints without converting formats."), 12));
        PageBody.Children.Add(status); PageBody.Children.Add(rows);
        var generation = renderVersion;
        var queryVersion = 0;
        async Task Load()
        {
            var revision = ++queryVersion;
            try
            {
                var result = await Task.Run(() => { var directories = new List<string>(); return (Files: SchematicManager.List(directory, directories), Directories: directories); });
                var files = result.Files;
                RuntimeLog.Debug($"Schematics instance={instance.Id}; files={files.Count}; folders={result.Directories.Count}; queryLength={schematicQuery.Length}; generation={generation}");
                if (generation != renderVersion || revision != queryVersion) return;
                rows.Children.Clear();
                var filtered = files.Where(f => f.RelativePath.Contains(schematicQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
                status.Text = UiFormat("{0} schematics · {1} shown", files.Count, filtered.Length);
                if (filtered.Length == 0) rows.Children.Add(Text(Ui("No matching schematic files."), 13));
                var folders = new Dictionary<string, StackPanel>(StringComparer.OrdinalIgnoreCase) { [""] = rows };
                StackPanel Folder(string path)
                {
                    if (folders.TryGetValue(path, out var found)) return found;
                    var parent = Folder(Path.GetDirectoryName(path) ?? "");
                    var content = new StackPanel { Spacing = 6, Margin = new Thickness(14, 0, 0, 0) };
                    var key = instance.Id + "/" + path;
                    var folder = new Expander { Header = "📁 " + Path.GetFileName(path), Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = schematicQuery.Length > 0 || expandedSchematicFolders.Contains(key), Tag = "schematic-folder" };
                    folder.Expanding += (_, _) => expandedSchematicFolders.Add(key);
                    folder.Collapsed += (_, _) => expandedSchematicFolders.Remove(key);
                    parent.Children.Add(folder); folders[path] = content;
                    return content;
                }
                var visibleFolders = schematicQuery.Length == 0 ? result.Directories : filtered.Select(f => Path.GetDirectoryName(f.RelativePath) ?? "").Where(p => p.Length > 0);
                foreach (var folder in visibleFolders.Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) Folder(folder);
                foreach (var file in filtered)
                {
                    if (generation != renderVersion || revision != queryVersion) return;
                    var name = Text(Path.GetFileName(file.RelativePath), 14, true); name.MaxLines = 1; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
                    ToolTipService.SetToolTip(name, file.RelativePath);
                    var detail = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { name, Text($"{Path.GetExtension(file.RelativePath).ToUpperInvariant()} · {file.Size / 1024d:N1} KB · {file.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}", 12) } };
                    var actions = Buttons(
                        Action(Ui("Export"), async (_, _) =>
                        {
                            var picker = new FileSavePicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                            picker.FileTypeChoices.Add(Ui("Schematic file"), [Path.GetExtension(file.RelativePath)]);
                            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(file.RelativePath);
                            var output = await picker.PickSaveFileAsync(); if (output == null) return;
                            await Run(ct => SchematicManager.Export(directory, file.RelativePath, output.Path, ct), Ui("Export schematic"));
                        }),
                        Action(Ui("Rename"), async (sender, _) =>
                        {
                            var input = new TextBox { Header = Ui("File name (without extension)"), Text = Path.GetFileNameWithoutExtension(file.RelativePath), MaxLength = 160 };
                            if (!await ConfirmPop((FrameworkElement)sender, Ui("Rename schematic"), input, Ui("Save"))) return;
                            await Run(_ => { SchematicManager.Rename(directory, file.RelativePath, input.Text); return Task.CompletedTask; }, Ui("Rename schematic"));
                            Render();
                        }),
                        Action(Ui("Delete"), async (sender, _) =>
                        {
                            if (!await ConfirmPop((FrameworkElement)sender, Ui("Move to Recycle Bin?"), file.RelativePath, Ui("Move to Recycle Bin"))) return;
                            await Run(_ => { RecyclePath(SchematicManager.ExistingFile(directory, file.RelativePath)); return Task.CompletedTask; }, Ui("Delete schematic"));
                            Render();
                        }));
                    actions.VerticalAlignment = VerticalAlignment.Center;
                    var row = new Grid { ColumnSpacing = 12, Tag = "schematic-row" };
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    // 复用卡片自适应布局，给图标列保留空位，窄窗口下操作按钮另起一行。
                    row.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(0) });
                    Grid.SetColumn(detail, 1); row.Children.Add(detail); Grid.SetColumn(actions, 2); row.Children.Add(actions);
                    AdaptCard(row, detail, actions);
                    var card = CompactCard(row);
                    card.Padding = new Thickness(12, 4, 12, 4); card.MinHeight = 48;
                    Folder(Path.GetDirectoryName(file.RelativePath) ?? "").Children.Add(card);
                    await Task.Yield();
                }
            }
            catch (Exception ex) { if (generation == renderVersion && revision == queryVersion) status.Text = Ui("Could not load schematics: ") + ex.Message; }
        }
        search.TextChanged += (_, _) => { schematicQuery = search.Text.Trim(); schematicLoad = Load(); };
        schematicLoad = Load();
    }
}
