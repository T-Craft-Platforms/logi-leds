using System.Globalization;
using System.Windows;
using LogiLeds.Models;

namespace LogiLeds.Views;

public sealed record TelemetryGameDialogResult(TelemetryGameSettings Game);

public partial class TelemetryGameDialog : Window
{
    private readonly TelemetryGameSettings _current;
    private readonly HashSet<int> _usedPorts;

    private TelemetryGameDialog(Window? owner, TelemetryGameSettings current, IEnumerable<int> usedPorts)
    {
        InitializeComponent();
        if (owner is not null) Owner = owner;
        _current = current;
        _usedPorts = usedPorts.ToHashSet();
        Heading.Text = $"Configure {current.Name}";
        SetFields(current);
    }

    public TelemetryGameDialogResult? Result { get; private set; }

    public static TelemetryGameDialogResult? Show(Window? owner, TelemetryGameSettings current,
        IEnumerable<int> usedPorts)
    {
        var dialog = new TelemetryGameDialog(owner, current, usedPorts);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void SetFields(TelemetryGameSettings game)
    {
        AddressInput.Text = game.BindAddress;
        PortInput.Text = game.Port.ToString(CultureInfo.InvariantCulture);
        MaxRpmInput.Text = game.MaxRpm.ToString(CultureInfo.InvariantCulture);
        MaxRpmRow.Visibility = game.Game == TelemetryGame.BeamNg ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            !int.TryParse(MaxRpmInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRpm))
        {
            ErrorText.Text = "Enter valid numbers for UDP port and maximum RPM.";
            return;
        }

        var game = _current with { BindAddress = AddressInput.Text.Trim(), Port = port, MaxRpm = maxRpm };
        if (!game.TryValidate(out var error))
        {
            ErrorText.Text = error;
            return;
        }

        if (_usedPorts.Contains(port))
        {
            ErrorText.Text = "Choose a different UDP port for each game.";
            return;
        }

        Result = new TelemetryGameDialogResult(game);
        DialogResult = true;
    }
}