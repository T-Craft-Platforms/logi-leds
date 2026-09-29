using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using System.Windows.Controls;

namespace LogiWheelForge.Controls;

public partial class MapperRuleEditor : UserControl
{
    public MapperRuleEditor() => InitializeComponent();

    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var wide = args.NewSize.Width >= 520;
        OutputColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(OutputCard, wide ? 1 : 0);
        Grid.SetRow(OutputCard, wide ? 0 : 1);
        InputCard.Margin = wide ? new Thickness(0, 0, 6, 0) : new Thickness(0);
        OutputCard.Margin = wide ? new Thickness(6, 0, 0, 0) : new Thickness(0, 12, 0, 0);
    }
}
