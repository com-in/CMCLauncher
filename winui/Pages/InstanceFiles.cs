using System.Diagnostics;
using CMCLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 存档页展示世界管理；常规页复用此入口展示最近的游戏截图。
    void InstanceFiles(Instance instance, bool includeWorlds = true)
    {
        var root = store.GameDirectory(instance);
        if (!includeWorlds)
        {
            var screenshots = Path.Combine(root, "screenshots");
            var shotSection = new StackPanel { Spacing = 7 };
            shotSection.Children.Add(Text("截图查看", 15, true));
            var shots = Directory.Exists(screenshots) ? Directory.EnumerateFiles(screenshots).Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc).Take(40).ToList() : [];
            if (shots.Count == 0) shotSection.Children.Add(Text("此实例暂无游戏截图。", 12));
            else foreach (var file in shots)
            {
                var row = new Grid { ColumnSpacing = 10, MinHeight = 64 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var image = new Image { Width = 96, Height = 56, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill, Source = new BitmapImage(new Uri(Path.GetFullPath(file))) };
                row.Children.Add(image); var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
                info.Children.Add(Text(Path.GetFileName(file), 13, true)); info.Children.Add(Text(File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm"), 11)); Grid.SetColumn(info, 1); row.Children.Add(info);
                var open = Action("查看", (_, _) => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })); Grid.SetColumn(open, 2); row.Children.Add(open);
                shotSection.Children.Add(CompactCard(row));
            }
            PageBody.Children.Add(shotSection);
            return;
        }
        var worlds = ContentManager.Worlds(root);
        var worldSection = new StackPanel { Spacing = 8 };
        worldSection.Children.Add(Text("存档管理", 15, true));
        worldSection.Children.Add(Buttons(Action("导入存档 ZIP", async (_, _) => await ImportWorld(instance)), Action("打开存档文件夹", (_, _) => OpenFolder(Path.Combine(root, "saves")))));
        if (worlds.Count == 0) worldSection.Children.Add(Text("此实例暂无存档。", 12));
        foreach (var name in worlds)
        {
            var folder = Path.Combine(root, "saves", name);
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(Text(name, 13, true));
            var info = new FileInfo(Path.Combine(folder, "level.dat"));
            panel.Children.Add(Text(info.Exists
                ? store.Config.Settings.LauncherLanguage == "en_us" ? $"Modified {info.LastWriteTime:yyyy-MM-dd HH:mm} · {info.Length / 1024:N0} KB" : $"最后修改 {info.LastWriteTime:yyyy-MM-dd HH:mm} · {info.Length / 1024:N0} KB"
                : "存档信息不可用", 11));
            panel.Children.Add(Action("导出 ZIP", async (_, _) => await ExportWorld(name, folder)));
            worldSection.Children.Add(CompactCard(panel));
        }
        PageBody.Children.Add(worldSection);
    }
    async Task ImportWorld(Instance instance)
    {
        var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync(); if (file == null) return;
        var name = Path.GetFileNameWithoutExtension(file.Name).Trim();
        if (name.Length == 0) throw new Exception("无法从文件名识别存档名称。");
        var destination = Store.SafePath(Path.Combine(store.GameDirectory(instance), "saves"), name);
        if (Directory.Exists(destination)) throw new Exception("已存在同名存档：" + name);
        await Run(ct => WorldArchive.Import(file.Path, destination, Path.GetFileNameWithoutExtension(file.Name), ct), "导入存档 · " + name);
        if (page == "version-settings") Render();
    }
    async Task ExportWorld(string name, string folder)
    {
        var picker = new FileSavePicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeChoices.Add("ZIP 存档", [".zip"]); picker.SuggestedFileName = name;
        var file = await picker.PickSaveFileAsync(); if (file == null) return;
        await Run(ct => WorldArchive.Export(folder, file.Path, ct), "导出存档 · " + name);
    }
}
