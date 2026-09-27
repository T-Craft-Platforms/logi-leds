using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LogiLeds.Controls;
using LogiLeds.ViewModels;
using Brush = System.Windows.Media.Brush;

namespace LogiLeds.Views;

public partial class CarTrainingLibraryWindow : Window
{
    private readonly RpmProfileViewModel _profile;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _resetting;

    public CarTrainingLibraryWindow(RpmProfileViewModel profile)
    {
        InitializeComponent();
        OwnedWindowDimmer.Attach(this);
        _profile = profile;
        DataContext = this;
        Refresh();
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public ObservableCollection<CarTrainingViewModel> Trainings { get; } = [];

    private void Refresh()
    {
        var overview = _profile.GetTrainingOverview();
        var incoming = overview.Entries.Select(entry =>
                (entry,
                    key:
                    $"{entry.Mapping.GameTitle}|{entry.Mapping.ProtocolVariant}|{entry.Mapping.CarOrdinal}|{entry.Mapping.EngineMaxRpm:0}"))
            .ToArray();
        foreach (var old in Trainings.Where(item => incoming.All(next => next.key != item.Key)).ToArray())
            Trainings.Remove(old);
        foreach (var (entry, key) in incoming)
        {
            var item = Trainings.FirstOrDefault(training => training.Key == key);
            if (item is null)
            {
                item = new CarTrainingViewModel(entry.Mapping);
                Trainings.Add(item);
            }

            item.Update(entry.Mapping, entry.IsCurrent);
        }

        EmptyState.Visibility = Trainings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetTrainingsButton.IsEnabled = !_resetting && Trainings.Count > 0;
        CurrentVehicleText.Text = overview.CurrentVehicle;
        TrainingStatusText.Text = !overview.IsEnabled ? "Learning off · enable it in Smart mode"
            : !overview.IsLive ? "Waiting for Forza telemetry"
            : overview.IsSampling ? "Collecting shift sample"
            : "Ready · upshift at full throttle";
        TrainingIndicator.Fill = overview.IsSampling
            ? (Brush)FindResource("SuccessBrush")
            : overview.IsLive && overview.IsEnabled
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("MutedBrush");
    }

    private async void ResetTrainings_OnClick(object sender, RoutedEventArgs e)
    {
        _resetting = true;
        ResetTrainingsButton.IsEnabled = false;
        string? error = null;
        try
        {
            await _profile.ResetCarTrainingsAsync();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            _resetting = false;
            Refresh();
            if (error is not null) TrainingStatusText.Text = $"Could not reset trainings: {error}";
        }
    }
}