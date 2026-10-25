using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    bool readingPackDrop;
    Func<ContentDialog, Task<ContentDialogResult>> showModpackImportDialog = async dialog => await dialog.ShowAsync();
    Func<ContentDialog, Task<ContentDialogResult>> showModpackNameDialog = async dialog => await dialog.ShowAsync();
    Func<string, string, CancellationToken, Task<List<string>>> fetchDownloadLoaderBuilds = null!;

    string? InstallationNameError(string name)
    {
        try { store.EnsureUniqueInstanceName(new Instance { Name = name.Trim() }); return null; }
        catch (Exception)
        {
            if (string.IsNullOrWhiteSpace(name)) return Ui("请填写版本名称。", "Enter a version name.");
            if (store.Config.Instances.Any(i => string.Equals(i.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) || Directory.Exists(Path.Combine(store.MinecraftRoot, "versions", name.Trim())))
                return Ui("已存在同名版本，请更换名称。", "A version with this name already exists. Choose another name.");
            return Ui("版本名称包含无效字符，请更换名称。", "The version name contains invalid characters. Choose another name.");
        }
    }

    // 安装可能在后台执行，需要回到 UI 线程询问名称，并把完成状态传回安装任务。
    Task ResolveModpackName(Instance instance, CancellationToken ct)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ResolveModpackNameOnUi(instance, ct); completion.TrySetResult(); }
            catch (OperationCanceledException) { completion.TrySetCanceled(ct); }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new InvalidOperationException("The launcher window is closing."));
        return completion.Task;
    }

    async Task ResolveModpackNameOnUi(Instance instance, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (InstallationNameError(instance.Name) == null) return;
        var input = new TextBox { Header = Ui("版本名称", "Version name"), Text = instance.Name, MaxLength = 80 };
        var error = new TextBlock { Foreground = Brush(lightTheme ? "#C42B1C" : "#FF99A4"), TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 8, Children = { input, error } };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
            Title = Ui("请为整合包输入新的版本名称", "Choose a new name for this modpack"), Content = content,
            PrimaryButtonText = Ui("继续安装", "Continue installation"), CloseButtonText = Ui("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        void Validate()
        {
            var message = InstallationNameError(input.Text);
            error.Text = message ?? "";
            error.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = message == null;
        }
        input.TextChanged += (_, _) => Validate(); Validate();
        dialog.PrimaryButtonClick += (_, args) => { Validate(); args.Cancel = !dialog.IsPrimaryButtonEnabled; };
        dialogOpen = true;
        using var cancellation = ct.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            if (await showModpackNameDialog(dialog) != ContentDialogResult.Primary) throw new OperationCanceledException(ct);
            ct.ThrowIfCancellationRequested();
            instance.Name = input.Text.Trim();
            store.EnsureUniqueInstanceName(instance);
        }
        finally { dialogOpen = false; }
    }

    static bool IsModpackFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mrpack" or ".zip";

    void Modpack_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.None;
        if (task != null || dialogOpen || readingPackDrop || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.IsGlyphVisible = true;
        e.DragUIOverride.Caption = Ui("松开以选择要安装的整合包", "Drop to choose modpacks to install");
        e.DragUIOverride.IsCaptionVisible = true;
    }

    // 读取拖放数据期间持有 deferral，读完先结束系统拖放，再排队弹出确认界面。
    async void Modpack_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (task != null || dialogOpen || readingPackDrop || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        readingPackDrop = true;
        var deferral = e.GetDeferral();
        string[] paths = [];
        Exception? failure = null;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && items.All(item => item is StorageFile && IsModpackFile(item.Path)))
                paths = items.Select(item => item.Path).ToArray();
            else if (items.Count > 0)
                throw new InvalidDataException(Ui("请拖入 .mrpack 或 .zip 整合包文件。", "Drop .mrpack or .zip modpack files."));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failure = ex; }
        finally { deferral.Complete(); }
        // Leave the shell's OLE drop operation before showing any modal UI or starting installation.
        if (!DispatcherQueue.TryEnqueue(() => CompleteModpackDrop(paths, failure))) readingPackDrop = false;
    }

    async void CompleteModpackDrop(string[] paths, Exception? failure)
    {
        try
        {
            if (failure != null) { await Error(failure); return; }
            if (paths.Length > 0 && task == null && !dialogOpen && await ConfirmModpackDrop(paths)) await ImportModpackFiles(paths);
        }
        catch (OperationCanceledException) { Report("操作已取消"); }
        catch (Exception ex) { await Error(ex); }
        finally { readingPackDrop = false; }
    }

    async Task<bool> ConfirmModpackDrop(string[] paths)
    {
        if (dialogOpen) return false;
        dialogOpen = true;
        try
        {
            var names = paths.Take(8).Select(Path.GetFileName).ToList();
            if (paths.Length > names.Count) names.Add(Ui($"以及另外 {paths.Length - names.Count} 个文件", $"and {paths.Length - names.Count} more"));
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = Ui("将为每个整合包创建独立游戏实例：", "A separate game instance will be created for each pack:"), TextWrapping = TextWrapping.Wrap });
            foreach (var name in names) content.Children.Add(new TextBlock { Text = "• " + name, TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = Ui("安装这些整合包？", "Install these modpacks?"),
                Content = content,
                PrimaryButtonText = Ui("安装", "Install"),
                CloseButtonText = Ui("取消", "Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            return await showModpackImportDialog(dialog) == ContentDialogResult.Primary;
        }
        finally { dialogOpen = false; }
    }

    // 逐包安装并保存，某个包失败时，前面已经完成的实例仍保留。
    async Task ImportModpackFiles(string[] paths)
    {
        await Run(async ct =>
            {
                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var instance = await Task.Run(() => modrinth.InstallPack(path, ct, ResolveModpackName), ct);
                    store.Config.Instances.Add(instance);
                    selected = instance;
                    store.Config.SelectedInstanceId = instance.Id;
                    await store.Save();
                }
        }, Ui("安装整合包", "Install modpack"));
        page = "instances"; Render();
    }
}
