using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Media;
using LogiLeds.Models;
using LogiLeds.ViewModels;
using ComboBox = System.Windows.Controls.ComboBox;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiLeds.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SizeSelectorToContent(ThemeSelector);
            SizeSelectorToContent(WheelSelector);
            if (WheelSelector.ItemsSource is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += WheelOptions_OnCollectionChanged;
                Unloaded += (_, _) => collection.CollectionChanged -= WheelOptions_OnCollectionChanged;
            }
        };
    }

    private void WheelOptions_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SizeSelectorToContent(WheelSelector);
    }

    private void AddGame_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel) viewModel.AddGame();
    }

    private void ManageGame_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel &&
            sender is System.Windows.Controls.Button { Tag: TelemetryGameSettings game })
            viewModel.ManageGame(game);
    }

    private static void SizeSelectorToContent(ComboBox selector)
    {
        var dpi = VisualTreeHelper.GetDpi(selector).PixelsPerDip;
        var typeface = new Typeface(selector.FontFamily, selector.FontStyle, selector.FontWeight, selector.FontStretch);
        var maxTextWidth = selector.Items.Cast<object?>()
            .Select(item =>
            {
                var text = new FormattedText(
                    item?.ToString() ?? string.Empty,
                    CultureInfo.CurrentCulture,
                    selector.FlowDirection,
                    typeface,
                    selector.FontSize,
                    selector.Foreground,
                    dpi);
                return text.Width;
            })
            .DefaultIfEmpty(0)
            .Max();

        selector.Width = Math.Ceiling(maxTextWidth + selector.Padding.Left + selector.Padding.Right + 36);
    }
}
