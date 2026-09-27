using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiWheelForge.Controls;

public partial class RpmRangeEditor : UserControl
{
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(RpmRangeEditor),
            new PropertyMetadata(false));

    public RpmRangeEditor()
    {
        InitializeComponent();
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }
}