using System.Windows;
using HeliumSecure.Core.Profiles;

namespace HeliumSecure.Views;

public partial class RenameProfileWindow : Window
{
    private readonly ProfileManager _profileManager;
    private readonly Guid _profileId;

    public RenameProfileWindow(ProfileManager profileManager, Guid profileId, string currentName)
    {
        InitializeComponent();
        _profileManager = profileManager;
        _profileId = profileId;
        ProfileNameBox.Text = currentName;
        ProfileNameBox.SelectAll();
        ProfileNameBox.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string newName = ProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ErrorMessage.Text = "Profile name cannot be empty.";
            ErrorMessage.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _profileManager.RenameProfile(_profileId, newName);
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
