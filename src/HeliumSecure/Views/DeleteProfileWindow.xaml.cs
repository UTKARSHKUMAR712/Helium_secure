using System.Windows;
using HeliumSecure.Core.Profiles;
using HeliumSecure.Helpers;

namespace HeliumSecure.Views;

public partial class DeleteProfileWindow : Window
{
    private readonly ProfileManager _profileManager;
    private readonly Guid _profileId;

    public DeleteProfileWindow(ProfileManager profileManager, Guid profileId, string profileName)
    {
        InitializeComponent();
        _profileManager = profileManager;
        _profileId = profileId;

        WindowThemeHelper.EnableDarkTitleBar(this);

        WarningText.Text = $"Are you sure you want to permanently delete profile '{profileName}'? All encrypted cookies, history, and vault files will be destroyed.";
        PasswordBox.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        string password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ErrorMessage.Text = "Please enter the password to confirm deletion.";
            ErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _profileManager.DeleteProfile(_profileId, password);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorMessage.Text = ex.Message;
            ErrorMessage.Visibility = Visibility.Visible;
        }
    }
}
