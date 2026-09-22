using System.Windows;
using LogiLeds.ViewModels;

namespace LogiLeds.Views;

public partial class CarTrainingLibraryWindow : Window
{
    private readonly Func<Task> _resetTrainings;

    public CarTrainingLibraryWindow(IReadOnlyList<CarTrainingViewModel> trainings, Func<Task> resetTrainings)
    {
        InitializeComponent();
        _resetTrainings = resetTrainings;
        DataContext = trainings;
        EmptyState.Visibility = trainings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetTrainingsButton.IsEnabled = trainings.Count > 0;
    }

    private async void ResetTrainings_OnClick(object sender, RoutedEventArgs e)
    {
        ResetTrainingsButton.IsEnabled = false;
        try
        {
            await _resetTrainings();
            DataContext = Array.Empty<CarTrainingViewModel>();
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            ResetTrainingsButton.IsEnabled = false;
        }
    }
}