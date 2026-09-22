using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LogiLeds.Commands;
using LogiLeds.Models;
using LogiLeds.Services;
using Media = System.Windows.Media;
using Application = System.Windows.Application;

namespace LogiLeds.ViewModels;

public sealed class RpmProfileViewModel : ObservableObject, IDisposable
{
    private readonly SettingsDraft _draft;
    private readonly AsyncRelayCommand _resetLearningCommand;
    private readonly LedApplicationService _service;
    private readonly Action<string> _setStatus;
    private WheelDefinition? _activeDefinition;
    private double? _learnedRedlinePercent;
    private string? _loadedProfileWheelId;

    public RpmProfileViewModel(LedApplicationService service, SettingsDraft draft, Action<string> setStatus,
        Func<Task> saveCommand)
    {
        _service = service;
        _draft = draft;
        _setStatus = setStatus;
        SaveCommand = new AsyncRelayCommand(saveCommand);
        ResetProfileCommand = new RelayCommand(ResetProfile);
        _resetLearningCommand = new AsyncRelayCommand(() => _service.ResetLearningAsync());
        _draft.PropertyChanged += OnDraftPropertyChanged;
        _service.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<ThresholdViewModel> Thresholds { get; } = [];
    public ICommand SaveCommand { get; }
    public ICommand ResetProfileCommand { get; }
    public ICommand ResetLearningCommand => _resetLearningCommand;

    public double FirstLedPercent
    {
        get => _draft.FirstLedPercent;
        set => _draft.FirstLedPercent = value;
    }

    public double RedlinePercent
    {
        get => _draft.RedlinePercent;
        set => _draft.RedlinePercent = value;
    }

    public bool BlinkAtRedline
    {
        get => _draft.BlinkAtRedline;
        set => _draft.BlinkAtRedline = value;
    }

    public bool LearnPerCarShift
    {
        get => _draft.LearnPerCarShift;
        set => _draft.LearnPerCarShift = value;
    }

    public RpmProfileMode ProfileMode
    {
        get => _draft.ProfileMode;
        set => _draft.ProfileMode = value;
    }

    public bool IsEasyMode
    {
        get => _draft.IsEasyMode;
        set => _draft.IsEasyMode = value;
    }

    public bool IsAdvancedMode
    {
        get => _draft.IsAdvancedMode;
        set => _draft.IsAdvancedMode = value;
    }

    public string CalibrationStatus => _learnedRedlinePercent is double value
        ? $"Learned shift point: {value:0.0}%"
        : "Learning per-car shift";

    public void Dispose()
    {
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        _service.SnapshotChanged -= OnSnapshotChanged;
    }

    public void Initialize(WheelDefinition? selectedWheel, IReadOnlyList<double> thresholds)
    {
        ConfigureThresholds(selectedWheel, thresholds);
        _service.SetPreviewWheel(_draft.SelectedWheel?.Id);
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsDraft.SelectedWheel))
        {
            _service.SetPreviewWheel(_draft.SelectedWheel?.Id);
            var wheel = _service.Wheels.FirstOrDefault(x => x.Id == _draft.SelectedWheel?.Id);
            if (wheel is not null) _ = LoadWheelProfileAsync(wheel, false);
        }
        else if (e.PropertyName == nameof(SettingsDraft.FirstLedPercent)) OnPropertyChanged(nameof(FirstLedPercent));
        else if (e.PropertyName == nameof(SettingsDraft.RedlinePercent)) OnPropertyChanged(nameof(RedlinePercent));
        else if (e.PropertyName == nameof(SettingsDraft.BlinkAtRedline)) OnPropertyChanged(nameof(BlinkAtRedline));
        else if (e.PropertyName == nameof(SettingsDraft.LearnPerCarShift)) OnPropertyChanged(nameof(LearnPerCarShift));
        else if (e.PropertyName == nameof(SettingsDraft.ProfileMode))
        {
            OnPropertyChanged(nameof(ProfileMode));
            OnPropertyChanged(nameof(IsEasyMode));
            OnPropertyChanged(nameof(IsAdvancedMode));
        }
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySnapshot(snapshot);
        else dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        if (_learnedRedlinePercent != snapshot.LearnedRedlinePercent)
        {
            _learnedRedlinePercent = snapshot.LearnedRedlinePercent;
            OnPropertyChanged(nameof(CalibrationStatus));
        }

        if (snapshot.Wheel is null || snapshot.Wheel.Id == _activeDefinition?.Id) return;
        _activeDefinition = snapshot.Wheel;
        _ = LoadWheelProfileAsync(snapshot.Wheel, true);
    }

    private void ResetProfile()
    {
        FirstLedPercent = _activeDefinition?.DefaultFirstPercent ?? LedProfileSettings.DefaultFirstLedPercent;
        RedlinePercent = _activeDefinition?.DefaultRedlinePercent ?? LedProfileSettings.DefaultRedlinePercent;
        BlinkAtRedline = true;
        ConfigureThresholds(_activeDefinition, []);
    }

    private void ConfigureThresholds(WheelDefinition? definition, IReadOnlyList<double> saved)
    {
        var wheel = definition ?? _service.Wheels.FirstOrDefault();
        var count = wheel?.ControlGroupCount ?? 5;
        var values = saved.Count == count ? saved.ToArray() : LedMath.BuildRecommendedThresholds(count);
        var colors = wheel?.Colors ?? ["#38D982", "#6EE65A", "#F0D84A", "#FFAA3B", "#FF5265"];
        Thresholds.Clear();
        for (var i = 0; i < count; i++)
            Thresholds.Add(new ThresholdViewModel
            {
                Label = i == count - 1 ? "Red light" : $"LED group {i + 1}",
                Color = (Media.Brush)new Media.BrushConverter().ConvertFromString(
                    colors[Math.Min(i, colors.Length - 1)])!,
                Value = values[i]
            });
    }

    private async Task LoadWheelProfileAsync(WheelDefinition wheel, bool applyToService)
    {
        try
        {
            if (!applyToService && _loadedProfileWheelId == wheel.Id) return;
            var profile = await _service.LoadWheelProfileAsync(wheel);
            _loadedProfileWheelId = wheel.Id;
            ProfileMode = profile.Mode;
            FirstLedPercent = profile.FirstLedPercent;
            RedlinePercent = profile.RedlinePercent;
            BlinkAtRedline = profile.BlinkAtRedline;
            ConfigureThresholds(wheel, profile.AdvancedThresholds);
            if (applyToService)
                await _service.UpdateSettingsAsync(_service.Settings with
                {
                    ProfileMode = profile.Mode, FirstLedPercent = profile.FirstLedPercent,
                    RedlinePercent = profile.RedlinePercent, BlinkAtRedline = profile.BlinkAtRedline,
                    AdvancedThresholds = profile.AdvancedThresholds
                });
        }
        catch (Exception ex)
        {
            _setStatus($"Could not load wheel profile: {ex.Message}");
        }
    }
}