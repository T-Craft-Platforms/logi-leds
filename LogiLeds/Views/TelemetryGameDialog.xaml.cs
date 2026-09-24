using System.Globalization;
using System.Windows;
using LogiLeds.Models;

namespace LogiLeds.Views;

public sealed record TelemetryGameDialogResult(TelemetryGameSettings? Game, bool Remove = false);

public partial class TelemetryGameDialog : Window
{
    private readonly HashSet<int> _usedPorts;
    private readonly bool _isNew;

    private TelemetryGameDialog(Window? owner, TelemetryGameSettings? current,
        IReadOnlyList<TelemetryGame> available, IEnumerable<int> usedPorts, bool canRemove)
    {
        InitializeComponent();
        if (owner is not null) Owner = owner;
        _isNew = current is null;
        _usedPorts = usedPorts.ToHashSet();
        Heading.Text = _isNew ? "Add game" : $"Manage {current!.Name}";
        GameSelector.ItemsSource = available.Select(game => game == TelemetryGame.Forza
            ? TelemetryGameSettings.DefaultForza : TelemetryGameSettings.DefaultBeamNg).ToArray();
        GameSelector.IsEnabled = _isNew;
        GameSelector.SelectedIndex = 0;
        if (current is not null) SetFields(current);
        RemoveButton.Visibility = canRemove ? Visibility.Visible : Visibility.Collapsed;
    }

    public TelemetryGameDialogResult? Result { get; private set; }

    public static TelemetryGameDialogResult? Show(Window? owner, TelemetryGameSettings? current,
        IReadOnlyList<TelemetryGame> available, IEnumerable<int> usedPorts, bool canRemove = false)
    {
        var dialog = new TelemetryGameDialog(owner, current, available, usedPorts, canRemove);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void GameSelector_OnSelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GameSelector.SelectedItem is not TelemetryGameSettings game) return;
        if (_isNew) SetFields(game);
        MaxRpmRow.Visibility = game.Game == TelemetryGame.BeamNg ? Visibility.Visible : Visibility.Collapsed;
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
        if (GameSelector.SelectedItem is not TelemetryGameSettings choice ||
            !int.TryParse(PortInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            !int.TryParse(MaxRpmInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRpm))
        {
            ErrorText.Text = "Enter valid numbers for UDP port and maximum RPM.";
            return;
        }

        var game = choice with { BindAddress = AddressInput.Text.Trim(), Port = port, MaxRpm = maxRpm };
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

    private void Remove_OnClick(object sender, RoutedEventArgs e)
    {
        Result = new TelemetryGameDialogResult(null, true);
        DialogResult = true;
    }
}
