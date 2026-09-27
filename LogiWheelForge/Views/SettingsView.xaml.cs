using System.Windows;
using LogiWheelForge.Models;
using LogiWheelForge.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiWheelForge.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void GameEnabled_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel ||
            sender is not CheckBox { Tag: TelemetryGameSettings game } checkBox)
            return;

        if (!viewModel.SetGameEnabled(game, checkBox.IsChecked == true)) checkBox.IsChecked = true;
    }

    private void ConfigureGame_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel &&
            sender is Button { Tag: TelemetryGameSettings game })
            viewModel.ConfigureGame(game);
    }
}