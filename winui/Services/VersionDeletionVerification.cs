using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyDeletionReasons(Action<bool, string> check, string directory)
    {
        var parentId = "deletion-dependency-parent";
        var childId = "deletion-dependency-child";
        await Store.AtomicJson(
            Store.SafePath(store.Library, $"versions/{parentId}/{parentId}.json"),
            new JsonObject { ["id"] = parentId }
        );
        await Store.AtomicJson(
            Store.SafePath(store.Library, $"versions/{childId}/{childId}.json"),
            new JsonObject { ["id"] = childId, ["inheritsFrom"] = parentId }
        );
        var parent = new Instance
        {
            Name = parentId,
            VersionId = parentId,
            GameVersion = parentId,
            ImportRoot = store.Library,
            Directory = store.Library,
        };
        var child = new Instance
        {
            Name = childId,
            VersionId = childId,
            GameVersion = parentId,
            ImportRoot = store.Library,
            Directory = store.Library,
        };
        var saved = showVersionDeletionDialog;
        store.Config.Instances.AddRange([parent, child]);
        try
        {
            showVersionDeletionDialog = async dialog =>
            {
                var body = (StackPanel)((ScrollViewer)dialog.Content).Content;
                check(
                    body.Children.OfType<CheckBox>().Single().IsEnabled == false
                        && body.Children.OfType<TextBlock>().Any(t => t.Text.Contains(childId)),
                    "dependency deletion warning names the actual referencing version"
                );
                var shown = dialog.ShowAsync();
                await Task.Delay(300);
                var primary = Descendants(dialog)
                    .OfType<Button>()
                    .Single(b => b.Name == "PrimaryButton");
                check(
                    primary.CornerRadius.TopLeft == 5 && primary.CornerRadius.BottomRight == 5,
                    "red delete confirmation button keeps rounded corners"
                );
                check(
                    ReferenceEquals(primary.Foreground, versionDeleteText)
                        && ReferenceEquals(primary.BorderBrush, versionDeleteText),
                    "rounded delete confirmation retains its red text and border"
                );
                check(
                    dialog.PrimaryButtonStyle.BasedOn != null && primary.Template != null,
                    "red delete button inherits the modern native button template"
                );
                var presenter = Descendants(primary)
                    .OfType<ContentPresenter>()
                    .First(p => p.Name == "ContentPresenter");
                check(
                    presenter.CornerRadius.TopLeft == 5,
                    "delete button rendered border uses its rounded corner value"
                );
                VisualStateManager.GoToState(primary, "PointerOver", false);
                check(
                    presenter.CornerRadius.TopLeft == 5,
                    "delete button remains rounded while hovered"
                );
                VisualStateManager.GoToState(primary, "Normal", false);
                await Capture(Path.Combine(directory, "dependency-delete-modal.png"), dialog);
                dialog.Hide();
                await shown;
                return ContentDialogResult.None;
            };
            await ConfirmDeleteInstance(parent, PageTitle);
            check(
                store.Config.Instances.Contains(parent)
                    && Directory.Exists(Store.SafePath(store.Library, "versions/" + parentId)),
                "canceling a dependency warning keeps the version and its files"
            );
        }
        finally
        {
            showVersionDeletionDialog = saved;
            store.Config.Instances.Remove(parent);
            store.Config.Instances.Remove(child);
        }
    }
}
