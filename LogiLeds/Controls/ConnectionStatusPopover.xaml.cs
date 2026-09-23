using System.Windows;
using System.Windows.Controls.Primitives;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiLeds.Controls;

public partial class ConnectionStatusPopover : UserControl
{
    public ConnectionStatusPopover()
    {
        InitializeComponent();
        StatusPopup.CustomPopupPlacementCallback = StatusPopup_OnCustomPopupPlacement;
        StatusPopup.Closed += (_, _) =>
        {
            if (StatusButton.IsChecked == true) StatusButton.IsChecked = false;
        };
    }

    private void StatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        StatusPopup.IsOpen = StatusButton.IsChecked == true;
    }

    private static CustomPopupPlacement[] StatusPopup_OnCustomPopupPlacement(Size popupSize, Size targetSize,
        Point offset)
    {
        var centeredX = (targetSize.Width - popupSize.Width) / 2;
        return [new CustomPopupPlacement(new Point(centeredX, targetSize.Height), PopupPrimaryAxis.Horizontal)];
    }
}