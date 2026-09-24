using System.Windows;
using LogiLeds.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiLeds.Views;

public partial class RpmProfileView : UserControl
{
    public RpmProfileView()
    {
        InitializeComponent();
    }

    private void OpenCarTrainings_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RpmProfileViewModel viewModel) return;
        new CarTrainingLibraryWindow(viewModel)
            { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}