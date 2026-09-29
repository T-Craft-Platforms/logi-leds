using System.Globalization;
using System.Windows.Data;
using LogiWheelForge.Models;
using Binding = System.Windows.Data.Binding;

namespace LogiWheelForge.Controls;

public sealed class MapperChoiceLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        MapperTriggerKind.Button => "Button press",
        MapperTriggerKind.AxisRange => "Within a range",
        MapperTriggerKind.AxisStep => "Every movement step",
        MapperStepMode.Movement => "As the control moves",
        MapperStepMode.AwayFromCenter => "Away from the center",
        MapperActionKind.Key => "Keyboard key",
        MapperActionKind.MouseButton => "Mouse button",
        MapperActionKind.MouseMove => "Move the pointer",
        MapperActionKind.MouseScroll => "Scroll the mouse wheel",
        MapperActionKind.HoldTarget => "Hold wheel position",
        MapperActionKind.ReleaseTarget => "Release wheel position",
        MapperActionMode.Hold => "Hold while active",
        MapperActionMode.Tap => "Tap once",
        MapperActionMode.Toggle => "Toggle on / off",
        MapperForceRelease.RangeExit => "When leaving the range",
        MapperForceRelease.Explicit => "With a release rule",
        -1 => "Left / decreasing",
        1 => "Right / increasing",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
