using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // Separate identity, file access and destructive actions without adding another navigation layer.
    void InstanceOverview(Instance instance)
    {
        var details = new StackPanel { Spacing = 6 };
        var title = Text(instance.Name, 20, true);
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        details.Children.Add(title);
        details.Children.Add(
            Text($"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}", 13)
        );
        details.Children.Add(
            Text(
                instance.Description.Length == 0 ? Ui("No description yet.") : instance.Description,
                13
            )
        );
        var identity = new Grid { ColumnSpacing = 14 };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        identity.Children.Add(
            new Border
            {
                Width = 56,
                Height = 56,
                VerticalAlignment = VerticalAlignment.Top,
                Child = InstanceIcon(instance),
            }
        );
        Grid.SetColumn(details, 1);
        identity.Children.Add(details);
        var summary = new StackPanel { Spacing = 14 };
        summary.Children.Add(identity);
        summary.Children.Add(
            Buttons(
                Action(Ui("Edit name"), async (_, _) => await EditInstance("name")),
                Action(Ui("Edit description"), async (_, _) => await EditInstance("description")),
                Action(Ui("Edit icon"), async (_, _) => await EditInstance("icon"))
            )
        );
        var summaryCard = Card(summary);
        summaryCard.Tag = "instance-overview";
        PageBody.Children.Add(summaryCard);

        var folder = new StackPanel { Spacing = 10 };
        folder.Children.Add(Text(Ui("Game directory"), 15, true));
        var path = Text(store.GameDirectory(instance), 12);
        path.IsTextSelectionEnabled = true;
        path.TextWrapping = TextWrapping.Wrap;
        folder.Children.Add(path);
        folder.Children.Add(
            Buttons(
                Action(
                    Ui("Open folder [context 293]"),
                    (_, _) => OpenFolder(store.GameDirectory(instance))
                )
            )
        );
        var folderCard = Card(folder);
        folderCard.Tag = "instance-directory";
        PageBody.Children.Add(folderCard);

        var management = new StackPanel { Spacing = 10 };
        management.Children.Add(Text(Ui("Version management"), 15, true));
        management.Children.Add(
            Buttons(
                VersionDeleteButton(
                    Action(
                        Ui("Delete version"),
                        async (sender, _) =>
                            await ConfirmDeleteInstance(instance, (FrameworkElement)sender)
                    )
                )
            )
        );
        var managementCard = Card(management);
        managementCard.Tag = "instance-management";
        PageBody.Children.Add(managementCard);
    }
}
