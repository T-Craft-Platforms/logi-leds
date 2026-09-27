using System.Windows;
using LogiWheelForge.ViewModels;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiWheelForge.Views;

public partial class RpmProfileView : UserControl
{
    private CarTrainingLibraryWindow? _carTrainingWindow;

    public RpmProfileView()
    {
        InitializeComponent();
    }

    private void OpenCarTrainings_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RpmProfileViewModel viewModel) return;
        if (_carTrainingWindow is { IsVisible: true }) return;
        _carTrainingWindow = new CarTrainingLibraryWindow(viewModel) { Owner = Window.GetWindow(this) };
        _carTrainingWindow.Closed += (_, _) => _carTrainingWindow = null;
        _carTrainingWindow.Show();
    }

    private void MoreProfileActions_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}