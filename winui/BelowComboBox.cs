using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CraftLauncher.WinUI;

// Keep native keyboard navigation and selection while anchoring the list below its field.
public sealed class BelowComboBox : ComboBox
{
    public BelowComboBox()
    {
        DefaultStyleKey = typeof(ComboBox);
        DropDownOpened += (_, _) => AnchorPopup();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AnchorPopup();
    }

    internal Popup? DropDownPopup => GetTemplateChild("Popup") as Popup;
    internal Microsoft.UI.Xaml.FrameworkElement? InputBorder => GetTemplateChild("Background") as Microsoft.UI.Xaml.FrameworkElement;

    void AnchorPopup()
    {
        if (DropDownPopup is not { } popup) return;
        popup.PlacementTarget = InputBorder ?? this;
        popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        popup.VerticalOffset = 0;
        popup.HorizontalOffset = 0;
    }
}
