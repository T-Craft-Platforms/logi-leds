using System.ComponentModel;
using System.Runtime.CompilerServices;
using Media = System.Windows.Media;

namespace LogiLeds.ViewModels;

public sealed class LedIndicatorViewModel(string color, int group) : INotifyPropertyChanged
{
    private bool _isBlinking;
    private bool _isLit;
    public Media.Brush Color { get; } = (Media.Brush)new Media.BrushConverter().ConvertFromString(color)!;
    public int Group { get; } = group;

    public bool IsLit
    {
        get => _isLit;
        set
        {
            if (_isLit == value) return;
            _isLit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Opacity));
        }
    }

    public bool IsBlinking
    {
        get => _isBlinking;
        set
        {
            if (_isBlinking == value) return;
            _isBlinking = value;
            OnPropertyChanged();
        }
    }

    public double Opacity => IsLit ? 1 : 0.12;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ThresholdViewModel : INotifyPropertyChanged
{
    private double _value;
    public required string Label { get; init; }
    public required Media.Brush Color { get; init; }

    public double Value
    {
        get => _value;
        set
        {
            var next = Math.Round(value, 1);
            if (Math.Abs(_value - next) < .01) return;
            _value = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record WheelOption(string? Id, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}