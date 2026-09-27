using System.Windows;
using LogiWheelForge.Controls;

namespace LogiWheelForge.Views;

public partial class ProfileNameDialog : Window
{
    private ProfileNameDialog(Window? owner)
    {
        InitializeComponent();
        OwnedWindowDimmer.Attach(this);
        if (owner is not null) Owner = owner;
        Loaded += (_, _) =>
        {
            NameInput.Focus();
            CreateButton.IsEnabled = true;
        };
    }

    public string ProfileName => NameInput.Text.Trim();

    public static string? Show(Window? owner)
    {
        var dialog = new ProfileNameDialog(owner);
        return dialog.ShowDialog() == true ? dialog.ProfileName : null;
    }

    private void Create_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text))
        {
            ErrorText.Text = "Enter a profile name.";
            NameInput.Focus();
            return;
        }

        DialogResult = true;
    }
}