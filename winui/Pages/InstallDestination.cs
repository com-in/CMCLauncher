using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    sealed record InstallDestination(Instance Instance, string? World);

    Func<ContentDialog, Task<ContentDialogResult>> showInstallDialog = async dialog =>
        await dialog.ShowAsync();

    // 列表提示与安装前校验共用这套规则，避免界面能选、实际却不兼容。
    string? InstallationReason(
        Instance? instance,
        string type,
        CommunityVersion version,
        bool? shaderSupport = null
    )
    {
        if (instance == null)
            return Ui("No game version selected on the home page.");
        if (!version.GameVersions.Contains(instance.GameVersion))
            return UiFormat("The selected file does not support Minecraft {0}.", instance.GameVersion);
        if (type == "mod")
        {
            var reason = ContentManager.ModInstallUnsupportedReason(instance);
            if (reason != null)
                return reason;
            if (!version.Loaders.Contains(instance.Loader, StringComparer.OrdinalIgnoreCase))
                return UiFormat("The selected mod does not support {0}.", LoaderCategory(instance.Loader));
        }
        if (
            type == "shader"
            && !(
                shaderSupport
                ?? ContentManager.HasShaderSupport(instance, store.GameDirectory(instance))
            )
        )
            return Ui("The current instance does not have Iris, Oculus, or OptiFine enabled, so shaders cannot be used.");
        if (type == "datapack" && ContentManager.Worlds(store.GameDirectory(instance)).Count == 0)
            return Ui("The current instance has no save with installable datapacks.");
        return null;
    }

    // 当前实例兼容时直接使用；否则列出可用实例，取消选择则返回 null。
    async Task<InstallDestination?> ResolveInstallDestination(string type, CommunityVersion version)
    {
        var target = selected;
        var reason = InstallationReason(target, type, version);
        if (reason != null)
        {
            var compatible = store
                .Config.Instances.Where(i => InstallationReason(i, type, version) == null)
                .ToList();
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(Text(reason, 13));
            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 280,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var instance in compatible)
            {
                var identity = new StackPanel { Spacing = 3 };
                identity.Children.Add(Text(instance.Name, 14, true));
                identity.Children.Add(
                    Text(
                        $"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}",
                        12
                    )
                );
                list.Items.Add(
                    new ListViewItem
                    {
                        Content = identity,
                        Tag = instance,
                        Padding = new Thickness(8),
                    }
                );
            }
            if (compatible.Count > 0)
            {
                content.Children.Add(Text(Ui("Which game version to install?"), 14, true));
                content.Children.Add(list);
                list.SelectedIndex = 0;
            }
            else
                content.Children.Add(
                    Text(Ui("No compatible install location. Please download or configure a game version that supports this resource."), 13)
                );
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = Ui("Choose install location"),
                Content = content,
                PrimaryButtonText =
                    compatible.Count == 0
                        ? Ui("Go to game downloads")
                        : Ui("Install to this version"),
                CloseButtonText = Ui("Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await showInstallDialog(dialog) != ContentDialogResult.Primary)
                return null;
            if (compatible.Count == 0)
            {
                page = "downloads";
                expandedNavigation = "downloads";
                await RenderAsync();
                return null;
            }
            target = (list.SelectedItem as ListViewItem)?.Tag as Instance;
            if (target == null)
                return null;
        }
        if (target == null)
            return null;
        // 数据包属于具体存档，不能只确定实例就直接写入。
        string? worldName = null;
        if (type == "datapack")
        {
            var worlds = ContentManager.Worlds(store.GameDirectory(target));
            var list = new ListView
            {
                ItemsSource = worlds,
                SelectedIndex = 0,
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 280,
            };
            var panel = new StackPanel
            {
                Spacing = 10,
                Children = { Text(Ui("Install to: ") + target.Name, 13), list },
            };
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = Ui("Choose a world for the data pack"),
                Content = panel,
                PrimaryButtonText = Ui("Install"),
                CloseButtonText = Ui("Cancel"),
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = worlds.Count > 0,
            };
            if (await showInstallDialog(dialog) != ContentDialogResult.Primary)
                return null;
            worldName = list.SelectedItem as string;
            if (worldName == null)
                return null;
        }
        return new(target, worldName);
    }
}