using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyVersionOverview(Action<bool, string> check, string directory)
    {
        var savedPage = page;
        var savedKind = kind;
        var savedSelected = selected;
        var savedSelectedId = store.Config.SelectedInstanceId;
        var savedSize = AppWindow.Size;
        try
        {
            selected = new Instance
            {
                Name = "Overview layout fixture",
                GameVersion = "1.21.1",
                Loader = "vanilla",
                Description = "Version information and file management",
            };
            page = "version-settings";
            kind = "overview";
            foreach (var width in new[] { 740, 1280 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 820));
                await RenderAsync();
                for (
                    var attempt = 0;
                    attempt < 40
                        && !(
                            PageFrame.Content is LauncherContentPage current
                            && ReferenceEquals(current.Content, PageBody)
                        );
                    attempt++
                )
                    await Task.Delay(50);
                await Task.Delay(500);
                ((FrameworkElement)Content).UpdateLayout();
                check(
                    new[]
                    {
                        "instance-overview",
                        "instance-directory",
                        "instance-management",
                        "instance-screenshots",
                    }.All(tag =>
                        Descendants(PageFrame)
                            .OfType<Border>()
                            .Any(card => card.Tag?.ToString() == tag && card.ActualWidth > 0)
                    ),
                    "general version settings displays separate identity, directory, management and screenshot cards: "
                        + width
                );
                check(
                    PageBody
                        .Children.OfType<Border>()
                        .All(card =>
                            card.ActualWidth > 0 && card.ActualWidth <= PageViewport.ActualWidth + 1
                        ),
                    "general version settings cards fit the available width: " + width
                );
                var overview = PageBody
                    .Children.OfType<Border>()
                    .Single(card => card.Tag?.ToString() == "instance-overview");
                check(
                    Descendants(overview)
                        .OfType<Button>()
                        .Select(button => button.Content?.ToString())
                        .SequenceEqual(
                            new[] { Ui("Edit name"), Ui("Edit description"), Ui("Edit icon") }
                        ),
                    "overview provides three separate editing actions: " + width
                );
                var management = PageBody
                    .Children.OfType<Border>()
                    .Single(card => card.Tag?.ToString() == "instance-management");
                check(
                    Descendants(management).OfType<Button>().Single().Content?.ToString()
                        == Ui("Delete version")
                        && Descendants(management).OfType<TextBlock>().Count() <= 2,
                    "version management keeps only the delete action: " + width
                );
                await Capture(
                    Path.Combine(directory, "version-general-" + width + ".png"),
                    PageBody
                );
            }
            var instance = selected!;
            var originalDescription = instance.Description;
            showInstanceEditDialog = dialog =>
            {
                var input = ((StackPanel)dialog.Content).Children.OfType<TextBox>().Single();
                check(
                    dialog.Title?.ToString() == Ui("Edit name")
                        && input.Header?.ToString() == Ui("Version name"),
                    "name editor contains only its own input"
                );
                input.Text = "Renamed fixture";
                return Task.FromResult(ContentDialogResult.Primary);
            };
            await EditInstance("name");
            check(
                instance.Name == "Renamed fixture" && instance.Description == originalDescription,
                "editing the name leaves the description unchanged"
            );
            showInstanceEditDialog = dialog =>
            {
                var input = ((StackPanel)dialog.Content).Children.OfType<TextBox>().Single();
                check(
                    dialog.Title?.ToString() == Ui("Edit description") && input.AcceptsReturn,
                    "description editor has its own multiline input"
                );
                input.Text = "Updated description";
                return Task.FromResult(ContentDialogResult.Primary);
            };
            await EditInstance("description");
            check(
                instance.Name == "Renamed fixture" && instance.Description == "Updated description",
                "editing the description leaves the name unchanged"
            );
            showInstanceEditDialog = dialog =>
            {
                var panel = (StackPanel)dialog.Content;
                check(
                    dialog.Title?.ToString() == Ui("Edit icon")
                        && !panel.Children.OfType<TextBox>().Any()
                        && Descendants(panel)
                            .OfType<Button>()
                            .Any(button =>
                                button.Content?.ToString() == Ui("Restore default icon")
                            ),
                    "icon editor contains image actions without unrelated text inputs"
                );
                return Task.FromResult(ContentDialogResult.None);
            };
            await EditInstance("icon");
            check(
                instance.IconPath == null
                    && instance.Name == "Renamed fixture"
                    && instance.Description == "Updated description",
                "canceling the icon editor preserves existing instance details"
            );
        }
        finally
        {
            showInstanceEditDialog = async dialog => await dialog.ShowAsync();
            selected = savedSelected;
            store.Config.SelectedInstanceId = savedSelectedId;
            kind = savedKind;
            page = savedPage;
            AppWindow.Resize(savedSize);
            await store.Save();
            await RenderAsync();
        }
    }
}
