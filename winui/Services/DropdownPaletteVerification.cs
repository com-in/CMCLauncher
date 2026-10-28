using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyDropdownPalette(Action<bool, string> check, string directory)
    {
        var savedPage = page;
        var savedTheme = store.Config.Settings.Theme;
        var savedPreset = store.Config.Settings.ColorPreset;
        BelowComboBox? combo = null;
        try
        {
            page = "more";
            await RenderAsync();
            await Task.Delay(450);
            combo = new BelowComboBox
            {
                Header = "Dropdown palette",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            combo.Items.Add("First item");
            combo.Items.Add("Selected item");
            combo.Items.Add(new ComboBoxItem { Content = "Disabled item", IsEnabled = false });
            for (var i = 3; i < 60; i++)
                combo.Items.Add("Item " + i);
            combo.SelectedIndex = 1;
            PageBody.Children.Add(combo);
            ((FrameworkElement)Content).UpdateLayout();
            foreach (var preset in new[] { "craft", "lakecyan", "sakura", "native", "custom" })
            foreach (var theme in new[] { "light", "dark" })
            {
                combo.IsDropDownOpen = false;
                store.Config.Settings.ColorPreset = preset;
                store.Config.Settings.Theme = theme;
                ApplyTheme();
                if (preset == "native")
                {
                    var surfaces = new[]
                    {
                        ComboSurfaceBrush,
                        ComboHoverBrush,
                        ComboPressedBrush,
                        ComboSelectedBrush,
                        ComboSelectedHoverBrush,
                        ComboSelectedPressedBrush,
                    };
                    check(
                        surfaces.All(b => b.Color.R == b.Color.G && b.Color.G == b.Color.B),
                        theme
                            + " native surfaces and interaction fills do not inherit the accent color"
                    );
                }
                combo.IsDropDownOpen = true;
                await Task.Delay(120);
                check(
                    combo.PopupBorder?.Background is SolidColorBrush surface
                        && surface.Color == ComboSurfaceBrush.Color
                        && combo.PopupBorder.BorderBrush is SolidColorBrush border
                        && border.Color == ComboBorderBrush.Color,
                    preset + " " + theme + " popup uses the themed surface and border"
                );
                var item = (ComboBoxItem)combo.ContainerFromIndex(1);
                item.ApplyTemplate();
                var layout = (Grid)VisualTreeHelper.GetChild(item, 0);
                foreach (
                    var (state, expected) in new (string State, SolidColorBrush Brush)[]
                    {
                        ("PointerOver", ComboHoverBrush),
                        ("Pressed", ComboPressedBrush),
                        ("Selected", ComboSelectedBrush),
                        ("SelectedUnfocused", ComboSelectedBrush),
                        ("SelectedPointerOver", ComboSelectedHoverBrush),
                        ("SelectedPressed", ComboSelectedPressedBrush),
                        ("SelectedDisabled", ComboSelectedDisabledBrush),
                    }
                )
                {
                    var found = VisualStateManager.GoToState(item, state, false);
                    await Task.Delay(20);
                    check(
                        found
                            && layout.Background is SolidColorBrush background
                            && background.Color == expected.Color,
                        preset + " " + theme + " dropdown item colors state " + state
                    );
                    var presenter = Descendants(item)
                        .OfType<ContentPresenter>()
                        .First(p => p.Name == "ContentPresenter");
                    var expectedText =
                        state == "SelectedDisabled" ? ComboDisabledTextBrush : ComboTextBrush;
                    check(
                        presenter.Foreground is SolidColorBrush text
                            && text.Color == expectedText.Color,
                        preset + " " + theme + " dropdown item text state " + state
                    );
                }
                var disabled = (ComboBoxItem)combo.ContainerFromIndex(2);
                check(
                    !disabled.IsEnabled
                        && ReferenceEquals(
                            disabled.Resources["ComboBoxItemForegroundDisabled"],
                            ComboDisabledTextBrush
                        ),
                    preset + " " + theme + " disabled options stay disabled and use muted text"
                );
                VisualStateManager.GoToState(item, "Selected", false);
                if (preset is "craft" or "lakecyan" or "sakura" or "native")
                    await Capture(
                        Path.Combine(directory, $"dropdown-{preset}-{theme}.png"),
                        (FrameworkElement)combo.DropDownPopup!.Child
                    );
            }
            store.Config.Settings.ColorPreset = "craft";
            store.Config.Settings.Theme = "light";
            ApplyTheme();
            combo.IsDropDownOpen = true;
            await Task.Delay(80);
            var openBorder = combo.PopupBorder!;
            var liveBrush = openBorder.Background;
            var oldColor = ((SolidColorBrush)liveBrush).Color;
            store.Config.Settings.Theme = "dark";
            ApplyTheme();
            await Task.Delay(80);
            check(
                combo.IsDropDownOpen
                    && ReferenceEquals(openBorder.Background, liveBrush)
                    && ((SolidColorBrush)liveBrush).Color != oldColor
                    && ((SolidColorBrush)liveBrush).Color == ComboSurfaceBrush.Color,
                "an already-open dropdown refreshes colors when the theme changes"
            );
            combo.SelectedIndex = 40;
            combo.IsDropDownOpen = true;
            await Task.Delay(100);
            var lateItem = combo.ContainerFromIndex(40) as ComboBoxItem;
            check(
                lateItem != null
                    && ReferenceEquals(
                        lateItem.Resources["ComboBoxItemBackgroundSelected"],
                        ComboSelectedBrush
                    ),
                "later generated dropdown containers receive the same palette"
            );
            combo.IsDropDownOpen = false;
            combo.IsDropDownOpen = true;
            await Task.Delay(80);
            check(
                combo.SelectedIndex == 40 && combo.PopupBorder?.Background == ComboSurfaceBrush,
                "closing and reopening preserves selection and current palette"
            );
        }
        finally
        {
            if (combo != null)
                combo.IsDropDownOpen = false;
            store.Config.Settings.Theme = savedTheme;
            store.Config.Settings.ColorPreset = savedPreset;
            ApplyTheme();
            page = savedPage;
            await RenderAsync();
            await communityFilterLoad;
            await communityLoad;
        }
    }
}
