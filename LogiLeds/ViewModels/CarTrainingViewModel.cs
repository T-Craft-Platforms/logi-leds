using System.Collections.ObjectModel;
using LogiLeds.Services;
using Media = System.Windows.Media;

namespace LogiLeds.ViewModels;

public sealed class CarTrainingViewModel
{
    public CarTrainingViewModel(CarTrainingMapping mapping)
    {
        GameTitle = mapping.GameTitle;
        ProtocolVariant = mapping.ProtocolVariant;
        CarOrdinal = mapping.CarOrdinal;
        EngineMaxRpm = mapping.EngineMaxRpm;
        RedlinePercent = mapping.LearnedRedlinePercent;
        var finalLedPercent = Math.Max(65, RedlinePercent - 5);
        var values = LedMath.BuildRecommendedThresholds(5, 65, finalLedPercent);
        var colors = new[] { "#38D982", "#6EE65A", "#F0D84A", "#FFAA3B", "#FF5265" };
        for (var i = 0; i < values.Length; i++)
            Thresholds.Add(new ThresholdViewModel
            {
                Label = i == values.Length - 1 ? "Red light" : $"LED group {i + 1}",
                Value = values[i],
                Color = (Media.Brush)new Media.BrushConverter().ConvertFromString(colors[i])!
            });
    }

    public string GameTitle { get; }
    public string ProtocolVariant { get; }
    public int CarOrdinal { get; }
    public float EngineMaxRpm { get; }
    public double RedlinePercent { get; set; }
    public ObservableCollection<ThresholdViewModel> Thresholds { get; } = [];
    public string VehicleLabel => $"Car #{CarOrdinal}";

    public string MappingDetails =>
        $"{GameTitle} · {ProtocolVariant} · {EngineMaxRpm:N0} RPM engine · Learned shift {RedlinePercent:0.0}%";
}