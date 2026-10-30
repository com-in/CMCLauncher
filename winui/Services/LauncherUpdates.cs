using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    readonly CancellationTokenSource updateLifetime = new();
    bool checkingLauncherUpdate;
    bool installingLauncherUpdate;
    LauncherRelease? launcherRelease;
    TextBlock? launcherUpdateStatus;
    Button? launcherCheckButton;
    Button? launcherInstallButton;
    HyperlinkButton? launcherReleaseLink;
    string launcherUpdateMessage = "";

    internal Border BuildLauncherUpdateSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text("Launcher updates", 18, true));
        panel.Children.Add(
            Text(UiFormat("Current version: {0}", LauncherUpdates.CurrentVersion), 12)
        );
        var automatic = new ToggleSwitch
        {
            Header = Ui("Check for updates on startup"),
            IsOn = store.Config.Settings.CheckLauncherUpdates,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        automatic.Toggled += async (_, _) =>
        {
            try
            {
                store.Config.Settings.CheckLauncherUpdates = automatic.IsOn;
                await store.Save();
                if (automatic.IsOn)
                    await CheckLauncherUpdate(false);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        panel.Children.Add(automatic);
        var beta = new ToggleSwitch
        {
            Header = Ui("Receive Beta releases"),
            IsOn = store.Config.Settings.ReceiveBetaUpdates,
            OnContent = Ui("context.settingspage.on"),
            OffContent = Ui("Off"),
        };
        beta.Toggled += async (_, _) =>
        {
            try
            {
                store.Config.Settings.ReceiveBetaUpdates = beta.IsOn;
                launcherRelease = null;
                launcherUpdateMessage = "";
                RefreshLauncherUpdateControls();
                await store.Save();
                await CheckLauncherUpdate(false);
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        panel.Children.Add(beta);
        panel.Children.Add(
            Text(
                "Beta releases may be less stable. Turning this off does not downgrade the installed version.",
                12
            )
        );
        launcherUpdateStatus = Text("", 13);
        panel.Children.Add(launcherUpdateStatus);
        launcherCheckButton = Action(
            "Check for updates",
            async (_, _) => await CheckLauncherUpdate(false)
        );
        launcherInstallButton = Action(
            "Download and install update",
            async (_, _) => await InstallLauncherUpdate(),
            true
        );
        // Stack vertically so translated labels also fit the minimum window width.
        panel.Children.Add(launcherCheckButton);
        panel.Children.Add(launcherInstallButton);
        launcherReleaseLink = new HyperlinkButton
        {
            Content = Ui("View release notes"),
            NavigateUri = new Uri(LauncherUpdates.ReleasesPage),
        };
        panel.Children.Add(launcherReleaseLink);
        RefreshLauncherUpdateControls();
        return Card(panel);
    }

    void RefreshLauncherUpdateControls()
    {
        if (launcherUpdateStatus == null)
            return;
        launcherUpdateStatus.Text =
            checkingLauncherUpdate ? Ui("Checking for updates…")
            : launcherRelease is { } release ? UiFormat("Update available: {0}", release.Tag)
            : Ui(launcherUpdateMessage);
        if (
            !checkingLauncherUpdate
            && launcherRelease is { } available
            && available.Asset is not { Sha256.Length: 64 }
        )
            launcherUpdateStatus.Text +=
                "\n" + Ui("This release requires a manual download. Open its release notes.");
        if (launcherCheckButton != null)
            launcherCheckButton.IsEnabled = !checkingLauncherUpdate && !installingLauncherUpdate;
        if (launcherInstallButton != null)
        {
            launcherInstallButton.Visibility = launcherRelease?.Asset is { Sha256.Length: 64 }
                ? Visibility.Visible
                : Visibility.Collapsed;
            launcherInstallButton.IsEnabled = !checkingLauncherUpdate && !installingLauncherUpdate;
        }
        if (launcherReleaseLink != null)
            launcherReleaseLink.NavigateUri =
                launcherRelease?.Page ?? new Uri(LauncherUpdates.ReleasesPage);
    }

    async Task CheckLauncherUpdate(bool background)
    {
        // A channel change during a request causes a fresh request before publishing results.
        if (
            checkingLauncherUpdate
            || installingLauncherUpdate
            || updateLifetime.IsCancellationRequested
        )
            return;
        checkingLauncherUpdate = true;
        RefreshLauncherUpdateControls();
        try
        {
            while (true)
            {
                var beta = store.Config.Settings.ReceiveBetaUpdates;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    updateLifetime.Token
                );
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var client = LauncherUpdates.CreateClient(store.Config.Settings);
                var release = await new LauncherUpdates(client).Check(
                    LauncherUpdates.CurrentVersion,
                    beta,
                    LauncherUpdateInstaller.SingleFile,
                    timeout.Token
                );
                if (beta != store.Config.Settings.ReceiveBetaUpdates)
                    continue;
                if (background && !store.Config.Settings.CheckLauncherUpdates)
                    return;
                launcherRelease = release;
                launcherUpdateMessage = "You are up to date.";
                if (background && release != null)
                    ShowToast(
                        UiFormat("Update available: {0}", release.Tag)
                            + " · "
                            + Ui("Launcher updates")
                    );
                break;
            }
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            launcherUpdateMessage =
                "Could not check for updates. Try again or open the release page.";
            AddLog("Launcher update check failed: " + ex.Message);
        }
        finally
        {
            checkingLauncherUpdate = false;
            RefreshLauncherUpdateControls();
        }
    }

    async Task InstallLauncherUpdate()
    {
        if (
            installingLauncherUpdate
            || launcherRelease is not { Asset: { Sha256.Length: 64 } asset } release
        )
            return;
        if (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
            return;
        if (task != null || game is { HasExited: false })
            throw new InvalidOperationException(
                Ui("Finish active tasks and close the game before installing an update.")
            );
        if (dialogOpen)
            return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                Title = UiFormat("Install update {0}?", release.Tag),
                Content = Ui(
                    "The launcher will download and verify the update, then close and restart. Your accounts, instances, and settings will be kept."
                ),
                PrimaryButtonText = Ui("Download and install update"),
                CloseButtonText = Ui("Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
        }
        finally
        {
            dialogOpen = false;
        }
        if (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
            return;
        installingLauncherUpdate = true;
        RefreshLauncherUpdateControls();
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CMCLauncher",
                "updates",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(directory);
            var package = Path.Combine(
                directory,
                LauncherUpdateInstaller.SingleFile ? "update.exe" : "update.zip"
            );
            var stage = Path.Combine(directory, "stage");
            IReadOnlyList<string>? files = null;
            await Run(
                async ct =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        ct,
                        updateLifetime.Token
                    );
                    using var client = LauncherUpdates.CreateClient(store.Config.Settings);
                    await new LauncherUpdates(client).Download(
                        asset,
                        package,
                        new Progress<double>(value => UpdateDownloadProgress(value)),
                        linked.Token,
                        store.Config.Settings.DownloadMirror
                    );
                    linked.Token.ThrowIfCancellationRequested();
                    files = await Task.Run(
                        () =>
                            LauncherUpdateInstaller.Stage(
                                package,
                                stage,
                                LauncherUpdateInstaller.SingleFile,
                                Path.GetFileName(LauncherUpdateInstaller.Executable)
                            ),
                        linked.Token
                    );
                    linked.Token.ThrowIfCancellationRequested();
                },
                Ui("Download launcher update")
            );
            if (release.IsBeta && !store.Config.Settings.ReceiveBetaUpdates)
                return;
            if (task != null || game is { HasExited: false })
                throw new InvalidOperationException(
                    Ui("Finish active tasks and close the game before installing an update.")
                );
            await store.Save();
            await LauncherUpdateInstaller.Start(directory, stage, files!);
            Close();
        }
        finally
        {
            installingLauncherUpdate = false;
            RefreshLauncherUpdateControls();
        }
    }
}