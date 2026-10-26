using System.IO.Compression;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyFeedbackAndSchematics(Action<bool, string> check, string directory)
    {
        var previousPage = page; var previousKind = kind; var previousSelected = selected;
        var previousId = store.Config.SelectedInstanceId;
        var previousDebug = store.Config.Settings.DebugLogging;
        Instance? fixture = null;
        void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        try
        {
            page = "more"; await RenderAsync(); await Task.Delay(450);
            await Run(_ => Task.CompletedTask, "Toast fixture");
            await Task.Delay(150); ((FrameworkElement)Content).UpdateLayout();
            var root = (FrameworkElement)Content;
            var toastPosition = TaskToast.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            check(TaskToast.IsOpen && TaskToast.Severity == InfoBarSeverity.Success && TaskToast.Message == "Toast fixture " + Ui("成功", "succeeded"), "successful tasks show the task name followed by its outcome");
            check(Math.Abs(toastPosition.X + TaskToast.ActualWidth / 2 - root.ActualWidth / 2) < 1
                && toastPosition.Y + TaskToast.ActualHeight <= root.ActualHeight - 20, "task toast is centered near the bottom of the window");
            await Capture(Path.Combine(directory, "task-completion-toast.png"));
            await Task.Delay(4200);
            check(!TaskToast.IsOpen, "completion toast dismisses automatically");
            try { await Run(_ => Task.FromCanceled(new CancellationToken(true)), "Canceled fixture"); } catch (OperationCanceledException) { }
            check(TaskToast.IsOpen && TaskToast.Severity == InfoBarSeverity.Informational, "canceled tasks show an informational toast");
            try { await Run(_ => Task.FromException(new IOException("fixture failure")), "Failure fixture"); } catch (IOException) { }
            check(TaskToast.IsOpen && TaskToast.Severity == InfoBarSeverity.Error, "failed tasks show an error toast");
            TaskToast.IsOpen = false;

            var anchor = new Button { Content = Ui("删除测试项目", "Delete test item"), HorizontalAlignment = HorizontalAlignment.Left };
            PageBody.Children.Insert(0, anchor); PageViewport.ChangeView(null, 0, null, true);
            await Task.Delay(250); PageViewport.UpdateLayout();
            check(anchor.IsLoaded && anchor.XamlRoot != null && anchor.ActualWidth > 0, "popconfirm anchor is attached and laid out");
            var confirmation = ConfirmPop(anchor, Ui("删除项目？", "Delete item?"), "Fixture", Ui("删除", "Delete"));
            await Task.Delay(180);
            check(activePopconfirm != null && !confirmation.IsCompleted && !dialogOpen, "delete confirmation uses an anchored non-modal flyout");
            var panel = (StackPanel)activePopconfirm!.Content;
            await Capture(Path.Combine(directory, "delete-popconfirm.png"), panel);
            Invoke(Descendants(panel).OfType<Button>().Single(b => b.Tag?.ToString() == "popconfirm-cancel"));
            check(!await confirmation, "canceling popconfirm rejects the delete operation");
            await Task.Delay(200);
            confirmation = ConfirmPop(anchor, Ui("删除项目？", "Delete item?"), "Fixture");
            await Task.Delay(100);
            panel = (StackPanel)activePopconfirm!.Content;
            Invoke(Descendants(panel).OfType<Button>().Single(b => b.Tag?.ToString() == "popconfirm-confirm"));
            check(await confirmation, "confirming popconfirm authorizes the operation");
            await Task.Delay(200);
            confirmation = ConfirmPop(anchor, "Fixture", "Dismiss");
            await Task.Delay(100); activePopconfirm!.Hide();
            check(!await confirmation, "dismissing popconfirm does not authorize deletion");

            var gameRoot = Path.Combine(directory, "schematic-ui-game");
            Directory.CreateDirectory(Path.Combine(gameRoot, "mods"));
            using (var zip = new ZipArchive(File.Create(Path.Combine(gameRoot, "mods", "projection.jar")), ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) writer.Write("""{"id":"litematica"}""");
            fixture = new Instance { Name = "投影管理测试", GameVersion = "1.21.1", Loader = "fabric", Directory = gameRoot };
            store.Config.Instances.Add(fixture); selected = fixture;
            page = "version-settings"; kind = "overview"; await RenderAsync(); await schematicAvailabilityLoad; await Task.Delay(450);
            var schematicDirectory = Path.Combine(gameRoot, "schematics");
            check(Directory.Exists(schematicDirectory) && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == Ui("投影管理", "Schematics")),
                "supported Mod metadata creates the folder and adds schematic management");
            await File.WriteAllTextAsync(Path.Combine(schematicDirectory, "house.litematic"), "fixture");
            Directory.CreateDirectory(Path.Combine(schematicDirectory, "parts"));
            await File.WriteAllTextAsync(Path.Combine(schematicDirectory, "parts", "machine.nbt"), "fixture");
            kind = "schematics"; await RenderAsync(); await schematicAvailabilityLoad; await schematicLoad; await Task.Delay(300);
            var folder = Descendants(PageBody).OfType<Expander>().Single(e => e.Tag?.ToString() == "schematic-folder");
            check(!folder.IsExpanded, "schematic folders start collapsed");
            folder.IsExpanded = true; await Task.Delay(180);
            check(Descendants(PageBody).OfType<Grid>().Count(g => g.Tag?.ToString() == "schematic-row") == 2, "schematic management lists projection files and nested Create blueprints");
            var search = Descendants(PageBody).OfType<TextBox>().Single(t => t.Tag?.ToString() == "schematic-search");
            search.Text = "machine"; await Task.Delay(100); await schematicLoad;
            check(Descendants(PageBody).OfType<Grid>().Count(g => g.Tag?.ToString() == "schematic-row") == 1, "schematic file search filters nested paths");
            check(Descendants(PageBody).OfType<Expander>().Single(e => e.Tag?.ToString() == "schematic-folder").IsExpanded, "search expands the matching schematic directory");
            search.Text = ""; await Task.Delay(100); await schematicLoad; PageViewport.ChangeView(null, 0, null, true); await Task.Delay(450);
            await Capture(Path.Combine(directory, "schematics-management.png"));
            fixture.Loader = "vanilla"; await RenderAsync();
            check(kind == "overview" && !Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == Ui("投影管理", "Schematics")),
                "vanilla versions hide schematic management even when a folder exists");
            page = "logs"; await RenderAsync(); await Task.Delay(200);
            var debug = Descendants(PageBody).OfType<ToggleSwitch>().Single(t => t.Tag?.ToString() == "debug-logging");
            debug.IsOn = true; await Task.Delay(200);
            RuntimeLog.Debug("UI debug fixture"); await Task.Delay(100);
            check(store.Config.Settings.DebugLogging && logView!.Text.Contains("[DEBUG]") && logView.Text.Contains("UI debug fixture"), "DEBUG switch persists and displays detailed runtime events");
            RuntimeLog.Debug("access_token=secret-test refresh_token=refresh-test Authorization: Bearer bearer-test"); await Task.Delay(100);
            check(!logView!.Text.Contains("secret-test") && !logView.Text.Contains("refresh-test") && !logView.Text.Contains("bearer-test"), "DEBUG display and disk logger redact credential fields");
            await Capture(Path.Combine(directory, "debug-logs.png"));
            debug.IsOn = false; await Task.Delay(100);
            var before = logs.Count; RuntimeLog.Debug("hidden debug fixture"); await Task.Delay(100);
            check(logs.Count == before, "disabling DEBUG stops new diagnostic events");
        }
        finally
        {
            activePopconfirm?.Hide(); TaskToast.IsOpen = false;
            if (fixture != null) store.Config.Instances.Remove(fixture);
            selected = previousSelected; store.Config.SelectedInstanceId = previousId;
            store.Config.Settings.DebugLogging = previousDebug; RuntimeLog.DebugEnabled = previousDebug;
            await store.Save(); page = previousPage; kind = previousKind;
            await RenderAsync(); await communityLoad; await communityFilterLoad;
        }
    }
}
