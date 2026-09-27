using System.Collections.ObjectModel;
using LogiWheelForge.Services;
using Media = System.Windows.Media;

namespace LogiWheelForge.ViewModels;

public sealed class CarTrainingViewModel : ObservableObject
{
    private int _confidencePercent;
    private bool _isCurrent;
    private double? _learnedRedlinePercent;
    private int _shiftCount;

    public CarTrainingViewModel(CarTrainingMapping mapping)
    {
        GameTitle = mapping.GameTitle;
        ProtocolVariant = mapping.ProtocolVariant;
        CarOrdinal = mapping.CarOrdinal;
        EngineMaxRpm = mapping.EngineMaxRpm;
        var colors = new[] { "#38D982", "#6EE65A", "#F0D84A", "#FFAA3B", "#FF5265" };
        for (var i = 0; i < colors.Length; i++)
            Thresholds.Add(new ThresholdViewModel
            {
                Label = i == colors.Length - 1 ? "Red light" : $"LED group {i + 1}",
                Color = (Media.Brush)new Media.BrushConverter().ConvertFromString(colors[i])!
            });
        Update(mapping, false);
    }

    public string GameTitle { get; }
    public string ProtocolVariant { get; }
    public int CarOrdinal { get; }
    public float EngineMaxRpm { get; }
    public ObservableCollection<ThresholdViewModel> Thresholds { get; } = [];
    public string Key => $"{GameTitle}|{ProtocolVariant}|{CarOrdinal}|{EngineMaxRpm:0}";
    public string VehicleLabel => $"Car #{CarOrdinal}";
    public int ProgressPercent => Math.Min(100, _shiftCount * 20);

    public string ProgressLabel => _learnedRedlinePercent is null
        ? $"{_shiftCount} samples · needs 3 matching shifts"
        : $"{_shiftCount} samples";

    public string ConfidenceLabel => _learnedRedlinePercent is null
        ? "Not learned yet"
        : $"{_confidencePercent}% confidence";

    public string ShiftPointLabel => _learnedRedlinePercent is double value
        ? $"Learned shift point  {value:0.0}%"
        : "Shift point pending";

    public string MappingDetails => $"{GameTitle} · {EngineMaxRpm:N0} RPM";
    public bool HasMapping => _learnedRedlinePercent is not null;

    public bool IsCurrent
    {
        get => _isCurrent;
        private set => SetField(ref _isCurrent, value);
    }

    // RpmRangeEditor uses two-way bindings for its editable profile sliders.
    // Training cards disable those controls, so safely ignore any source updates.
    public double RedlinePercent
    {
        get => _learnedRedlinePercent ?? 90;
        set { }
    }

    public void Update(CarTrainingMapping mapping, bool isCurrent)
    {
        IsCurrent = isCurrent;
        if (_shiftCount != mapping.ShiftCount || _confidencePercent != mapping.ConfidencePercent ||
            _learnedRedlinePercent != mapping.LearnedRedlinePercent)
        {
            _shiftCount = mapping.ShiftCount;
            _confidencePercent = mapping.ConfidencePercent;
            _learnedRedlinePercent = mapping.LearnedRedlinePercent;
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressLabel));
            OnPropertyChanged(nameof(ConfidenceLabel));
            OnPropertyChanged(nameof(ShiftPointLabel));
            OnPropertyChanged(nameof(HasMapping));
            OnPropertyChanged(nameof(RedlinePercent));
            if (_learnedRedlinePercent is double redline)
            {
                var values = LedMath.BuildRecommendedThresholds(5, 65, Math.Max(65, redline - 5));
                for (var i = 0; i < values.Length; i++) Thresholds[i].Value = values[i];
            }
        }
    }
}